using System;
using System.Windows;
using System.Windows.Media.Imaging;

using Hardcodet.Wpf.TaskbarNotification;

namespace MD.BRIDGE.Services
{
    public class TaskbarIconService : ITaskbarIconService
    {
        private readonly TaskbarIcon _taskbarIcon;

        public TaskbarIconService()
        {
            _taskbarIcon = (TaskbarIcon)Application.Current.FindResource("TrayIcon");
            SetTrayIcon("Assets/tray_normal.png");
            UpdateToolTipMessage("MD-BRIDGE v1.0.0.0");
        }

        public void SetTrayIcon(string iconAssetPath)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var packUri = $"pack://application:,,,/{iconAssetPath}";
                var bitmap = new BitmapImage(new Uri(packUri, UriKind.Absolute));
                _taskbarIcon.IconSource = bitmap;
            });
        }

        public void UpdateToolTipMessage(string message)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                _taskbarIcon.ToolTipText = message;
            });
        }
    }
}
