using MD.BRIDGE.Utils;
using MD.Platform.Log;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace MD.BRIDGE.Services
{
    public static class SettingService
    {
        #region Default Values
        public static string DefaultServerAddress { get; } = "http://172.16.3.82:8080";

        private static Dictionary<Product, string> _productLogDirectories
        {
            get
            {
                string userName = Environment.UserName;
                return new Dictionary<Product, string>() {
                    { Product.MD_NEXT,  $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-NEXT\Log" },
                    { Product.MD_RED3,  $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-RED\Log" },
                    { Product.MD_RED4,  $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-RED4\Log" },
                    { Product.MD_VIDEO, $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-MEDIA\Log" },
                    { Product.MD_LIVE,  $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-LIVE\Log" },
                    { Product.MD_CLOUD, $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-CLOUD\Log" },
                    { Product.MD_DRONE, $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-DRONE\Log" }
                };
            }
        }

        private static Dictionary<Product, DateTimeOffset> _defaultProductOffsets { get; set; } = new Dictionary<Product, DateTimeOffset>() {
            { Product.MD_NEXT,  new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_RED3,  new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_RED4,  new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_VIDEO, new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_LIVE,  new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_CLOUD, new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            { Product.MD_DRONE, new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero) }
        };

        private static CultureInfo _defaultCurlture = new CultureInfo("en-US");
        #endregion

        private static string _settingPath
        {
            get
            {
                string userName = Environment.UserName;
                return $@"C:\Users\{userName}\AppData\Local\MD-Series\MD-BRIDGE\settings.json";
            }
        }

        private static SettingsModel LoadSettings()
        {
            SettingsModel setting = File.Exists(_settingPath)
                ? JsonConvert.DeserializeObject<SettingsModel>(File.ReadAllText(_settingPath)) // 기존 설정 불러오기
                : new SettingsModel
                {
                    ServerAddress = DefaultServerAddress,
                    ProductLogDirectories = _productLogDirectories,
                    CultureInfo = _defaultCurlture,
                    ProductOffsets = _defaultProductOffsets
                };

            // 실제 설치된 제품만 설정에 포함
            setting.ProductLogDirectories = _productLogDirectories
                .Where(kvp => Directory.Exists(kvp.Value))
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);

            setting.ProductOffsets = setting.ProductLogDirectories
                .ToDictionary(
                    kvp => kvp.Key,
                    kvp => setting.ProductOffsets.ContainsKey(kvp.Key) ? setting.ProductOffsets[kvp.Key] : _defaultProductOffsets[kvp.Key]
                );

            SaveSettings(setting);
            return setting;
        }

        private static void SaveSettings(SettingsModel settings)
        {
            var directoryPath = Path.GetDirectoryName(_settingPath);
            if (!Directory.Exists(_settingPath))
            {
                Directory.CreateDirectory(directoryPath);
            }

            var jsonString = JsonConvert.SerializeObject(settings, Formatting.Indented);
            File.WriteAllText(_settingPath, jsonString);
        }

        public static string GetServerAddress()
        {
            return LoadSettings().ServerAddress;
        }

        public static void SetServerAddress(string serverAddress)
        {
            if (serverAddress.IsNullOrEmpty())
            {
                serverAddress = "";
            }
            else if (serverAddress.StartsWith("http://") || serverAddress.StartsWith("https://"))
            {
                serverAddress = serverAddress.TrimEnd('/');
            }
            else
            {
                serverAddress = $"http://{serverAddress}".TrimEnd('/');
            }

            var settings = LoadSettings();
            settings.ServerAddress = serverAddress;
            SaveSettings(settings);
        }

        public static Dictionary<Product, string> GetProductLogDictionaries()
        {
            return LoadSettings().ProductLogDirectories;
        }

        public static string GetLogDirectory(Product product)
        {
            return LoadSettings().ProductLogDirectories[product];
        }

        public static DateTimeOffset GetProductOffset(Product product)
        {
            return LoadSettings().ProductOffsets[product];
        }

        public static void SetProductOffset(Product product, DateTimeOffset offset)
        {
            var settings = LoadSettings();
            settings.ProductOffsets[product] = offset;
            SaveSettings(settings);
        }

        public static CultureInfo GetCultureInfo()
        {
            return LoadSettings().CultureInfo;
        }

        public static void SetCultureInfo(CultureInfo culture)
        {
            var settings = LoadSettings();
            settings.CultureInfo = culture;
            SaveSettings(settings);
        }
    }
}
