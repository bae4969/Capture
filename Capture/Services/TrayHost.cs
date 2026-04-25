// author: eng-fe-desktop
// phase: engineering
// ADR-102: H.NotifyIcon.Wpf — TaskbarIcon 은 App.xaml 에서 XAML 로 선언됨
// TrayHost 는 App.xaml.cs 에서 TaskbarIcon 참조를 주입받아 관리

using H.NotifyIcon;

namespace Capture.Services;

public class TrayHost : ITrayHost
{
    private readonly TaskbarIcon _taskbarIcon;
    private bool _disposed;

    public TrayHost(TaskbarIcon taskbarIcon)
    {
        _taskbarIcon = taskbarIcon;
    }

    public void Start()
    {
        _taskbarIcon.Visibility = System.Windows.Visibility.Visible;
    }

    public void ShowBalloonTip(string title, string message)
    {
        _taskbarIcon.ShowNotification(title, message);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _taskbarIcon.Dispose();
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
