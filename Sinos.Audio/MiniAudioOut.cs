using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.MiniAudio;
using NAudio.Wave;

namespace Sinos.Audio;

/// <summary>
/// MiniAudio バックエンドを使用した NAudio <see cref="IWavePlayer"/> の実装。
/// </summary>
public unsafe class MiniAudioOut : IWavePlayer, IDisposable
{
    private readonly int _latencyMilliseconds;

    private const int S24MinValue = -8388608;  // -2^23
    private const int S24MaxValue = 8388607;   // 2^23 - 1
    private const byte U8Silence = 128;

    // ── Native device ─────────────────────────────────────────────────────

    private MaDevice* _nativeDevice;
    private GCHandle _selfHandle;
    // _sourceProviderから音声データを読み取るための1秒分のバッファ。
    private byte[]? _pcmReadBuffer;

    // ── Managed state ─────────────────────────────────────────────────────

    private IWaveProvider? _sourceProvider;
    private WaveFormat? _outputFormat;
    private MaFormat _outputMaFormat;
    private volatile PlaybackState _playbackState;
    private SynchronizationContext? _playbackSyncContext;

    // 複数スレッドからの呼び出し時に状態が競合しないことを保証するためにロックを設ける
    private readonly Lock _lock = new();
    private bool _endOfStreamScheduled;
    private bool _playbackStoppedRaised;
    private bool _isDisposed;

    // ── IWavePlayer ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public PlaybackState PlaybackState => this._playbackState;

    /// <inheritdoc />
    public float Volume { get; set; } = 1.0f;

    /// <inheritdoc />
    public WaveFormat OutputWaveFormat =>
        this._outputFormat ?? throw new InvalidOperationException("Not initialized.");

    /// <inheritdoc />
    public event EventHandler<StoppedEventArgs>? PlaybackStopped;

    // ── Construction ──────────────────────────────────────────────────────

    /// <summary>
    /// <see cref="MiniAudioOut"/> クラスの新しいインスタンスを初期化する。
    /// </summary>
    /// <param name="latencyMs">レイテンシ（ミリ秒単位）</param>
    public MiniAudioOut(int latencyMs = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latencyMs, 1);
        this._latencyMilliseconds = latencyMs;
    }

    // ── IWavePlayer methods ───────────────────────────────────────────────

    /// <inheritdoc />
    public void Init(IWaveProvider waveProvider)
    {
        ArgumentNullException.ThrowIfNull(waveProvider);

        if (this._playbackState != PlaybackState.Stopped)
            throw new InvalidOperationException("Cannot call Init while playing.");

        this._sourceProvider = waveProvider;
        this._outputFormat = waveProvider.WaveFormat;
        this._outputMaFormat = GetMaFormat(this._outputFormat);
        this._playbackSyncContext = SynchronizationContext.Current;
    }

    /// <inheritdoc />
    public void Play()
    {
        if (this._sourceProvider is null)
            throw new InvalidOperationException("Must call Init before Play.");

        if (this._playbackState == PlaybackState.Playing)
            return;

        if (this._playbackState == PlaybackState.Paused)
        {
            this._playbackState = PlaybackState.Playing;
            return;
        }

        this._playbackState = PlaybackState.Playing;
        try
        {
            lock (this._lock)
            {
                this._endOfStreamScheduled = false;
                this._playbackStoppedRaised = false;
                this.OpenDevice();
            }
        }
        catch
        {
            this._playbackState = PlaybackState.Stopped;
            throw;
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        // デバイスのコールバックは実行状態にしておく。一時停止中は無音出力する。
        if (this._playbackState == PlaybackState.Playing)
            this._playbackState = PlaybackState.Paused;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (this._playbackState == PlaybackState.Stopped)
            return;

        this._playbackState = PlaybackState.Stopped;
        this.CloseDevice();
        this.RaisePlaybackStopped(null);
    }

    // ── Dispose pattern ───────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        this.Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// このインスタンスによって使用されるリソースを解放する。
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (this._isDisposed) return;
        this._isDisposed = true;

        this._playbackState = PlaybackState.Stopped;
        this.CloseDevice();
    }

    ~MiniAudioOut() => this.Dispose(false);

    // ── MiniAudio device lifecycle ────────────────────────────────────────

    private void OpenDevice()
    {
        var format = this._outputFormat!;

        // コールバック内で使用される音声データ読み取りバッファを事前割り当てる。
        this._pcmReadBuffer = new byte[format.AverageBytesPerSecond];

        this._selfHandle = GCHandle.Alloc(this);

        var config = MiniAudio.DeviceConfigInit(MaDeviceType.Playback);
        config.SampleRate = (uint)format.SampleRate;
        config.Playback.Format = this._outputMaFormat;
        config.Playback.Channels = (uint)format.Channels;
        // コールバック周期を要求されたレイテンシーの半分に設定する
        config.PeriodSizeInMilliseconds = (uint)(this._latencyMilliseconds / 2);
        config.PUserData = (void*)GCHandle.ToIntPtr(this._selfHandle);
        delegate* unmanaged[Cdecl]<MaDevice*, void*, void*, uint, void> dataCallback = &DataCallback;
        config.DataCallback = (void*)dataCallback;

        this._nativeDevice = (MaDevice*)NativeMemory.AllocZeroed((nuint)sizeof(MaDevice));
        try
        {
            MaResult result;

            result = MiniAudio.DeviceInit(null, in config, ref *this._nativeDevice);
            if (result != MaResult.Success)
                throw new InvalidOperationException($"ma_device_init failed: {result}");

            result = MiniAudio.DeviceStart(ref *this._nativeDevice);
            if (result != MaResult.Success)
            {
                MiniAudio.DeviceUninit(ref *this._nativeDevice);
                throw new InvalidOperationException($"ma_device_start failed: {result}");
            }
        }
        catch
        {
            NativeMemory.Free(this._nativeDevice);
            this._nativeDevice = null;
            this._selfHandle.Free();
            throw;
        }
    }

    /// <summary>デバイスを破棄する。</summary>
    private void CloseDevice()
    {
        lock (this._lock)
        {
            this.CloseDeviceCore();
        }
    }

    /// <summary>デバイス終了処理を実行する。</summary>
    private void CloseDeviceCore()
    {
        if (this._nativeDevice == null)
            return;

        MiniAudio.DeviceUninit(ref *this._nativeDevice);
        NativeMemory.Free(this._nativeDevice);
        this._nativeDevice = null;

        if (this._selfHandle.IsAllocated)
            this._selfHandle.Free();

        this._pcmReadBuffer = null;
    }

    // ── Audio callback (miniaudio audio thread) ───────────────────────────

    /// <summary>
    /// MiniAudio の内部オーディオスレッドから呼び出される。
    /// <see cref="IWaveProvider"/> から 音声データを読み取り、<see cref="Volume"/>を適用して出力バッファーにコピーする。
    /// </summary>
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DataCallback(MaDevice* device, void* outputBuffer, void* inputBuffer, uint frameCount)
    {
        var handle = GCHandle.FromIntPtr((nint)device->PUserData);
        if (handle.IsAllocated && handle.Target is MiniAudioOut @this)
            @this.ProvideAudioData(outputBuffer, frameCount);
    }

    private void ProvideAudioData(void* outputBuffer, uint frameCount)
    {
        var format = this._outputFormat;
        var provider = this._sourceProvider;
        var pcmBuffer = this._pcmReadBuffer;
        if (format == null || provider == null || pcmBuffer == null)
            return;

        int totalBytes = (int)frameCount * format.BlockAlign;

        if (this._playbackState == PlaybackState.Paused)
        {
            WriteSilence(outputBuffer, totalBytes, this._outputMaFormat);
            return;
        }

        // _pcmReadBuffer に PCM データを読み取り、ネイティブ出力ポインターにコピーする。
        int toRead = Math.Min(totalBytes, pcmBuffer.Length);
        int bytesRead = provider.Read(pcmBuffer, 0, toRead);

        if (bytesRead > 0)
        {
            float volume = this.Volume;
            if (volume != 1.0f)
                ApplyVolume(pcmBuffer.AsSpan(0, bytesRead), this._outputMaFormat, volume);

            fixed (byte* src = pcmBuffer)
                Buffer.MemoryCopy(src, outputBuffer, totalBytes, bytesRead);
        }

        // 未入力フレームはサイレンスで埋める。
        if (bytesRead < totalBytes)
            WriteSilence((byte*)outputBuffer + bytesRead, totalBytes - bytesRead, this._outputMaFormat);

        // ストリーム終了: コールバック内から DeviceUninit を呼び出さないよう
        // (デッドロックを防ぐため) 非同期でシャットダウンをスケジュールする。
        if (bytesRead == 0 && this._playbackState == PlaybackState.Playing)
            this.ScheduleEndOfStream();
    }

    private void ScheduleEndOfStream()
    {
        // provider が 0 を返し続ける場合の重複スケジュール化を防ぐ。
        if (Interlocked.Exchange(ref this._endOfStreamScheduled, true))
            return;

        this._playbackState = PlaybackState.Stopped;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (this._lock)
            {
                // EOS がトリガーされた後に Play() が呼び出された場合、
                // _endOfStreamScheduled はリセットされている。このコールバックは古い。
                if (!this._endOfStreamScheduled)
                    return;

                this.CloseDeviceCore();
            }
            this.RaisePlaybackStopped(null);
        });
    }

    // ── Event helper ──────────────────────────────────────────────────────

    private void RaisePlaybackStopped(Exception? exception)
    {
        if (Interlocked.Exchange(ref this._playbackStoppedRaised, true))
            return;

        var handler = this.PlaybackStopped;
        if (handler == null)
            return;

        var args = new StoppedEventArgs(exception);
        if (this._playbackSyncContext != null)
            this._playbackSyncContext.Post(_ => handler(this, args), null);
        else
            handler(this, args);
    }

    /// <summary>
    /// <see cref="WaveFormat"/>を<see cref="MaFormat"/> に変換する。
    /// </summary>
    /// <param name="waveFormat">変換元の WaveFormat</param>
    /// <returns>対応する MaFormat</returns>
    /// <exception cref="NotSupportedException">対応していないフォーマットの場合</exception>
    private static MaFormat GetMaFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
            return MaFormat.F32;

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return waveFormat.BitsPerSample switch
            {
                8 => MaFormat.U8,
                16 => MaFormat.S16,
                24 => MaFormat.S24,
                32 => MaFormat.S32,
                _ => throw new NotSupportedException($"Unsupported PCM bit depth: {waveFormat.BitsPerSample} bit."),
            };
        }

        throw new NotSupportedException(
            $"Unsupported wave format: {waveFormat.Encoding} ({waveFormat.BitsPerSample} bit).");
    }

    /// <summary>
    /// 出力バッファに無音データを書き込む。
    /// </summary>
    /// <param name="destination">書き込み先ポインタ</param>
    /// <param name="byteCount">書き込むバイト数</param>
    /// <param name="format">オーディオフォーマット</param>
    private static void WriteSilence(void* destination, int byteCount, MaFormat format)
    {
        if (format == MaFormat.U8)
            NativeMemory.Fill(destination, (nuint)byteCount, U8Silence);
        else
            NativeMemory.Clear(destination, (nuint)byteCount);
    }

    /// <summary>
    /// オーディオバッファに音量を適用する。
    /// フォーマットに応じた飽和処理を行い、クリッピングを防ぐ。
    /// </summary>
    /// <param name="buffer">処理対象のバッファ</param>
    /// <param name="format">オーディオフォーマット</param>
    /// <param name="volume">適用する音量（0.0～1.0+）</param>
    private static void ApplyVolume(Span<byte> buffer, MaFormat format, float volume)
    {
        if (volume == 0.0f)
        {
            if (format == MaFormat.U8)
                buffer.Fill(U8Silence);
            else
                buffer.Clear();
            return;
        }

        switch (format)
        {
            case MaFormat.F32:
                {
                    var samples = MemoryMarshal.Cast<byte, float>(buffer);
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] *= volume;
                    break;
                }
            case MaFormat.S16:
                {
                    var samples = MemoryMarshal.Cast<byte, short>(buffer);
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = SaturateToS16((int)(samples[i] * volume));
                    break;
                }
            case MaFormat.S24:
                {
                    for (int i = 0, len = buffer.Length - 2; i < len; i += 3)
                    {
                        int sample = buffer[i] | (buffer[i + 1] << 8) | ((sbyte)buffer[i + 2] << 16);
                        int scaled = SaturateToS24((int)(sample * volume));
                        buffer[i] = (byte)(scaled & 0xFF);
                        buffer[i + 1] = (byte)((scaled >> 8) & 0xFF);
                        buffer[i + 2] = (byte)((scaled >> 16) & 0xFF);
                    }
                    break;
                }
            case MaFormat.S32:
                {
                    var samples = MemoryMarshal.Cast<byte, int>(buffer);
                    for (int i = 0; i < samples.Length; i++)
                        samples[i] = SaturateToS32((long)((double)samples[i] * volume));
                    break;
                }
            case MaFormat.U8:
                {
                    for (int i = 0; i < buffer.Length; i++)
                    {
                        int centered = buffer[i] - U8Silence;
                        buffer[i] = SaturateToU8((int)(centered * volume) + U8Silence);
                    }
                    break;
                }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static short SaturateToS16(int value)
        => value < short.MinValue ? short.MinValue : value > short.MaxValue ? short.MaxValue : (short)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SaturateToS24(int value)
        => value < S24MinValue ? S24MinValue : value > S24MaxValue ? S24MaxValue : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int SaturateToS32(long value)
        => value < int.MinValue ? int.MinValue : value > int.MaxValue ? int.MaxValue : (int)value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static byte SaturateToU8(int value)
        => value < byte.MinValue ? byte.MinValue : value > byte.MaxValue ? byte.MaxValue : (byte)value;
}
