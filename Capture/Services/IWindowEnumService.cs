// author: eng-fe-desktop
// phase: engineering
// preserve: User32 고수준 헬퍼 위임 — migration-mapping.md §9-4

using System.Drawing;

namespace Capture.Services;

public interface IWindowEnumService
{
    void UpdateVisibleWindowList();
    Rectangle GetWindowRectFromPoint(Point point);
    Rectangle GetWindowRectSafe(IntPtr hwnd);
}
