using MD.Platform.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MD.BRIDGE.Utils;
using LogModule;

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

            var logDirectory = SettingService.GetLogDirectory(product);
            var offset = SettingService.GetProductOffset(product);

            DateTimeOffset now = DateTimeOffset.Now;

            /** Serach log files */
            var logFilePaths = GetLogFilePaths(logDirectory: logDirectory, start: offset, end: now);
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
            var isSuccess = await WebClientService.UploadLogs(
                request: new WebClientService.UploadLogRequest(
                    product: product,
                    logs: pathToRecords.Select(kvp => new WebClientService.UploadLogRequest.Log(
                        filename: Path.GetFileName(kvp.Key),
                        records: kvp.Value
                    ))
                )
            );

            if (isSuccess)
            {
                CleanUpLogFiles(logFilePaths, offset, now);
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

            return Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .Where(filePath => filePath.Contains("_monitor"))
                .Where(filePath => new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero).IsBetween(start, end));
        }

        private async void CleanUpLogFiles(IEnumerable<string> logFilePaths, DateTimeOffset start, DateTimeOffset end)
        {
            var completedLogfilePaths = logFilePaths
                .Where(IsFileClosed) // 다른 프로세스가 사용하지 않는 로그파일 filter
                .Where(filePath => new DateTimeOffset(File.GetLastWriteTimeUtc(filePath), TimeSpan.Zero).IsBetween(start, end));

            Logger.Info($"Completed log files: {completedLogfilePaths}");

            await WebClientService.TerminateMonitoring(new WebClientService.TerminateMonitoringRequest(
                fileNames: completedLogfilePaths.Select(filePath => Path.GetFileName(filePath))
            ));
        }

        private bool IsFileClosed(string logFilePath)
        {
            try
            {
                using (File.Open(logFilePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                {
                    return true;
                }

            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
