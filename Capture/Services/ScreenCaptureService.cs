// author: eng-fe-desktop
// phase: engineering
// ADR-104: System.Drawing + Graphics.CopyFromScreen
// natural-fix: Graphics.FromImage without using → using 블록 적용

using System.Drawing;
using System.Windows.Forms;
using Capture.Interop;

namespace Capture.Services;

public class ScreenCaptureService : IScreenCaptureService
{
    public System.Drawing.Bitmap CaptureMonitor(Screen screen)
    {
        var bounds = screen.Bounds;
        var bmp = new System.Drawing.Bitmap(bounds.Width, bounds.Height);
        // natural-fix: using 적용
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
        return bmp;
    }

    public System.Drawing.Bitmap CaptureRegion(Rectangle region)
    {
        var bmp = new System.Drawing.Bitmap(region.Width, region.Height);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(region.Location, Point.Empty, region.Size);
        return bmp;
    }

    public System.Drawing.Bitmap CaptureWindow(IntPtr hwnd)
    {
        Rectangle rect = User32.GetWindowRectSafe(hwnd);
        if (rect.IsEmpty) return new System.Drawing.Bitmap(1, 1);
        return CaptureRegion(rect);
    }
}
