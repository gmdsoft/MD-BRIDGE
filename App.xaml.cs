using System.Windows;
using MD.BRIDGE.Services;
using MD.BRIDGE.ViewModels;
using MD.BRIDGE.Views;
using Application = System.Windows.Application;
using System.Threading;
using System.Linq;
using System;
using System.Runtime.CompilerServices;

namespace MD.BRIDGE
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private const string _mutexKey = "Global\\MD-BRIDGE";
        private static Mutex _mutex;

        private MainWindow _window;

        protected override void OnStartup(StartupEventArgs e)
        {

            bool createdNew;
            _mutex = new Mutex(true, _mutexKey, out createdNew);

            if (!createdNew)
            {
                Environment.Exit(0);
            }

            var cultureInfo = SettingService.GetCultureInfo();

            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;

            base.OnStartup(e);

            if (_window == null)
            {
                _window = new MainWindow();
                _window.DataContext = new MainViewModel(new TaskbarIconService());
            }

            bool trayOnly = e.Args.Contains("--tray-only");

            if (!trayOnly)
            {
                ShowMainWindow();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _mutex?.ReleaseMutex();
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
