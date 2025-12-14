using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Cysharp.Diagnostics;
using Microsoft.Extensions.Logging;
using Sinos.Components;
using Sinos.Data.Projects;

namespace Sinos.Services;

public class NeutrinoCommonService(ILogger<NeutrinoCommonService> logger)
{
    private ILogger<NeutrinoCommonService> _logger = logger;

    /// <summary>標準出力される進捗情報をパースするための正規表現</summary>
    private static readonly Regex ProgressRegex = new(@"^.+Progress\s*=\s*(?<progress>\d+)\s*%.+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// NEUTRINOを実行する
    /// </summary>
    /// <param name="command">実行ファイル</param>
    /// <param name="args">コマンドライン引数</param>
    /// <param name="workdir">作業ディレクトリ</param>
    /// <param name="progress">進捗通知</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns><see cref="Task"/></returns>
    /// <exception cref="NeutrinoExecuteException">実行失敗情報</exception>
    public Task Execute(string command, IEnumerable<string> args = null, string? workdir = null, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
        => this.Execute(Guid.CreateVersion7(), command, args, workdir, progress, cancellationToken);

    /// <summary>
    /// NEUTRINOを実行する
    /// </summary>
    /// <param name="executionId">実行時識別ID</param>
    /// <param name="command">実行ファイル</param>
    /// <param name="args">コマンドライン引数</param>
    /// <param name="workdir">作業ディレクトリ</param>
    /// <param name="progress">進捗通知</param>
    /// <param name="cancellationToken">CancellationToken</param>
    /// <returns><see cref="Task"/></returns>
    /// <exception cref="NeutrinoExecuteException">実行失敗情報</exception>
    public async Task Execute(Guid executionId, string command, IEnumerable<string>? args = null, string? workdir = null, IProgress<ProgressReport>? progress = null, CancellationToken cancellationToken = default)
    {
        // 実行開始から終了までの一連の流れを特定するための識別子
        var logPrefix = $"[Process: {executionId:N}]";
        var logger = this._logger;

        // 実行開始ログ
        logger.LogDebug($"{logPrefix} === START NEUTRINO ===");
        logger.LogDebug($"{logPrefix} Pwd: {workdir}");
        logger.LogDebug($"{logPrefix} Execute: {command}\t{string.Join("\t", args ?? [])}");

        // 出力情報を保持しておく
        StringBuilder output = new();

        var lockObj = new Lock();
        void OnConsoleWrite(string line)
        {
            lock (lockObj)
                output.AppendLine(line);
        }

        var processInfo = new ProcessStartInfo()
        {
            FileName = command,
            WorkingDirectory = workdir,
        };
        foreach (var arg in args ?? [])
            processInfo.ArgumentList.Add(arg);

        try
        {
            var (_, stdout, stderr) = ProcessX.GetDualAsyncEnumerable(processInfo);

            var stdoutTask = this.ReadConsoleWriteWithProgress(logPrefix, stdout, progress, OnConsoleWrite, cancellationToken);
            var stderrTask = this.ReadConsoleWrite(logPrefix, stderr, OnConsoleWrite, cancellationToken);

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (ProcessErrorException pee)
        {
            // 実行失敗時
            logger.LogWarning(pee, $"{logPrefix} Neutrino execution failed.");

            progress?.Report(new(ProgressReportType.Error, null, 100));

            throw new NeutrinoExecuteException(
                command, workdir, args, pee.ExitCode, output.ToString(), pee);
        }
        finally
        {
            // 実行終了ログ
            logger.LogDebug($"{logPrefix} === END NEUTRINO ===");
        }
    }

    private async Task ReadConsoleWriteWithProgress(string logPrefix, IAsyncEnumerable<string> enumerable, IProgress<ProgressReport>? progress, Action<string> onConsoleWriteLine, CancellationToken cancellationToken)
            {
        bool isInitializing = true;

        await foreach (var line in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    this._logger.LogTrace($"{logPrefix} {line}");
            onConsoleWriteLine.Invoke(line);

                    double? progressValue = null;

                    var m = ProgressRegex.Match(line);
                    if (m.Success)
                    {
                        isInitializing = false;
                        progressValue = double.Parse(m.Groups["progress"].Value);
                    }

                    progress?.Report(new(isInitializing ? ProgressReportType.Indeterminate : ProgressReportType.InProgress, line, progressValue));
                }
                }

    private async Task ReadConsoleWrite(string logPrefix, IAsyncEnumerable<string> enumerable, Action<string> onConsoleWriteLine, CancellationToken cancellationToken)
        {
        await foreach (var line in enumerable.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            this._logger.LogWarning($"{logPrefix} {line}");
            onConsoleWriteLine.Invoke(line);
        }
    }
}
