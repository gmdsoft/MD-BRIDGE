using MD.Platform.Log;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MD.BRIDGE.Utils;

namespace MD.BRIDGE.Services
{
    public class BridgeService
    {
        private const string _mutexKey = "Global\\MD-BRIDGE";
        private TaskCompletionSource<bool> _completionSource;
        private CancellationTokenSource cts;

        public void Run()
        {
            Task.Run(() =>
            {
                using (var mutex = new Mutex(true, _mutexKey, out bool createdNew))
                {
                    if (!createdNew)
                    {
                        Console.WriteLine($"[{DateTimeOffset.Now}] Another Bridge is already running.");
                        Environment.Exit(0);
                    }

                    _completionSource = new TaskCompletionSource<bool>();
                    cts = new CancellationTokenSource();

                    try
                    {
                        RunTasksAsync().GetAwaiter().GetResult();
                    }
                    finally
                    {
                        mutex.ReleaseMutex();
                    }
                }
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
            Console.WriteLine($"[{DateTimeOffset.Now}] Start MonitoringLogTask.");

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
            Console.WriteLine($"[{DateTimeOffset.Now}] Start HandleLogTask. Offset: {offset}, Now: {now}");

            /** Serach log files */
            Console.WriteLine($"[{DateTimeOffset.Now}] Start Search LogFiles.");
            var logFilePaths = GetLogFilePaths(logDirectory: logDirectory, start: offset, end: now);
            Console.WriteLine($"[{DateTimeOffset.Now}] Product:{product}, log file size:{logFilePaths.Count()}");
            Console.WriteLine($"[{DateTimeOffset.Now}] Finish Search LogFiles.");

            if (logFilePaths.Count() == 0)
            {
                Console.WriteLine($"[{DateTimeOffset.Now}] Nothing to handle... Finish HandleLogTask.");
                return;
            }

            /** Parse logs */
            Console.WriteLine($"[{DateTimeOffset.Now}] Start Parsing Logs.");
            var pathToRecords = logFilePaths.ToDictionary(
                logFilePath => logFilePath,
                logFilePath => LogExctractorService.Extract(logFilePath, offset, now)
            );

            pathToRecords.ToList().ForEach(pathToLog =>
                Console.WriteLine($"[{DateTimeOffset.Now}] ㄴ Log file name:{pathToLog.Key}, Records:{pathToLog.Value.Count()}")
            );
            Console.WriteLine($"[{DateTimeOffset.Now}] Finish Parsing Logs.");

            /** Upload logs to server */
            Console.WriteLine($"[{DateTimeOffset.Now}] Start Upload Logs.");
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
                // Todo: clean up log files
                CleanUpLogFiles(logFilePaths, offset, now);
                SettingService.SetProductOffset(product, now);
            }
            else
            {
                Console.WriteLine($"[{DateTimeOffset.Now}] Fail to upload logs.");
            }
            Console.WriteLine($"[{DateTimeOffset.Now}] Finish Upload Logs.");


            Console.WriteLine($"[{DateTimeOffset.Now}] Finish HandleLogTask.");
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

            await WebClientService.CompleteLogs(new WebClientService.CompleteLogRequest(
                filenames: completedLogfilePaths.Select(filePath => Path.GetFileName(filePath))
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
