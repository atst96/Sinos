using System.Runtime.InteropServices;

namespace Sinos.Audio;

/// <summary>
/// Single-Producer Single-Consumer のロックフリーリングバッファ。
/// </summary>
/// <remarks>
/// <para>
/// Producer（書き込み側）と Consumer（読み取り側）がそれぞれ別スレッドで動作することを前提とする。
/// 複数の Producer または複数の Consumer を使用する場合はスレッドセーフではない。
/// </para>
/// <para>
/// キャッシュラインのフォールスシェアリングを防ぐため、<c>_head</c> と <c>_tail</c> は 128 バイトのパディングで分離している。
/// </para>
/// </remarks>
internal sealed class SpscRingBuffer
{
    // 64 バイトのキャッシュライン × 2 = 128 バイトで head と tail を分離する。
    // volatile は付与せず、Volatile.Read / Volatile.Write で明示的にメモリバリアを挿入する。
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct PaddedIndex
    {
        [FieldOffset(0)]
        public int Value;
    }

    private readonly byte[] _buffer;
    private readonly int _mask; // サイズが 2^n なので &演算で剰余を取れる

    // _head: Consumer (Read) が更新する読み取り位置
    private PaddedIndex _head;
    // _tail: Producer (Write) が更新する書き込み位置
    private PaddedIndex _tail;

    /// <summary>バッファの容量（バイト単位）。</summary>
    public int Capacity => _buffer.Length;

    /// <summary>読み取り可能なバイト数。</summary>
    public int AvailableRead
    {
        get
        {
            int tail = Volatile.Read(ref _tail.Value);
            int head = Volatile.Read(ref _head.Value);
            return (tail - head) & _mask;
        }
    }

    /// <summary>書き込み可能なバイト数。</summary>
    public int AvailableWrite => Capacity - 1 - AvailableRead;

    /// <param name="capacityPowerOfTwo">
    /// バッファの容量（バイト単位）。2 の累乗である必要がある。
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="capacityPowerOfTwo"/> が 2 以上の 2 の累乗でない場合。
    /// </exception>
    public SpscRingBuffer(int capacityPowerOfTwo)
    {
        if (capacityPowerOfTwo < 2 || (capacityPowerOfTwo & (capacityPowerOfTwo - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(capacityPowerOfTwo), "Must be a power of two and >= 2.");

        _buffer = new byte[capacityPowerOfTwo];
        _mask = capacityPowerOfTwo - 1;
    }

    /// <summary>
    /// <paramref name="source"/> のデータをバッファに書き込む。
    /// </summary>
    /// <returns>実際に書き込んだバイト数。空きが不足している場合は <paramref name="source"/> より少なくなる。</returns>
    public int Write(ReadOnlySpan<byte> source)
    {
        int available = AvailableWrite;
        if (available <= 0 || source.IsEmpty)
            return 0;

        int toWrite = source.Length < available ? source.Length : available;
        int tail = _tail.Value;
        int size = _buffer.Length;

        int firstChunk = size - tail;
        if (firstChunk >= toWrite)
        {
            source[..toWrite].CopyTo(_buffer.AsSpan(tail));
        }
        else
        {
            source[..firstChunk].CopyTo(_buffer.AsSpan(tail));
            source[firstChunk..toWrite].CopyTo(_buffer.AsSpan(0));
        }

        // _head の読み取りが完了する前に _tail を更新しないよう書き込みフェンスを挿入する。
        Volatile.Write(ref _tail.Value, (tail + toWrite) & _mask);
        return toWrite;
    }

    /// <summary>
    /// バッファから <paramref name="destination"/> にデータを読み取る。
    /// </summary>
    /// <returns>実際に読み取ったバイト数。データが不足している場合は <paramref name="destination"/> より少なくなる。</returns>
    public int Read(Span<byte> destination)
    {
        int available = AvailableRead;
        if (available <= 0 || destination.IsEmpty)
            return 0;

        int toRead = destination.Length < available ? destination.Length : available;
        int head = _head.Value;
        int size = _buffer.Length;

        int firstChunk = size - head;
        if (firstChunk >= toRead)
        {
            _buffer.AsSpan(head, toRead).CopyTo(destination);
        }
        else
        {
            _buffer.AsSpan(head, firstChunk).CopyTo(destination);
            _buffer.AsSpan(0, toRead - firstChunk).CopyTo(destination[firstChunk..]);
        }

        // _tail の読み取りが完了する前に _head を更新しないよう書き込みフェンスを挿入する。
        Volatile.Write(ref _head.Value, (head + toRead) & _mask);
        return toRead;
    }

    /// <summary>
    /// バッファをクリアし、読み取り位置・書き込み位置をリセットする。
    /// </summary>
    /// <remarks>
    /// Producer と Consumer が両方停止している状態で呼び出すこと。
    /// </remarks>
    public void Clear()
    {
        Volatile.Write(ref _head.Value, 0);
        Volatile.Write(ref _tail.Value, 0);
    }

    /// <summary>
    /// <paramref name="minimumCapacity"/> 以上の容量を持つ 2 の累乗の値を返す。
    /// </summary>
    public static int NextPowerOfTwo(int minimumCapacity)
    {
        if (minimumCapacity < 2)
            return 2;

        int value = minimumCapacity - 1;
        value |= value >> 1;
        value |= value >> 2;
        value |= value >> 4;
        value |= value >> 8;
        value |= value >> 16;
        return value + 1;
    }
}
