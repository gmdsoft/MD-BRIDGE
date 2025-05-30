using CSharpFunctionalExtensions;
using LogModule;
using MD.Platform.Log;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace MD.BRIDGE.Services
{
    static public class WebClientService
    {
        public static async Task<Result<GetLatestMdBridgeVersionResponse>> CheckServerHealthAndGetVersion()
        {
            using (var httpClient = GetHttpClient())
            {
                try
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    HttpResponseMessage response = await httpClient.GetAsync("/api/v1/monitoring/ready");

                    if (!response.IsSuccessStatusCode)
                    {
                        return Result.Failure<GetLatestMdBridgeVersionResponse>($"Health check failed with status code: {response.StatusCode}");
                    }

                    string content = await response.Content.ReadAsStringAsync();

                    var versionInfo = JsonConvert.DeserializeObject<GetLatestMdBridgeVersionResponse>(content);
                    if (versionInfo == null)
                    {
                        return Result.Failure<GetLatestMdBridgeVersionResponse>("Failed to deserialize version info.");
                    }

                    return Result.Success(versionInfo);
                }
                catch (Exception e)
                {
                    Logger.Error($"Exception occurred during health check/version fetch:\n{e.Message}");
                    return Result.Failure<GetLatestMdBridgeVersionResponse>($"Exception: {e.Message}");
                }
            }
        }

        public static async Task<bool> UploadLogs(UploadLogRequest request)
        {
            using (var httpClient = GetHttpClient())
            {
                try
                {
                    var content = new StringContent(
                        JsonConvert.SerializeObject(request),
                        Encoding.UTF8,
                        "application/json"
                    );

                    HttpResponseMessage response = await httpClient.PostAsync("/api/v1/monitoring/logs", content);
                    return response.IsSuccessStatusCode;
                }
                catch (Exception e)
                {
                    Logger.Error($"Fail to upload logs.\n{e.Message}");
                    return false;
                }
            }
        }

        public static async Task<bool> TerminateMonitoring(TerminateMonitoringRequest request)
        {
            using (var httpClient = GetHttpClient())
            {
                try
                {
                    var content = new StringContent(
                        JsonConvert.SerializeObject(request),
                        Encoding.UTF8,
                        "application/json"
                    );

                    HttpResponseMessage response = await httpClient.PutAsync("/api/v1/monitoring/logs/termination", content);
                    return response.IsSuccessStatusCode;
                }
                catch (Exception e)
                {
                    Logger.Error($"Fail to terminate monitoring.\n{e.Message}");
                    return false;
                }
            }
        }

        public class UploadLogRequest
        {
            [JsonProperty("product")]
            public string Product { get; private set; }
            [JsonProperty("logs")]
            public IEnumerable<Log> Logs { get; private set; }

            public UploadLogRequest(Product product, IEnumerable<Log> logs)
            {
                Product = product.ToString();
                Logs = logs;
            }

            public class Log
            {
                [JsonProperty("filename")]
                public string Filename { get; private set; }
                [JsonProperty("records")]
                public IEnumerable<string> Records { get; private set; }

                public Log(string filename, IEnumerable<string> records)
                {
                    Filename = filename;
                    Records = records;
                }
            }
        }

        public class TerminateMonitoringRequest
        {
            [JsonProperty("fileNames")]
            public IEnumerable<string> FileNames { get; private set; }
            public TerminateMonitoringRequest(IEnumerable<string> fileNames)
            {
                FileNames = fileNames;
            }
        }

        public class GetLatestMdBridgeVersionResponse
        {
            [JsonProperty("fileId")]
            public string FileId { get; private set; }
            [JsonProperty("latestVersion")]
            public string LatestVersion { get; private set; }
        }

        private static HttpClient GetHttpClient()
        {
            var httpClient = new HttpClient();
            try
            {
                httpClient.BaseAddress = new Uri(SettingService.GetServerAddress());
            }
            catch
            { }

            return httpClient;
        }
    }
}