// author: eng-fe-desktop
// phase: engineering
// preserve: User32 static 헬퍼 위임 — migration-mapping.md §9-4

using System.Drawing;
using Capture.Interop;

namespace Capture.Services;

public class WindowEnumService : IWindowEnumService
{
    public void UpdateVisibleWindowList()
    {
        User32.UpdateVisibleWindowList();
    }

    public Rectangle GetWindowRectFromPoint(Point point)
    {
        return User32.GetWindowRectFromPoint(point);
    }

    public Rectangle GetWindowRectSafe(IntPtr hwnd)
    {
        return User32.GetWindowRectSafe(hwnd);
    }
}
