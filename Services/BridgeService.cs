using MD.Platform.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MD.BRIDGE.Utils;
using LogModule;
using DevExpress.Mvvm.Native;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MD.BRIDGE.Services
{
    public class BridgeService
    {
        private TaskCompletionSource<bool> _completionSource;
        private CancellationTokenSource cts;

        public void Run()
        {
            Task.Run(() =>
            {
                _completionSource = new TaskCompletionSource<bool>();
                cts = new CancellationTokenSource();

                RunTasksAsync().GetAwaiter().GetResult();
            });
        }

        public void Stop()
        {
            cts?.Cancel();
        }

        public async Task WaitForCompletion()
        {
            if (_completionSource != null)
            {
                await _completionSource.Task; // 작업 완료까지 대기
            }
        }

        private async Task RunTasksAsync()
        {
            var tasks = new List<Task>
            {
                MonitoringLogTask(cts.Token),
            };

            await Task.WhenAll(tasks);
            _completionSource?.SetResult(true);
        }

        private async Task MonitoringLogTask(CancellationToken cancellationToken)
        {
            var productLogDirectories = SettingService.GetProductLogDictionaries();
            Logger.Info($"Start monitoringLogTask.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var kvp in productLogDirectories)
                    {
                        ProcessMonitoringLog(product: kvp.Key);
                    }

                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async void ProcessMonitoringLog(Product product)
        {

            var logDirectories = SettingService.GetLogDirectories(product);
            var offset = SettingService.GetProductOffset(product);

            DateTimeOffset now = DateTimeOffset.Now;

            /** Serach log files */
            var logFilePaths = logDirectories.SelectMany(logDirectory => GetLogFilePaths(logDirectory: logDirectory, start: offset, end: now));
            if (logFilePaths.Count() == 0)
            {
                return;
            }

            var pathToRecords = logFilePaths.ToDictionary(
                logFilePath => logFilePath,
                logFilePath => LogExctractorService.Extract(logFilePath, offset, now)
            );

            pathToRecords.ToList().ForEach(pathToLog =>
                Logger.Info($" - Log file name:{pathToLog.Key}, Records:{pathToLog.Value.Count()}")
            );

            /** Upload logs to server */
            bool isSuccess;
            if (pathToRecords.Count() == 0)
            {
                isSuccess = true;
            }
            else
            {
                isSuccess = await WebClientService.UploadLogs(
                    request: new WebClientService.UploadLogRequest(
                        product: product,
                        logs: pathToRecords.Select(kvp => new WebClientService.UploadLogRequest.Log(
                            filename: Path.GetFileName(kvp.Key),
                            records: kvp.Value
                        ))
                    )
                );
            }

            if (isSuccess)
            {
                await CleanUpMonitorLogFilesAsync(logDirectories);
                SettingService.SetProductOffset(product, now);
            }
            else
            {
                Logger.Debug("Fail to upload logs.");
            }
        }

        private IEnumerable<string> GetLogFilePaths(string logDirectory, DateTimeOffset start, DateTimeOffset end)
        {
            if (Directory.Exists(logDirectory) == false)
            {
                return new List<string>();
            }

            return Directory.GetFiles(logDirectory, searchPattern: "*.log", SearchOption.TopDirectoryOnly)
                .Where(filePath => filePath.Contains("_monitor"))
                .Where(filePath => new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero).IsBetween(start, end));
        }

        private async Task CleanUpMonitorLogFilesAsync(List<string> logDirectories)
        {
            foreach (var logDirectory in logDirectories)
            {
                if (!Directory.Exists(logDirectory))
                {
                    continue;
                }

                var completedLogfilePaths = Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                    .Where(filePath => filePath.Contains("_monitor"))
                    .Where(filePath => !IsProcessRunning(filePath))
                    .ToList();

                if (completedLogfilePaths.Count == 0L)
                {
                    continue;
                }

                Logger.Info($"Completed log file: {string.Join("\n", completedLogfilePaths)}");

                try
                {
                    var isSuccess = await WebClientService.TerminateMonitoring(new WebClientService.TerminateMonitoringRequest(
                        fileNames: completedLogfilePaths.Select(filePath => Path.GetFileName(filePath))
                    ));

                    if (isSuccess)
                    {
                        foreach (var filePath in completedLogfilePaths)
                        {
                            try
                            {
                                File.Delete(filePath);
                                Logger.Info($"Deleted log file: {filePath}");
                            }
                            catch (Exception ex)
                            {
                                Logger.Warn($"Failed to delete file: {filePath}. Reason: {ex.Message}");
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.Error($"Fail to terminate monitor log file.\n{e.Message}");
                }
            }
        }

        private bool IsProcessRunning(string logFilePath)
        {
            var pattern = @"\[(\d+)\]";
            var match = Regex.Match(logFilePath, pattern);

            if (match.Success)
            {
                var processId = int.Parse(match.Groups[1].Value);
                try
                {
                    var process = Process.GetProcessById(processId);
                    return !process.HasExited;
                }
                catch (ArgumentException)
                {
                    // Process has exited
                    return false;
                }
            }

            // If the pattern does not match, assume the process is not running
            return false;
        }
    }
}
