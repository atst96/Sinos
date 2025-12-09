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

        // 実行開始ログ
        this._logger.LogTrace($"{logPrefix} === START NEUTRINO ===");
        this._logger.LogTrace($"{logPrefix} Pwd: {workdir}");
        this._logger.LogTrace($"{logPrefix} Execute: {command}\t{string.Join("\t", args ?? [])}");

        bool isInitializing = true;

        // 出力情報を保持しておく
        StringBuilder output = new();

        var psi = new ProcessStartInfo()
        {
            FileName = command,
            WorkingDirectory = workdir,
        };

        foreach (var arg in args ?? [])
            psi.ArgumentList.Add(arg);

        try
        {
            var (_, stdout, stderr) = ProcessX.GetDualAsyncEnumerable(psi);

            var stdoutTask = Task.Run(async () =>
            {
                await foreach (var line in stdout.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    this._logger.LogTrace($"{logPrefix} {line}");
                    output.AppendLine(line);

                    double? progressValue = null;

                    var m = ProgressRegex.Match(line);
                    if (m.Success)
                    {
                        isInitializing = false;
                        progressValue = double.Parse(m.Groups["progress"].Value);
                    }

                    progress?.Report(new(isInitializing ? ProgressReportType.Indeterminate : ProgressReportType.InProgress, line, progressValue));
                }
            });

            var stderrTask = Task.Run(async () =>
            {
                await foreach (var line in stderr.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    this._logger.LogTrace($"{logPrefix} {line}");
                    output.AppendLine(line);
                }
            });

            await Task.WhenAll(stdoutTask, stderrTask).ConfigureAwait(false);
        }
        catch (ProcessErrorException pee)
        {
            // 実行失敗時
            progress?.Report(new(ProgressReportType.Error, null, 100));

            this._logger.LogWarning(pee, $"{logPrefix} Neutrino execution failed.");

            throw new NeutrinoExecuteException(
                command, workdir, args, pee.ExitCode, output.ToString(), pee);
        }
        finally
        {
            // 実行終了ログ
            this._logger.LogTrace($"{logPrefix} === END NEUTRINO ===");
        }
    }
}
