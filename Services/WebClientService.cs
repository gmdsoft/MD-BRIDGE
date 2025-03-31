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
                    HttpResponseMessage response = await httpClient.GetAsync("/api/v1/monitor-logs/ready");
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

                    HttpResponseMessage response = await httpClient.PostAsync("/api/v1/monitor-logs", content);
                    return response.IsSuccessStatusCode;
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                    return false;
                }
            }
        }

        public static async Task<bool> CompleteLogs(CompleteLogRequest request)
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

                    HttpResponseMessage response = await httpClient.PostAsync("/api/v1/monitor-logs/complete", content);
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

        public class CompleteLogRequest
        {
            [JsonProperty("filenames")]
            public IEnumerable<string> Filenames { get; private set; }
            public CompleteLogRequest(IEnumerable<string> filenames)
            {
                Filenames = filenames;
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