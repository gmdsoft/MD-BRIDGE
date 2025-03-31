using System.Windows;
using MD.BRIDGE.Services;
using MD.BRIDGE.ViewModels;
using MD.BRIDGE.Views;
using Application = System.Windows.Application;
using System.Threading;

namespace MD.BRIDGE
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private MainWindow _window;

        protected override void OnStartup(StartupEventArgs e)
        {
            var cultureInfo = SettingService.GetCultureInfo();

            Thread.CurrentThread.CurrentCulture = cultureInfo;
            Thread.CurrentThread.CurrentUICulture = cultureInfo;

            base.OnStartup(e);

            ShowMainWindow();
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
