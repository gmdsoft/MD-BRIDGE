using LogModule;
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

        private static Dictionary<Product, List<string>> _productLogDirectories
        {
            get
            {
                string userName = Environment.UserName;
                return new Dictionary<Product, List<string>>() {
                    { Product.MD_NEXT, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-NEXT\Log" } },
                    { Product.MD_RED3, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-RED\Log" } },
                    { Product.MD_RED4, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-RED4\Log" } },
                    { Product.MD_VIDEO, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-MEDIA\Log", $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-VIDEO\Log" } },
                    { Product.MD_LIVE, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-LIVE\Log" } },
                    { Product.MD_CLOUD, new List<string> {  $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-CLOUD\Log" } },
                    { Product.MD_DRONE, new List<string> { $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-DRONE\Log" } }
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

        private static object _fileLock = new object();

        private static string _settingPath
        {
            get
            {
                return $@"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\MD-Series\MD-BRIDGE\settings.json";
            }
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

        public static Dictionary<Product, List<string>> GetProductLogDictionaries()
        {
            return LoadSettings().ProductLogDirectories;
        }

        public static List<string> GetLogDirectories(Product product)
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

        private static SettingsModel LoadSettings()
        {
            lock (_fileLock)
            {
                var setting = GetOrDefalutSettings();

                // 실제 설치된 제품만 설정에 포함
                setting.ProductLogDirectories = _productLogDirectories
                    .Where(kvp => kvp.Value.Any(Directory.Exists))
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value.Where(Directory.Exists).ToList()
                    );

                setting.ProductOffsets = setting.ProductLogDirectories
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => setting.ProductOffsets.ContainsKey(kvp.Key) ? setting.ProductOffsets[kvp.Key] : _defaultProductOffsets[kvp.Key]
                    );

                SaveSettings(setting);
                return setting;
            }
        }

        private static SettingsModel GetOrDefalutSettings()
        {
            if (!File.Exists(_settingPath))
                return CreateDefaultSettings();

            try
            {
                var json = File.ReadAllText(_settingPath);
                return JsonConvert.DeserializeObject<SettingsModel>(json)
                       ?? CreateDefaultSettings();
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to load settings: {ex.Message}");
                return CreateDefaultSettings();
            }
        }

        private static SettingsModel CreateDefaultSettings()
        {
            return new SettingsModel
            {
                ServerAddress = DefaultServerAddress,
                ProductLogDirectories = _productLogDirectories,
                CultureInfo = _defaultCurlture,
                ProductOffsets = _defaultProductOffsets
            };
        }

        private static void SaveSettings(SettingsModel settings)
        {
            lock (_fileLock)
            {
                var directoryPath = Path.GetDirectoryName(_settingPath);
                if (!Directory.Exists(directoryPath))
                {
                    Directory.CreateDirectory(directoryPath);
                }

                var jsonString = JsonConvert.SerializeObject(settings, Formatting.Indented);
                File.WriteAllText(_settingPath, jsonString);
            }
        }
    }
}
