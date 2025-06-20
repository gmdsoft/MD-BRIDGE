﻿using DevExpress.Mvvm;
using LogModule;
using MD.BRIDGE.Properties;
using MD.BRIDGE.Services;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;

namespace MD.BRIDGE.ViewModels
{
    public enum ConnectionStatus { Idle, Connecting, Connected, Error, Init_Error }

    public enum UpdateAvailabilityStatus { UpToDate, ReadyToUpdate, Blocked }

    public class MainViewModel : BindableBase
    {
        #region Fields & Constants

        private readonly BridgeService _bridgeService;
        private readonly ITaskbarIconService _taskbarIconService;

        private CancellationTokenSource _cancellationTokenSource;

        private readonly string _originalLanguage;
        private bool _isInit = true;
        private string _latestVersion;
        private string _latestVersionFileId;

        #endregion

        #region Properties

        // 지연시간 상수 (밀리초)
        private const int ServerHealthCheckDelay = 5000;
        private const int RetryWhenHiddenDelay = 5000;

        private bool _isWindowVisible;
        public bool IsWindowVisible
        {
            get => _isWindowVisible;
            set
            {
                if (_isWindowVisible == value) return;
                _isWindowVisible = value;
                RaisePropertiesChanged();
            }
        }

        private ConnectionStatus _connectionStatus;
        public ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                if (_connectionStatus == value) return;
                _connectionStatus = value;
                RaisePropertiesChanged();
            }
        }

        private UpdateAvailabilityStatus _updateAvailabilityStatus;
        public UpdateAvailabilityStatus UpdateAvailabilityStatus
        {
            get => _updateAvailabilityStatus;
            set
            {
                if (_updateAvailabilityStatus == value) return;
                _updateAvailabilityStatus = value;
                RaisePropertiesChanged();
            }
        }

        private string _serverAddress;
        public string ServerAddress
        {
            get => _serverAddress;
            set
            {
                if (_serverAddress == value) return;
                _serverAddress = value;
                RaisePropertiesChanged();
            }
        }

        private string _connectionStatusDetailText;
        public string ConnectionStatusDetailText
        {
            get => _connectionStatusDetailText;
            set
            {
                if (_connectionStatusDetailText == value) return;
                _connectionStatusDetailText = value;
                RaisePropertiesChanged();
            }
        }

        private string _selectedLanguage;
        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage == value) return;
                _selectedLanguage = value;
                RaisePropertiesChanged();
                RaisePropertiesChanged(nameof(CanApplyLanguage));
            }
        }
        public bool CanApplyLanguage => SelectedLanguage != _originalLanguage;

        private string _footerServerStatusText;
        public string FooterServerStatusText
        {
            get => _footerServerStatusText;
            set
            {
                if (_footerServerStatusText == value) return;
                _footerServerStatusText = value;
                RaisePropertiesChanged();
            }
        }

        private string _version;
        public string Version
        {
            get => _version;
            set
            {
                if (_version == value) return;
                _version = value;
                RaisePropertiesChanged();
            }
        }

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand ApplyLanguageCommand { get; }
        public ICommand DownloadCommand { get; }

        #endregion

        #region Constructor

        public MainViewModel(ITaskbarIconService taskbarIconService)
        {
            _taskbarIconService = taskbarIconService;
            _bridgeService = new BridgeService();

            ConnectCommand = new DelegateCommand(ExecuteConnectCommand);
            ApplyLanguageCommand = new DelegateCommand(ExecuteApplyLanguageCommand);
            DownloadCommand = new DelegateCommand(ExecuteDownloadCommand);

            ServerAddress = SettingService.GetServerAddress();
            SelectedLanguage = SettingService.GetCultureInfo().Name;
            _originalLanguage = SelectedLanguage; // 처음 불러온 언어 저장

            FooterServerStatusText = Resources.Footer_ServerStatus_Idle;

            // 빌드 버전 정보
            FileVersionInfo buildVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            Version = buildVersion.FileVersion;
            _latestVersion = buildVersion.FileVersion;

            StartBridgeService();

            // Backgound tasks
            Task.Run(() => ServerHealthCheckTask());
            Task.Run(() => RetryWhenHiddenTask());
        }

        #endregion

        #region Server Connection Logic

        private async void StartBridgeService()
        {
            Logger.Info("Starting bridgeService.");

            if (_connectionStatus == ConnectionStatus.Connecting)  // 이미 연결 중이라면, 연결 시도를 하지 않음
            {
                Logger.Info("Connecting to the server. Please wait a moment and try again.");
                return;
            }

            // 기존 BridgeServce 중지
            ResetCancellationToken();
            await StopBridgeServiceAsync();

            SetConnectionStatus(ConnectionStatus.Connecting);
            var isHealthy = await CheckServerHealth();

            if (isHealthy)
            {
                await Task.Delay(TimeSpan.FromSeconds(value: 0.3));
                _ = _bridgeService.RunAsync();
                SetConnectionStatus(ConnectionStatus.Connected);
            }
            else
            {
                if (_isInit)
                {
                    SetConnectionStatus(ConnectionStatus.Init_Error);
                }
                else
                {
                    SetConnectionStatus(ConnectionStatus.Error);
                }
            }
        }

        private async Task ServerHealthCheckTask()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(ServerHealthCheckDelay);

                    if (ConnectionStatus == ConnectionStatus.Connected)
                    {
                        var isHealthy = await CheckServerHealth();

                        if (!isHealthy)
                        {
                            SetConnectionStatus(ConnectionStatus.Error);
                            StopBridgeService();
                        }

                        UpdateTrayIconAndMessagesByStatus();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Info($"Server health check error. {e}");
            }
        }

        private async Task RetryWhenHiddenTask()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(RetryWhenHiddenDelay);

                    if (IsWindowVisible)
                    {
                        continue;
                    }

                    if (ConnectionStatus == ConnectionStatus.Init_Error || ConnectionStatus == ConnectionStatus.Error)
                    {
                        StartBridgeService();
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Info($"Retry when hidden error. {e}");
            }
        }

        private void ResetCancellationToken()
        {
            Logger.Info("Reset CancellationTokenSource.");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            Logger.Debug("CancellationTokenSource created.");
        }

        private async Task StopBridgeServiceAsync()
        {
            Logger.Info("Stopping BridgeService.");

            _bridgeService.Stop();
            await _bridgeService.WaitForCompletion();
        }

        private async Task<bool> CheckServerHealth()
        {
            var result = await WebClientService.CheckServerHealthAndGetVersion();

            if (result.IsFailure)
            {
                Logger.Info($"Server health check failed: {result.Error}");
                return false;
            }

            // 최신 버전 갱신
            _latestVersion = result.Value.LatestVersion;
            _latestVersionFileId = result.Value.FileId;

            return true;
        }

        private async void StopBridgeService()
        {
            Logger.Info("Stopping BridgeService.");
            await StopBridgeServiceAsync();

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            Logger.Debug("Cancel and dispose CancellationTokenSource.");
        }

        private bool IsUpdatable()
        {
            if (string.IsNullOrWhiteSpace(_latestVersion) || string.IsNullOrWhiteSpace(_latestVersionFileId))
            {
                return false;
            }

            try
            {
                var current = new Version(this.Version);
                var latest = new Version(_latestVersion);

                // latest 가 current 보다 크면 업데이트 가능
                return latest.CompareTo(current) > 0;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private void UpdateTrayIconAndMessagesByStatus()
        {
            // 상태에 따른 업데이트 가능 여부 설정
            if (IsUpdatable())
            {
                UpdateAvailabilityStatus = ConnectionStatus == ConnectionStatus.Connected
                    ? UpdateAvailabilityStatus.ReadyToUpdate  // 업데이트 가능
                    : UpdateAvailabilityStatus.Blocked; // 업데이트 불가
            }
            else
            {
                // 업데이트가 필요하지 않거나, 서버에 연결되지 않은 경우
                UpdateAvailabilityStatus = UpdateAvailabilityStatus.UpToDate;
            }

            // 상태에 따른 아이콘 및 메시지 업데이트
            if (ConnectionStatus == ConnectionStatus.Connected)
            {
                if (UpdateAvailabilityStatus == UpdateAvailabilityStatus.ReadyToUpdate)
                {
                    _taskbarIconService.SetTrayIcon("Assets/tray_updatable.png");
                    _taskbarIconService.UpdateToolTipMessage(Resources.Tray_UpdatableMessage);
                }
                else
                {
                    _taskbarIconService.SetTrayIcon("Assets/tray_connected.png");
                    _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectedMessage);
                }

                ConnectionStatusDetailText = Resources.Inline_Connection_DefaultMessage;
                FooterServerStatusText = Resources.Footer_ServerStatus_Connected;
            }
            else if (ConnectionStatus == ConnectionStatus.Error || ConnectionStatus == ConnectionStatus.Init_Error)
            {
                _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
                _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);
                ConnectionStatusDetailText = Resources.Inline_Connection_ErrorMessage;
                FooterServerStatusText = Resources.Footer_ServerStatus_Failed;
            }
            else if (ConnectionStatus == ConnectionStatus.Idle)
            {
                _taskbarIconService.SetTrayIcon("Assets/tray_idle.png");
                _taskbarIconService.UpdateToolTipMessage(Resources.Tray_IdleMessage);
                ConnectionStatusDetailText = Resources.Inline_Connection_IdleMessage;
                FooterServerStatusText = Resources.Footer_ServerStatus_Idle;
            }
            else
            {
                _taskbarIconService.SetTrayIcon("Assets/tray_normal.png");
                _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectingMessage);
                ConnectionStatusDetailText = Resources.Inline_Connection_ConnectingMessage;
                FooterServerStatusText = Resources.Footer_ServerStatus_Connecting;
            }
        }

        private void SetConnectionStatus(ConnectionStatus status)
        {
            Logger.Info($"Setting state: {status}");
            ConnectionStatus = status;

            if (status == ConnectionStatus.Connected)
            {
                _isInit = false;
            }

            UpdateTrayIconAndMessagesByStatus();
        }

        #endregion

        #region Command Handlers

        private void ExecuteConnectCommand()
        {
            Logger.Info($"New server address: {ServerAddress}");
            SettingService.SetServerAddress(ServerAddress);

            if (ConnectionStatus == ConnectionStatus.Connected)
            {
                StopBridgeService();
                SetConnectionStatus(ConnectionStatus.Idle);
            }
            else if (ConnectionStatus == ConnectionStatus.Error)
            {
                SetConnectionStatus(ConnectionStatus.Idle);
            }
            else
            {
                StartBridgeService();
            }
        }

        private void ExecuteApplyLanguageCommand()
        {
            MessageBoxResult result = System.Windows.MessageBox.Show(
                Resources.MessageBox_Apply_Language_Message,
                Resources.MessageBox_Apply_Language_Title,
                MessageBoxButton.YesNo
            );

            if (result == MessageBoxResult.Yes)
            {
                Logger.Info($"Selected language: {SelectedLanguage}");
                var newCulture = new CultureInfo(SelectedLanguage);
                SettingService.SetCultureInfo(newCulture);

                RestartApplication();
            }
        }

        private void ExecuteDownloadCommand()
        {
            Logger.Info("Downloading latest version.");
            var downloadUrl = $"{ServerAddress}/api/v1/files/download/{_latestVersionFileId}";

            Process.Start(new ProcessStartInfo
            {
                FileName = downloadUrl,
                UseShellExecute = true
            });
        }

        private void RestartApplication()
        {
            string exePath = Assembly.GetEntryAssembly().Location;
            string batPath = Path.Combine(Path.GetTempPath(), "restart.bat");

            var exeName = Path.GetFileName(exePath);

            string batContent = $@"
                                @echo off
                                :loop
                                tasklist /FI ""IMAGENAME eq {exeName}"" | find /i ""{Path.GetFileNameWithoutExtension(exeName)}"" > nul
                                if not errorlevel 1 (
                                    timeout /t 1 > nul
                                    goto loop
                                )
                                start """" ""{exePath}""
                                del ""%~f0""
                                ";

            File.WriteAllText(batPath, batContent);

            // 배치 파일 실행
            Process.Start(new ProcessStartInfo()
            {
                FileName = batPath,
                WindowStyle = ProcessWindowStyle.Hidden,
                CreateNoWindow = true,
                UseShellExecute = true
            });

            // 현재 앱 정상 종료
            System.Windows.Application.Current.Shutdown();
        }

        #endregion
    }
}