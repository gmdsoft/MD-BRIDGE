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

        public async Task RunAsync()
        {
            _completionSource = new TaskCompletionSource<bool>();
            cts = new CancellationTokenSource();

            var tasks = new List<Task>
            {
                MonitoringLogTask(cts.Token),
            };

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (Exception ex)
            {
                Logger.Error($"Unhandled error: {ex.Message}");
            }
            finally
            {
                _completionSource?.TrySetResult(true);
            }
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

        private async Task MonitoringLogTask(CancellationToken cancellationToken)
        {
            var productLogDirectories = SettingService.GetProductLogDictionaries();
            Logger.Info("Starting MonitoringLogTask.");

            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    var tasks = productLogDirectories.Select(async kvp =>
                    {
                        await ProcessMonitoringLog(kvp.Key);
                    }).ToList();

                    await Task.WhenAll(tasks);
                    await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        private async Task ProcessMonitoringLog(Product product)
        {

            var logDirectories = SettingService.GetLogDirectories(product);
            var offset = SettingService.GetProductOffset(product);

            DateTimeOffset now = DateTimeOffset.Now;

            /** Serach log files */
            var logFilePaths = logDirectories.SelectMany(logDirectory => GetMonitorLogFilePaths(logDirectory: logDirectory, start: offset, end: now));
            if (logFilePaths.Count() == 0)
            {
                return;
            }

            var pathToRecords = logFilePaths.ToDictionary(
                logFilePath => logFilePath,
                logFilePath => LogExctractorService.Extract(logFilePath, offset, now)
            );

            foreach (var pathToLog in pathToRecords)
            {
                Logger.Info($" - Log file name:{pathToLog.Key}, Records:{pathToLog.Value.Count()}");
            }

            /** Upload logs to server */
            var logsToUpload = pathToRecords.Select(kvp => new WebClientService.UploadLogRequest.Log(
                filename: Path.GetFileName(kvp.Key),
                records: kvp.Value
            )).ToList();

            bool isSuccess = await WebClientService.UploadLogs(
                new WebClientService.UploadLogRequest(product, logsToUpload)
            );

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

        private IEnumerable<string> GetMonitorLogFilesInDirectory(string logDirectory)
        {
            if (Directory.Exists(logDirectory) == false)
            {
                return Enumerable.Empty<string>();
            }

            return Directory.GetFiles(logDirectory, "*.log", SearchOption.TopDirectoryOnly)
                .Where(filePath => filePath.Contains("_monitor"));
        }

        private IEnumerable<string> GetMonitorLogFilePaths(string logDirectory, DateTimeOffset start, DateTimeOffset end)
        {
            if (Directory.Exists(logDirectory) == false)
            {
                return new List<string>();
            }

            return GetMonitorLogFilesInDirectory(logDirectory)
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

                var completedLogfilePaths = GetMonitorLogFilesInDirectory(logDirectory)
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
                                continue;
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
                    return Process.GetProcesses().Any(p => p.Id == processId);
                }
                catch (Exception e)
                {
                    // Process has exited
                    Logger.Error($"Error checking process with ID {processId}: {e.Message}");
                    return true;
                }
            }

            // If the pattern does not match, assume the process is not running
            return true;
        }
    }
}
