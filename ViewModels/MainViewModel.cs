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
    public enum ServerConnectionStatus
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
        private bool isConnecting = false;  // 중복 연결 시도를 방지하는 플래그

        #endregion

        #region Properties
        // 지연시간 상수 (밀리초)
        private const int InitialConnectionDelay = 3000;
        private const int MonitorConnectionDelay = 5000;

        private string _serverAddress;
        public string ServerAddress
        {
            get => _serverAddress;
            set { _serverAddress = value; RaisePropertiesChanged(); }
        }

        private ServerConnectionStatus _connectionStatus;
        public ServerConnectionStatus ConnectionStatus
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
                if (_selectedLanguage != value)
                {
                    _selectedLanguage = value;
                    RaisePropertiesChanged();
                }
            }
        }

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

        public ICommand ConnectToServerCommand { get; }
        public ICommand ApplyLanguageCommand { get; }

        #endregion

        #region Constructor

        public MainViewModel(ITaskbarIconService taskbarIconService)
        {
            _taskbarIconService = taskbarIconService;
            _bridgeService = new BridgeService();

            ConnectToServerCommand = new DelegateCommand(ExecuteConnectToServerCommand);
            ApplyLanguageCommand = new DelegateCommand(ExecuteApplyLanguageCommand);

            ServerAddress = SettingService.GetServerAddress();
            SelectedLanguage = SettingService.GetCultureInfo().Name;

            FooterServerStatusText = Resources.Footer_ServerStatus_Waiting;

            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionText = "v" + version;

            // 빌드 버전 정보
            FileVersionInfo buildVersion = FileVersionInfo.GetVersionInfo(Assembly.GetExecutingAssembly().Location);
            BuildVersionText = buildVersion.FileVersion;


            // 최초 서버 연결 시도 (초기 연결 오류가 발생할 경우 별도 처리)
            StartBridgeService(isInit: true);
        }

        #endregion

        #region Server Connection Logic

        private async void StartBridgeService(bool isInit = false)
        {
            Logger.Info("Starting bridgeService.");

            if (isConnecting)  // 이미 연결 중이라면, 연결 시도를 하지 않음
            {
                Logger.Info("Connecting to the server. Please wait a moment and try again.");
                return;
            }

            isConnecting = true;  // 연결 시도 시작

            ResetCancellationToken();
            await StopBridgeServiceAsync();

            // 최초 연결 시도
            await AttemptInitialConnectionAsync(isInit);
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            SetConnectedStatus();
            await _bridgeService.RunAsync();

            // 연결 후 주기적으로 서버 상태 모니터링
            await MonitorServerConnectionAsync();

            isConnecting = false;  // 연결 후 상태 초기화
        }

        /// <summary>
        /// 기존의 CancellationTokenSource를 취소 및 해제하고 새로 생성합니다.
        /// </summary>
        private void ResetCancellationToken()
        {
            Logger.Info("Reset CancellationTokenSource.");
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = new CancellationTokenSource();
            Logger.Debug("CancellationTokenSource created.");
        }

        /// <summary>
        /// 현재 브릿지 서비스를 중지하고 완료될 때까지 기다립니다.
        /// </summary>
        private async Task StopBridgeServiceAsync()
        {
            Logger.Info("Stopping BridgeService.");

            _bridgeService.Stop();
            await _bridgeService.WaitForCompletion();
        }

        /// <summary>
        /// 최초 연결을 시도하여 서버가 응답할 때까지 대기합니다.
        /// </summary>
        private async Task<bool> AttemptInitialConnectionAsync(bool isInit)
        {
            bool isConnectable = false;
            while (!isConnectable)
            {
                try
                {
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    SetConnectingStatus();
                    SettingService.SetServerAddress(ServerAddress);
                    isConnectable = await WebClientService.CheckConnection();

                    // 연결 실패 시 처리
                    if (!isConnectable)
                    {
                        if (isInit)
                        {
                            SetInitErrorStatus();
                            Logger.Info("Unable to initialize server connection");
                        }
                        else
                        {
                            SetErrorStatus();
                            Logger.Info("Unable to connect to the server");
                        }
                        await Task.Delay(InitialConnectionDelay, _cancellationTokenSource.Token);
                    }
                }
                catch (TaskCanceledException)
                {
                    Logger.Info("Connection attempt canceled.");
                    break;
                }
            }

            return isConnectable;
        }

        /// <summary>
        /// 서버가 연결된 후 주기적으로 연결 상태를 확인합니다.
        /// 만약 서버 응답이 비정상적이면 서비스를 중단한 후 복구될 때까지 대기하다가 재시작합니다.
        /// </summary>
        private async Task MonitorServerConnectionAsync()
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(MonitorConnectionDelay, _cancellationTokenSource.Token);

                    bool connectionOk = await WebClientService.CheckConnection();
                    if (!connectionOk)
                    {
                        Logger.Info("Server connection lost. Stopping BridgeService.");
                        _bridgeService.Stop();
                        SetErrorStatus();

                        // 서버 복구 대기 루프
                        while (!_cancellationTokenSource.Token.IsCancellationRequested && !(await WebClientService.CheckConnection()))
                        {
                            Logger.Info("Server connection lost. Waiting for recovery...");
                            await Task.Delay(MonitorConnectionDelay, _cancellationTokenSource.Token);
                        }

                        Logger.Info("Server connection restored. Restarting BridgeService.");
                        SetConnectedStatus();
                        await _bridgeService.RunAsync();
                    }
                }
                catch (OperationCanceledException)
                {
                    Logger.Info("Monitoring task canceled.");
                    break;
                }
            }
        }

        private async void StopBridgeService()
        {
            Logger.Info("Stopping BridgeService.");
            await StopBridgeServiceAsync();

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;
            Logger.Debug("Cancel and dispose CancellationTokenSource.");

            SetIdleState();
        }

        #endregion

        #region Command Handlers

        private void ExecuteConnectToServerCommand()
        {
            Logger.Info($"New server address: {ServerAddress}");
            SettingService.SetServerAddress(ServerAddress);

            if (ConnectionStatus == ServerConnectionStatus.Connected)
            {
                StopBridgeService();
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

            ConnectionStatus = ServerConnectionStatus.Idle;
        }

        private void SetConnectingStatus()
        {
            Logger.Info("Setting connecting status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_normal.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectingMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ConnectingMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connecting;

            ConnectionStatus = ServerConnectionStatus.Connecting;
        }

        private void SetConnectedStatus()
        {
            Logger.Info("Setting connected status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connected.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectedMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_DefaultMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connected;

            ConnectionStatus = ServerConnectionStatus.Connected;
        }

        private void SetErrorStatus()
        {
            Logger.Info("Setting error status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;

            ConnectionStatus = ServerConnectionStatus.Error;
        }

        private void SetInitErrorStatus()
        {
            Logger.Info("Setting initial error status.");

            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_initErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;

            ConnectionStatus = ServerConnectionStatus.Init_Error;
        }

        #endregion
    }
}