using System;
using System.Windows;
using System.Windows.Input;
using DevExpress.Mvvm;
using MD.BRIDGE.Services;
using MD.BRIDGE.Utils;
using MD.BRIDGE.Properties;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace MD.BRIDGE.ViewModels
{
    public enum ServerConnectionStatus
    {
        Idle,
        Connecting,
        Connected,
        Error, // 연결 실패
        Init_Error// 최초 연결 실패
    }

    public class MainViewModel : BindableBase
    {
        private BridgeService _bridgeService;

        private CancellationTokenSource _cancellationTokenSource;

        private string _serverAddress;
        public string ServerAddress
        {
            get => _serverAddress;
            set
            {
                _serverAddress = value;
                RaisePropertiesChanged();
            }
        }

        private ServerConnectionStatus _connectionStatus;
        public ServerConnectionStatus ConnectionStatus
        {
            get => _connectionStatus;
            set
            {
                _connectionStatus = value;
                RaisePropertiesChanged();
            }
        }

        private string _connectionStatusDetailText;
        public string ConnectionStatusDetailText
        {
            get => _connectionStatusDetailText;
            set
            {
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
            set
            {
                _footerServerStatusText = value;
                RaisePropertiesChanged();
            }
        }

        private string _buildVersionText;
        public string BuildVersionText
        {
            get => _buildVersionText;
            set
            {
                _buildVersionText = value;
                RaisePropertiesChanged();
            }
        }

        public ICommand ConnectToServerCommand { get; }
        public ICommand ApplyLanguageCommand { get; }

        private readonly ITaskbarIconService _taskbarIconService;

        public MainViewModel(ITaskbarIconService taskbarIconService)
        {
            ConnectToServerCommand = new DelegateCommand(ExecuteConnectToServerCommand);
            ApplyLanguageCommand = new DelegateCommand(ExecuteApplyLanguageCommand);

            _taskbarIconService = taskbarIconService;
            _bridgeService = new BridgeService();

            ServerAddress = SettingService.GetServerAddress();
            SelectedLanguage = SettingService.GetCultureInfo().Name;

            FooterServerStatusText = Resources.Footer_ServerStatus_Waiting;

            BuildVersionText = "v1.0.0.0";

            StartBridgeService(IsInit: true);
        }

        private async void StartBridgeService(bool IsInit = false)
        {
            if (_cancellationTokenSource != null)
            {
                _cancellationTokenSource.Cancel();
                _cancellationTokenSource.Dispose();
            }
            _cancellationTokenSource = new CancellationTokenSource();

            _bridgeService.Stop();
            await _bridgeService.WaitForCompletion();

            var isConnectable = false;

            try
            {
                while (!isConnectable)
                {
                    // 취소 요청이 들어왔는지 체크
                    _cancellationTokenSource.Token.ThrowIfCancellationRequested();

                    SetConnectingStatus();
                    SettingService.SetServerAddress(ServerAddress);
                    isConnectable = await WebClientService.CheckConnection();

                    if (!isConnectable)
                    {
                        if (IsInit)
                        {
                            SetInitErrorStatus();
                            Console.WriteLine("최초 서버 연결 실패");
                        }
                        else
                        {
                            SetErrorStatus();
                            Console.WriteLine("서버 연결 실패");
                        }
                        await Task.Delay(3000, _cancellationTokenSource.Token);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("서버 연결 취소");
                SetIdleState();
                return;
            }

            SetConnectedStatus();
            _bridgeService.Run();
        }

        private async void StopBridgeService()
        {
            // 취소 요청 후 작업 완료 대기 및 정리
            _bridgeService.Stop();
            await _bridgeService.WaitForCompletion();

            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _cancellationTokenSource = null;

            ConnectionStatus = ServerConnectionStatus.Idle;
            SetIdleState();
        }

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

        private void SetIdleState()
        {
            _taskbarIconService.SetTrayIcon("Assets/tray_waiting.png");
            _taskbarIconService.UpdateToolTipMessage("");

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
    }
}
