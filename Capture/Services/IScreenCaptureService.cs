// author: eng-fe-desktop
// phase: engineering
// ADR-104: System.Drawing + Graphics.CopyFromScreen — migration-mapping.md §7-1

using System.Drawing;

namespace Capture.Services;

public interface IScreenCaptureService
{
    System.Drawing.Bitmap CaptureRegion(Rectangle region);
    System.Drawing.Bitmap CaptureWindow(IntPtr hwnd);
}
