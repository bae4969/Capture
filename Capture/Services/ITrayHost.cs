// author: eng-fe-desktop
// phase: engineering
// ADR-102: H.NotifyIcon.Wpf TaskbarIcon 호스팅 — migration-mapping.md §4-1

namespace Capture.Services;

public interface ITrayHost : IDisposable
{
    void Start();
    void ShowBalloonTip(string title, string message);
}
