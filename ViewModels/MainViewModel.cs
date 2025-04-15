using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using DevExpress.Mvvm;
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
            BuildVersionText = "v1.0.0.0";

            // 최초 서버 연결 시도 (초기 연결 오류가 발생할 경우 별도 처리)
            StartBridgeService(isInit: true);
        }

        #endregion

        #region Server Connection Logic

        private async void StartBridgeService(bool isInit = false)
        {
            ResetCancellationToken();
            await StopBridgeServiceInternalAsync();

            // 최초 연결 시도
            await AttemptInitialConnectionAsync(isInit);
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                return;

            SetConnectedStatus();
            _bridgeService.Run();

            // 연결 후 주기적으로 서버 상태 모니터링
            await MonitorServerConnectionAsync();
        }

        /// <summary>
        /// 최초 연결을 시도하여 서버가 응답할 때까지 대기합니다.
        /// </summary>
        private async Task<bool> AttemptInitialConnectionAsync(bool isInit)
        {
            bool isConnectable = false;
            while (!isConnectable)
            {
                _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                SetConnectingStatus();
                SettingService.SetServerAddress(ServerAddress);
                isConnectable = await WebClientService.CheckConnection();

                if (!isConnectable)
                {
                    if (isInit)
                    {
                        SetInitErrorStatus();
                        Console.WriteLine("최초 서버 연결 실패");
                    }
                    else
                    {
                        SetErrorStatus();
                        Console.WriteLine("서버 연결 실패");
                    }
                    await Task.Delay(InitialConnectionDelay, _cancellationTokenSource.Token);
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
                        Console.WriteLine("실행 중 서버 연결이 끊어졌습니다.");
                        _bridgeService.Stop();
                        SetErrorStatus();

                        // 서버 복구 대기 루프
                        while (!_cancellationTokenSource.Token.IsCancellationRequested &&
                               !(await WebClientService.CheckConnection()))
                        {
                            Console.WriteLine("서버 복구 대기 중...");
                            await Task.Delay(MonitorConnectionDelay, _cancellationTokenSource.Token);
                        }

                        Console.WriteLine("서버 응답 정상 복구됨");
                        SetConnectedStatus();
                        _bridgeService.Run();
                    }
                }
                catch (OperationCanceledException)
                {
                    Console.WriteLine("모니터링 작업 취소됨");
                    break;
                }
            }
        }

        /// <summary>
        /// 현재 브릿지 서비스를 중지하고 완료될 때까지 기다립니다.
        /// </summary>
        private async Task StopBridgeServiceInternalAsync()
        {
            _bridgeService.Stop();
            await _bridgeService.WaitForCompletion();
        }

        /// <summary>
        /// 기존의 CancellationTokenSource를 취소 및 해제하고 새로 생성합니다.
        /// </summary>
        private void ResetCancellationToken()
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
            _cancellationTokenSource = new CancellationTokenSource();
        }

        private async void StopBridgeService()
        {
            await StopBridgeServiceInternalAsync();

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ConnectionStatus = ServerConnectionStatus.Idle;
            SetIdleState();
        }

        #endregion

        #region Command Handlers

        private void ExecuteConnectToServerCommand()
        {
            Console.WriteLine($"입력받은 서버 주소: {ServerAddress}");
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
            Console.WriteLine($"Selected Language: {SelectedLanguage}");
            SettingService.SetCultureInfo(new CultureInfo(SelectedLanguage));
        }

        #endregion

        #region UI Status Helpers

        private void SetIdleState()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_waiting.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_IdleMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_IdleMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Waiting;
            ConnectionStatus = ServerConnectionStatus.Idle;
        }

        private void SetConnectingStatus()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_normal.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectingMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ConnectingMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connecting;
            ConnectionStatus = ServerConnectionStatus.Connecting;
        }

        private void SetConnectedStatus()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_connected.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectedMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_DefaultMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Connected;
            ConnectionStatus = ServerConnectionStatus.Connected;
        }

        private void SetErrorStatus()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_ErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;
            ConnectionStatus = ServerConnectionStatus.Error;
        }

        private void SetInitErrorStatus()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_connect_error.png");
            _taskbarIconService.UpdateToolTipMessage(Resources.Tray_ConnectFailMessage);

            ConnectionStatusDetailText = Resources.Inline_Connection_initErrorMessage;
            FooterServerStatusText = Resources.Footer_ServerStatus_Failed;
            ConnectionStatus = ServerConnectionStatus.Init_Error;
        }

        #endregion
    }
}