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
        public static async Task<bool> CheckConnection()
        {

            using (var httpClient = GetHttpClient())
            {
                try
                {
                    httpClient.Timeout = TimeSpan.FromSeconds(2);
                    HttpResponseMessage response = await httpClient.GetAsync("/api/v1/monitoring/ready");
                    return response.IsSuccessStatusCode;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
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
                    Console.WriteLine(e);
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

                    HttpResponseMessage response = await httpClient.PutAsync("/api/v1/monitoring/termination", content);
                    return response.IsSuccessStatusCode;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
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