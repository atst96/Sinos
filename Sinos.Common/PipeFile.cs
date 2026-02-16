using System.IO.Pipes;
using Sinos.Utils;

namespace Sinos;

using FwPath = Path;

/// <summary>
/// 名前付きパイプを使用してファイル出力内容を受け取るためのクラス<br />
/// 外部アプリケーションでパイプでは支障がある場合は<see cref="TempFile"/>クラスを使用する。
/// </summary>
public class PipeFile : Stream
{
    /// <summary>接尾辞</summary>
    private static readonly string _prefix = $"Sinos_{AppInstance.Instance.Id}";

    /// <summary>パイプのパスの接頭辞</summary>
    private static readonly string _pipePathPrefix = OperatingSystem.IsWindows() ? @"\\.\pipe\" : FwPath.GetTempPath();

    /// <summary>パイプ作成待機の待ち時間</summary>
    private const int WiatTime = 20;

    private string _pipeName;
    private NamedPipeServerStream _server;
    private readonly PipeDirection _direction;
    private readonly FileStream? _fileStream;
    private bool _disposed;
    private long _writtenLength;
    private Task _lastWriteTask = Task.CompletedTask;

    /// <summary>ファイルパス</summary>
    public string Path { get; }

    /// <<summary>ctor</summary>
    /// <param name="suffix">パイプ名の接尾辞</param>
    /// <param name="direction">パイプの方向</param>
    private PipeFile(string? suffix, PipeDirection direction)
    {
        this._direction = direction;

        // パイプ名を生成
        var pipeName = this._pipeName = $"{_prefix}_{IdUtil.RandomString(10)}{suffix}";

        // ファイルパス生成する
        // WindowsOSの場合は名前付きパイプ("\\.\pipe\パイプ名")、それ以外は"/tmp/パイプ名"
        this.Path = OperatingSystem.IsWindows()
            ? $"{_pipePathPrefix}{pipeName}"
            : FwPath.Combine(_pipePathPrefix, pipeName);

        var server = this._server = new(this._pipeName, direction, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

        if (direction is PipeDirection.Out or PipeDirection.InOut)
            server.ReadMode = PipeTransmissionMode.Byte;

        if (!OperatingSystem.IsWindows() && direction != PipeDirection.Out)
        {
            this._fileStream = new FileStream(this.Path, FileMode.Create, FileAccess.ReadWrite, FileShare.ReadWrite, 4096, FileOptions.Asynchronous);
            this._fileStream.SetLength(0);
        }

        this._writtenLength = 0;
    }

    /// <summary>
    /// 読み取り専用のファイルを作成する
    /// </summary>
    /// <param name="suffix">ファイル名の接尾辞</param>
    /// <returns></returns>
    public static PipeFile CreateReadOnly(string? suffix = null)
        => new(suffix, PipeDirection.Out);

    /// <summary>
    /// 書き込み専用のファイルを作成する
    /// </summary>
    /// <param name="suffix">ファイル名の接尾辞</param>
    /// <returns></returns>
    public static PipeFile CreateWriteOnly(string? suffix = null)
        => new(suffix, PipeDirection.In);

    /// <summary>
    /// 読み書き可能なファイルを作成する
    /// </summary>
    /// <param name="suffix<">ファイル名の接尾辞</param>
    /// <returns></returns>>
    public static PipeFile CreateReadWrite(string? suffix)
        => new(suffix, PipeDirection.InOut);

    /// <summary><inheritdoc/></summary>
    public override bool CanRead => OperatingSystem.IsWindows() ? this._server.CanRead : this._direction != PipeDirection.Out;

    /// <summary><inheritdoc/></summary>
    public override bool CanSeek => OperatingSystem.IsWindows() ? this._server.CanSeek : this._fileStream?.CanSeek == true;

    /// <summary><inheritdoc/></summary>
    public override bool CanWrite => OperatingSystem.IsWindows() ? this._server.CanWrite : this._direction != PipeDirection.In;

    /// <summary><inheritdoc/></summary>
    public override long Length
        => OperatingSystem.IsWindows() ? this._server.Length : this._fileStream?.Length ?? 0;

    /// <summary><inheritdoc/></summary>
    public override long Position
    {
        get => OperatingSystem.IsWindows() ? this._server.Position : this._fileStream?.Position ?? 0;
        set => this.Seek(value, SeekOrigin.Begin);
    }

    /// <summary><inheritdoc/></summary>
    public override void Flush()
    {
        if (OperatingSystem.IsWindows())
            this._server.Flush();
        else
            this._fileStream?.Flush();
    }

    /// <summary>接続を開始する</summary>
    private void WaitForConnection()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var server = this._server;

        if (!server.IsConnected)
            server.WaitForConnection();
    }

    /// <summary>接続を開始する</summary>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns>Task</returns>
    private async ValueTask WaitForConnectionAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows())
            return;

        var server = this._server;

        if (!server.IsConnected)
            await server.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task WaitForFileContentAsync(long requiredLength, CancellationToken cancellationToken)
    {
        var fileStream = this._fileStream ?? throw new InvalidOperationException();

        while (new FileInfo(this.Path).Length < requiredLength)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(WiatTime, cancellationToken).ConfigureAwait(false);
        }

        fileStream.Position = 0;
    }

    /// <summary><inheritdoc/></summary>
    public override int Read(byte[] buffer, int offset, int count)
    {
        if (OperatingSystem.IsWindows())
        {
            this.WaitForConnection();
            return this._server.Read(buffer, offset, count);
        }

        var fileStream = this._fileStream ?? throw new InvalidOperationException();
        this.WaitForFileContentAsync(count, CancellationToken.None).GetAwaiter().GetResult();
        return fileStream.Read(buffer, offset, count);
    }

    /// <summary><inheritdoc/></summary>
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            await this.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            return await this._server.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
        }

        var fileStream = this._fileStream ?? throw new InvalidOperationException();
        await this.WaitForFileContentAsync(count, cancellationToken).ConfigureAwait(false);
        return await fileStream.ReadAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
    }

    /// <summary><inheritdoc/></summary>
    public override long Seek(long offset, SeekOrigin origin)
    {
        if (OperatingSystem.IsWindows())
        {
            this.WaitForConnection();
            return this._server.Seek(offset, origin);
        }

        var fileStream = this._fileStream ?? throw new InvalidOperationException();
        return fileStream.Seek(offset, origin);
    }

    /// <summary><inheritdoc/></summary>
    public override void SetLength(long value)
    {
        if (OperatingSystem.IsWindows())
        {
            this.WaitForConnection();
            this._server.SetLength(value);
        }
        else
        {
            var fileStream = this._fileStream ?? throw new InvalidOperationException();
            fileStream.SetLength(value);
        }
    }

    /// <summary><inheritdoc/></summary>
    public override void Write(byte[] buffer, int offset, int count)
    {
        this._writtenLength = count;

        if (OperatingSystem.IsWindows())
        {
            this.WaitForConnection();
            this._server.Write(buffer, offset, count);
            this._lastWriteTask = Task.CompletedTask;
            return;
        }

        if (this._direction == PipeDirection.Out)
        {
            File.WriteAllBytes(this.Path, buffer.AsSpan(offset, count).ToArray());
            this._lastWriteTask = Task.CompletedTask;
        }
        else
        {
            var fileStream = this._fileStream ?? throw new InvalidOperationException();
            fileStream.Position = 0;
            fileStream.SetLength(0);
            fileStream.Write(buffer.AsSpan(offset, count));
            fileStream.Flush();
            this._lastWriteTask = Task.CompletedTask;
        }
    }

    /// <summary><inheritdoc/></summary>
    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        this._lastWriteTask = Task.CompletedTask;
        this.Write(buffer, offset, count);
    }

    /// <summary><inheritdoc/></summary>
    public override void Write(ReadOnlySpan<byte> buffer)
        => this.Write(buffer.ToArray(), 0, buffer.Length);

    /// <summary><inheritdoc/></summary>
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        => new(this.WriteAsync(buffer.ToArray(), 0, buffer.Length, cancellationToken));

    /// <summary><inheritdoc/></summary>
    public override void Close()
    {
        var server = this._server;

        if (server.IsConnected)
            server.Disconnect();

        base.Close();
    }

    /// <summary><inheritdoc/></summary>
    protected override void Dispose(bool disposing)
    {
        this._disposed = true;

        this._fileStream?.Dispose();

        this._server.Dispose();
        base.Dispose(disposing);
    }

    /// <summary>
    /// パイプが作成されるまで待機する
    /// </summary>
    /// <param name="pipeFiles">パイプリスト</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns></returns>
    public static async Task WaitForPipeReady(IEnumerable<PipeFile> pipeFiles, CancellationToken cancellationToken = default)
    {
        // パイプの一覧を取得する
        if (OperatingSystem.IsWindows())
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pipeFiles.Any(i => i._disposed))
                    throw new TaskCanceledException();

                foreach (var pipe in pipeFiles.Where(p => p._direction == PipeDirection.Out))
                    await pipe._lastWriteTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                var files = Directory.GetFiles(_pipePathPrefix, $"{_prefix}*");

                if (pipeFiles.All(i => files.Contains(i.Path) && (i._direction != PipeDirection.Out || new FileInfo(i.Path).Length >= i._writtenLength)))
                    return;

                await Task.Delay(WiatTime, cancellationToken).ConfigureAwait(false);
            }
        }
        else
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (pipeFiles.Any(i => i._disposed))
                    throw new TaskCanceledException();

                foreach (var pipe in pipeFiles.Where(p => p._direction == PipeDirection.Out))
                    await pipe._lastWriteTask.WaitAsync(cancellationToken).ConfigureAwait(false);

                if (pipeFiles.All(i => File.Exists(i.Path) && (i._direction != PipeDirection.Out || new FileInfo(i.Path).Length >= i._writtenLength)))
                    return;

                await Task.Delay(WiatTime, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
