using System.Windows;
using MD.BRIDGE.Services;
using MD.BRIDGE.ViewModels;
using MD.BRIDGE.Views;
using Application = System.Windows.Application;
using System.Threading;
using System.Linq;
using System;
using LogModule;

namespace MD.BRIDGE
{
    public partial class App : Application
    {
        private MainWindow _window;
        private const string _mutexKey = "Global\\MD-BRIDGE";
        private static Mutex _mutex;

        protected override void OnStartup(StartupEventArgs e)
        {
            // GMD Logger 초기화
            //LoggerService.Instance.InitializeLogger("MD-Series", "MD-BRIDGE");

            if (!AcquireMutex())
            {
                //Logger.Error("Another instance is already running. Exiting application.");
                Environment.Exit(0);
            }

            //Logger.Info("Mutex acquired successfully.");

            InitializeApp(e);
        }

        private bool AcquireMutex()
        {
            bool createdNew;
            _mutex = new Mutex(true, _mutexKey, out createdNew);
            return createdNew;
        }

        private void InitializeApp(StartupEventArgs e)
        {
            //Logger.Info("Initializing application.");

            var cultureInfo = SettingService.GetCultureInfo();
            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;
            //Logger.Info($"Culture set to: {cultureInfo}");

            base.OnStartup(e);

            bool isTrayOnly = e.Args.Contains("--tray-only");
            //Logger.Info($"Startup mode: {(isTrayOnly ? "Tray only" : "With window")}");

            if (!isTrayOnly)
            {
                ShowMainWindow();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
            //Logger.Info("Mutex is released. Application exited.");
            
            base.OnExit(e);
        }

        private void MenuItem_Open_Click(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void MenuItem_Exit_Click(object sender, RoutedEventArgs e)
        {
            Current.Shutdown();
        }

        private void TaskbarIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            ShowMainWindow();
        }

        private void ShowMainWindow()
        {
            if (_window == null)
            {
                _window = new MainWindow();
                _window.DataContext = new MainViewModel(new TaskbarIconService());
            }

            _window.Show();
        }
    }
}
