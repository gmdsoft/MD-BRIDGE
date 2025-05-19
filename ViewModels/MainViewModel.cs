using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Input;
using DevExpress.Mvvm;
using LogModule;
using MD.BRIDGE.Properties;
using MD.BRIDGE.Services;

namespace MD.BRIDGE.ViewModels
{
    public enum ConnectionStatus
    {
        Idle,
        Connecting,
        Connected,
        Error,       // 연결 실패
        Init_Error   // 최초 연결 실패
    }

    public class MainViewModel : BindableBase
    {
        #region Fields & Constants

        private readonly BridgeService _bridgeService;
        private CancellationTokenSource _cancellationTokenSource;
        private readonly ITaskbarIconService _taskbarIconService;
        private readonly string _originalLanguage;
        private bool _isInit = true;

        #endregion

        #region Properties
        // 지연시간 상수 (밀리초)
        private const int MonitorConnectionDelay = 5000;

        private bool _isWindowVisible;
        public bool IsWindowVisible
        {
            get => _isWindowVisible;
            set
            {
                if (_isWindowVisible != value)
                {
                    _isWindowVisible = value;
                    RaisePropertiesChanged();
                }
            }
        }

        private string _serverAddress;
        public string ServerAddress
        {
            get => _serverAddress;
            set { _serverAddress = value; RaisePropertiesChanged(); }
        }

        private ConnectionStatus _connectionStatus;
        public ConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set { _connectionStatus = value; RaisePropertiesChanged(); }
        }

        private string _connectionStatusDetailText;
        public string ConnectionStatusDetailText
        {
            get => _connectionStatusDetailText;
            set { _connectionStatusDetailText = value; RaisePropertiesChanged(); }
        }

        private string _selectedLanguage;
        public string SelectedLanguage
        {
            get => _selectedLanguage;
            set
            {
                if (_selectedLanguage == value) return;
                _selectedLanguage = value;
                RaisePropertiesChanged();                     // SelectedLanguage 변경 알림
                RaisePropertiesChanged(nameof(CanApplyLanguage)); // CanApplyLanguage 변경 알림
            }
        }
        public bool CanApplyLanguage => SelectedLanguage != _originalLanguage;

        private string _footerServerStatusText;
        public string FooterServerStatusText
        {
            get => _footerServerStatusText;
            set { _footerServerStatusText = value; RaisePropertiesChanged(); }
        }

        private string _versionText;
        public string VersionText
        {
            get => _versionText;
            set { _versionText = value; RaisePropertiesChanged(); }
        }

        private string _buildVersionText;
        public string BuildVersionText
        {
            get => _buildVersionText;
            set { _buildVersionText = value; RaisePropertiesChanged(); }
        }

        #endregion

        #region Commands

        public ICommand ConnectCommand { get; }
        public ICommand ApplyLanguageCommand { get; }

        #endregion

        #region Constructor

        public MainViewModel(ITaskbarIconService taskbarIconService)
        {
            _taskbarIconService = taskbarIconService;
            _bridgeService = new BridgeService();

            ConnectCommand = new DelegateCommand(ExecuteConnectCommand);
            ApplyLanguageCommand = new DelegateCommand(ExecuteApplyLanguageCommand);

            ServerAddress = SettingService.GetServerAddress();
            SelectedLanguage = SettingService.GetCultureInfo().Name;
            _originalLanguage = SelectedLanguage; // 처음 불러온 언어 저장

            FooterServerStatusText = Resources.Footer_ServerStatus_Waiting;

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText = "v" + version;

            // 빌드 버전 정보
            FileVersionInfo buildVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            BuildVersionText = buildVersion.FileVersion;

            StartBridgeService();
            _ = CheckServerHealth();
            _ = RetryWhenHidden();
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

            SetConnectingStatus();
            var isHealthy = await CheckHealth();

            if (isHealthy)
            {
                _ = _bridgeService.RunAsync();
                SetConnectedStatus();
            }
            else
            {
                if (_isInit)
                {
                    SetInitErrorStatus();
                }
                else
                {
                    SetErrorStatus();
                }
            }
        }

        private async Task CheckServerHealth()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(MonitorConnectionDelay);

                    if (ConnectionStatus == ConnectionStatus.Connected)
                    {
                        var isHealthy = await CheckHealth();

                        if (!isHealthy)
                        {
                            SetErrorStatus();
                            StopBridgeService();
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Info($"Server health check error. {e}");
            }
        }

        private async Task RetryWhenHidden()
        {
            try
            {
                while (true)
                {
                    await Task.Delay(MonitorConnectionDelay);

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

        private async Task<bool> CheckHealth()
        {
            bool isConnectable = false;
            try
            {
                isConnectable = await WebClientService.CheckConnection();
            }
            catch (Exception)
            {
                Logger.Info("Cloud not connect to server");
            }

            return isConnectable;
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

        #endregion

        #region Command Handlers

        // Connect Button
        private void ExecuteConnectCommand()
        {
            Logger.Info($"New server address: {ServerAddress}");
            SettingService.SetServerAddress(ServerAddress);

            if (ConnectionStatus == ConnectionStatus.Connected)
            {
                StopBridgeService();
                SetIdleState();
            }
            else if (ConnectionStatus == ConnectionStatus.Error)
            {
                SetIdleState();
            }
            else
            {
                StartBridgeService();
            }
        }

        private void ExecuteApplyLanguageCommand()
        {
            MessageBox.Show(Resources.MessageBox_Apply_Language_Message);
            Logger.Info($"Selected language: {SelectedLanguage}");
            var newCulture = new CultureInfo(SelectedLanguage);
            SettingService.SetCultureInfo(newCulture);

            RestartApplication();
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

        #region Status Helpers

        private void SetIdleState()
        {
            Logger.Info("Setting idle state.");

            _taskbarIconService.SetTrayIcon("Assets/tray_waiting.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_IdleMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_IdleMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Waiting;

            ConnectionStatus = ConnectionStatus.Idle;
        }

        private void SetConnectingStatus()
        {
            Logger.Info("Setting connecting status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_normal.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectingMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ConnectingMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connecting;

            ConnectionStatus = ConnectionStatus.Connecting;
        }

        private void SetConnectedStatus()
        {
            Logger.Info("Setting connected status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connected.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectedMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_DefaultMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connected;

            ConnectionStatus = ConnectionStatus.Connected;
            _isInit = false;
        }

        private void SetErrorStatus()
        {
            Logger.Info("Setting error status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;

            ConnectionStatus = ConnectionStatus.Error;
        }

        private void SetInitErrorStatus()
        {
            Logger.Info("Setting initial error status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_initErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;

            ConnectionStatus = ConnectionStatus.Init_Error;
        }

        #endregion
    }
}