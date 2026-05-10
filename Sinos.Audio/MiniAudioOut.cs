using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Hexa.NET.MiniAudio;
using NAudio.Wave;

namespace Sinos.Audio;

/// <summary>
/// MiniAudio バックエンドを使用した NAudio <see cref="IWavePlayer"/> の実装。
/// </summary>
/// <remarks>
/// <para>
/// 専用の Producer スレッドが <see cref="IWaveProvider"/> からデータを読み取りSPSC リングバッファに書き込む。
/// MiniAudio のオーディオコールバックはリングバッファからデータを取り出して出力バッファにコピーするだけなので、コールバックスレッドをブロックしない。
/// </para>
/// <para>
/// 低レイテンシ動作のため、<see cref="Play"/> はリングバッファの半分が充填されてからデバイスを開始する（プリバッファリング）。
/// </para>
/// </remarks>
public unsafe class MiniAudioOut : IWavePlayer, IDisposable
{
    private readonly int _latencyMilliseconds;

    private const int S24MinValue = -8388608;  // -2^23
    private const int S24MaxValue = 8388607;   // 2^23 - 1
    private const byte U8Silence = 128;

    // ── Native device ─────────────────────────────────────────────────────

    private MaDevice* _nativeDevice;
    private GCHandle _selfHandle;

    // ── Ring buffer ───────────────────────────────────────────────────────

    private SpscRingBuffer? _ringBuffer;
    // コールバックが読み取った後に Producer スレッドへ通知するイベント。
    private ManualResetEventSlim? _producerWakeEvent;

    // ── Producer thread ───────────────────────────────────────────────────

    private Thread? _producerThread;
    private CancellationTokenSource? _producerCts;
    // プリバッファリング完了通知用。Play() がデバイス開始前に待機する。
    private volatile bool _prebufferReady;

    // ── Managed state ─────────────────────────────────────────────────────

    private IWaveProvider? _sourceProvider;
    private WaveFormat? _outputFormat;
    private MaFormat _outputMaFormat;
    private volatile PlaybackState _playbackState;
    private SynchronizationContext? _playbackSyncContext;

    private readonly Lock _lock = new();
    private bool _endOfStreamScheduled;
    private bool _playbackStoppedRaised;
    private bool _isDisposed;

    // ── IWavePlayer ───────────────────────────────────────────────────────

    /// <inheritdoc />
    public PlaybackState PlaybackState => _playbackState;

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
    /// <param name="latencyMs">レイテンシ（ミリ秒単位、1 以上）</param>
    public MiniAudioOut(int latencyMs = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(latencyMs, 1);
        _latencyMilliseconds = latencyMs;
    }

    // ── IWavePlayer methods ───────────────────────────────────────────────

    /// <inheritdoc />
    public void Init(IWaveProvider waveProvider)
    {
        ArgumentNullException.ThrowIfNull(waveProvider);

        if (this._playbackState != PlaybackState.Stopped)
            throw new InvalidOperationException("Cannot call Init while playing or paused.");

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

        // 一時停止からの再開：デバイスは既に動作しているので状態遷移のみ。
        if (this._playbackState == PlaybackState.Paused)
        {
            this._playbackState = PlaybackState.Playing;
            return;
        }

        // Stopped → Playing
        this._playbackState = PlaybackState.Playing;
        try
        {
            lock (this._lock)
            {
                this._endOfStreamScheduled = false;
                this._playbackStoppedRaised = false;
                this.StartPlayback();
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
        // デバイスのコールバックは動作させたまま、無音を出力する。
        if (this._playbackState == PlaybackState.Playing)
            this._playbackState = PlaybackState.Paused;
    }

    /// <inheritdoc />
    public void Stop()
    {
        if (this._playbackState == PlaybackState.Stopped)
            return;

        this._playbackState = PlaybackState.Stopped;
        this.StopPlayback();
        this.RaisePlaybackStopped(null);
    }

    // ── Dispose pattern ───────────────────────────────────────────────────

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>このインスタンスが使用するリソースを解放する。</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (this._isDisposed) return;
        this._isDisposed = true;

        this._playbackState = PlaybackState.Stopped;
        this.StopPlayback();
    }

    ~MiniAudioOut() => Dispose(false);

    // ── Playback lifecycle ────────────────────────────────────────────────

    /// <summary>
    /// Producer スレッドとデバイスを起動する。<c>_lock</c> 保持下で呼び出すこと。
    /// </summary>
    private void StartPlayback()
    {
        var format = this._outputFormat!;

        // リングバッファサイズ = latencyMs × 2 相当のバイト数を 2 の累乗に切り上げ。
        // これにより 4 周期分のデータを格納できる。
        int rawSize = format.AverageBytesPerSecond * this._latencyMilliseconds * 2 / 1000;
        int bufferSize = SpscRingBuffer.NextPowerOfTwo(rawSize < 2 ? 2 : rawSize);

        this._ringBuffer = new SpscRingBuffer(bufferSize);
        this._producerWakeEvent = new ManualResetEventSlim(true);
        this._prebufferReady = false;

        this._producerCts = new CancellationTokenSource();
        this._producerThread = new Thread(this.ProducerLoop)
        {
            IsBackground = true,
            Priority = ThreadPriority.AboveNormal,
            Name = "MiniAudioOut2.Producer",
        };
        this._producerThread.Start();

        // プリバッファリング：リングバッファが半分埋まるまで待ってからデバイスを開始する。
        // タイムアウトは latencyMs × 2。
        int timeoutMs = _latencyMilliseconds * 2;
        int elapsed = 0;
        const int spinIntervalMs = 1;
        while (!this._prebufferReady && elapsed < timeoutMs)
        {
            Thread.Sleep(spinIntervalMs);
            elapsed += spinIntervalMs;
        }

        this.OpenDevice();
    }

    /// <summary>
    /// Producer スレッドとデバイスを停止する。
    /// </summary>
    private void StopPlayback()
    {
        // Producer スレッドをキャンセルしてから Join する。
        var cts = this._producerCts;
        if (cts is not null)
        {
            cts.Cancel();
            this._producerWakeEvent?.Set();
            this._producerThread?.Join();
            cts.Dispose();
            this._producerCts = null;
            this._producerThread = null;
        }

        lock (this._lock)
        {
            this.CloseDeviceCore();
        }

        this._producerWakeEvent?.Dispose();
        this._producerWakeEvent = null;
        this._ringBuffer = null;
    }

    // ── MiniAudio device lifecycle ────────────────────────────────────────

    private void OpenDevice()
    {
        var format = this._outputFormat!;

        this._selfHandle = GCHandle.Alloc(this);

        var config = MiniAudio.DeviceConfigInit(MaDeviceType.Playback);
        config.SampleRate = (uint)format.SampleRate;
        config.Playback.Format = this._outputMaFormat;
        config.Playback.Channels = (uint)format.Channels;
        // コールバック周期をレイテンシの半分に設定する。
        config.PeriodSizeInMilliseconds = (uint)(this._latencyMilliseconds / 2);
        config.PUserData = (void*)GCHandle.ToIntPtr(this._selfHandle);
        delegate* unmanaged[Cdecl]<MaDevice*, void*, void*, uint, void> cb = &DataCallback;
        config.DataCallback = (void*)cb;

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
            if (this._selfHandle.IsAllocated)
                this._selfHandle.Free();
            throw;
        }
    }

    private void CloseDeviceCore()
    {
        if (this._nativeDevice == null)
            return;

        MiniAudio.DeviceUninit(ref *this._nativeDevice);
        NativeMemory.Free(this._nativeDevice);
        this._nativeDevice = null;

        if (this._selfHandle.IsAllocated)
            this._selfHandle.Free();
    }

    // ── Producer thread ───────────────────────────────────────────────────

    private void ProducerLoop()
    {
        var ct = this._producerCts!.Token;
        var format = this._outputFormat!;

        // 1 コールバック周期分（periodSize の 2 倍）のバッファを Producer の読み取り単位とする。
        int readChunkBytes = format.AverageBytesPerSecond * _latencyMilliseconds / 1000;
        // BlockAlign の倍数に揃える。
        readChunkBytes = (readChunkBytes / format.BlockAlign) * format.BlockAlign;
        if (readChunkBytes < format.BlockAlign)
            readChunkBytes = format.BlockAlign;

        byte[] readBuf = new byte[readChunkBytes];

        while (!ct.IsCancellationRequested)
        {
            var ringBuffer = this._ringBuffer;
            var wakeEvent = this._producerWakeEvent;
            if (ringBuffer is null || wakeEvent is null)
                break;

            // 一時停止中は CPU を使わず待機する（コールバックは無音を出力し続ける）。
            if (this._playbackState == PlaybackState.Paused)
            {
                wakeEvent.Reset();
                wakeEvent.Wait(ct);
                continue;
            }

            int available = ringBuffer.AvailableWrite;
            if (available < format.BlockAlign)
            {
                // バッファが満杯：コールバックの読み取りを待つ。
                wakeEvent.Reset();
                wakeEvent.Wait(ct);
                continue;
            }

            int toRead = readChunkBytes < available ? readChunkBytes : available;
            toRead = (toRead / format.BlockAlign) * format.BlockAlign;
            if (toRead < format.BlockAlign)
            {
                wakeEvent.Reset();
                wakeEvent.Wait(ct);
                continue;
            }

            int bytesRead;
            try
            {
                bytesRead = this._sourceProvider!.Read(readBuf, 0, toRead);
            }
            catch (Exception ex)
            {
                // Read() で例外が発生した場合は再生を終了する。
                this.ScheduleEndOfStream(ex);
                return;
            }

            if (bytesRead > 0)
            {
                float volume = this.Volume;
                if (volume != 1.0f)
                    ApplyVolume(readBuf.AsSpan(0, bytesRead), this._outputMaFormat, volume);

                ringBuffer.Write(readBuf.AsSpan(0, bytesRead));
            }

            // プリバッファリング通知：リングバッファの半分以上が埋まったらデバイス開始を許可する。
            if (!this._prebufferReady && ringBuffer.AvailableRead >= ringBuffer.Capacity / 2)
                this._prebufferReady = true;

            if (bytesRead == 0 && this._playbackState == PlaybackState.Playing)
            {
                // EOS：残りのデータが再生されるまで少し待ってからシャットダウンする。
                this.ScheduleEndOfStream(null);
                return;
            }
        }
    }

    // ── Audio callback (MiniAudio audio thread) ───────────────────────────

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static void DataCallback(MaDevice* device, void* outputBuffer, void* inputBuffer, uint frameCount)
    {
        var handle = GCHandle.FromIntPtr((nint)device->PUserData);
        if (handle.IsAllocated && handle.Target is MiniAudioOut @this)
            @this.ProvideAudioData(outputBuffer, frameCount);
    }

    private void ProvideAudioData(void* outputBuffer, uint frameCount)
    {
        var format = _outputFormat;
        var ringBuffer = _ringBuffer;
        if (format == null || ringBuffer == null)
            return;

        int totalBytes = (int)frameCount * format.BlockAlign;

        if (_playbackState == PlaybackState.Paused)
        {
            WriteSilence(outputBuffer, totalBytes, _outputMaFormat);
            return;
        }

        // リングバッファからスタックに読み取り、出力バッファにコピーする。
        // スタックアロケーションを避けるため、出力バッファに直接書き込む Span を使用。
        var outSpan = new Span<byte>(outputBuffer, totalBytes);
        int bytesRead = ringBuffer.Read(outSpan);

        // アンダーラン時は残りを無音で埋める（再生は停止しない）。
        if (bytesRead < totalBytes)
            WriteSilence((byte*)outputBuffer + bytesRead, totalBytes - bytesRead, this._outputMaFormat);

        // リングバッファに空きができたので Producer スレッドを起床させる。
        this._producerWakeEvent?.Set();
    }

    // ── EOS handling ──────────────────────────────────────────────────────

    private void ScheduleEndOfStream(Exception? exception)
    {
        if (Interlocked.Exchange(ref this._endOfStreamScheduled, true))
            return;

        this._playbackState = PlaybackState.Stopped;
        ThreadPool.QueueUserWorkItem(_ =>
        {
            lock (this._lock)
            {
                if (!this._endOfStreamScheduled)
                    return;

                this.CloseDeviceCore();
            }
            this.RaisePlaybackStopped(exception);
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

    // ── Static helpers ────────────────────────────────────────────────────

    /// <summary><see cref="WaveFormat"/> を <see cref="MaFormat"/> に変換する。</summary>
    /// <exception cref="NotSupportedException">対応していないフォーマットの場合。</exception>
    private static MaFormat GetMaFormat(WaveFormat waveFormat)
    {
        if (waveFormat.Encoding == WaveFormatEncoding.IeeeFloat && waveFormat.BitsPerSample == 32)
            return MaFormat.F32;

        if (waveFormat.Encoding == WaveFormatEncoding.Pcm)
        {
            return waveFormat.BitsPerSample switch
            {
                8  => MaFormat.U8,
                16 => MaFormat.S16,
                24 => MaFormat.S24,
                32 => MaFormat.S32,
                _  => throw new NotSupportedException($"Unsupported PCM bit depth: {waveFormat.BitsPerSample} bit."),
            };
        }

        throw new NotSupportedException(
            $"Unsupported wave format: {waveFormat.Encoding} ({waveFormat.BitsPerSample} bit).");
    }

    private static void WriteSilence(void* destination, int byteCount, MaFormat format)
    {
        if (format == MaFormat.U8)
            NativeMemory.Fill(destination, (nuint)byteCount, U8Silence);
        else
            NativeMemory.Clear(destination, (nuint)byteCount);
    }

    /// <summary>
    /// オーディオバッファに音量を適用する。フォーマットに応じた飽和処理を行う。
    /// </summary>
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
                    buffer[i]     = (byte)(scaled & 0xFF);
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
