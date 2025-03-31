namespace MD.BRIDGE.Services
{
    public interface ITaskbarIconService
    {
        void SetTrayIcon(string iconPath);
        void UpdateToolTipMessage(string message);
    }
}
