// author: eng-fe-desktop
// phase: engineering

using System.Drawing;

namespace Capture.Services;

public interface IClipboardService
{
    void SetImage(System.Drawing.Bitmap bitmap);
    void SetText(string text);
}
