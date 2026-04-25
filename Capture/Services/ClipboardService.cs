// author: eng-fe-desktop
// phase: engineering
// WPF Clipboard API 사용 (System.Windows.Clipboard)
// UseWindowsForms=true 로 인해 WinForms Clipboard 와 충돌 — 명시 별칭

using Capture.Imaging;
using WpfClipboard = System.Windows.Clipboard;

namespace Capture.Services;

public class ClipboardService : IClipboardService
{
    public void SetImage(System.Drawing.Bitmap bitmap)
    {
        var bitmapSource = BitmapInterop.ToBitmapSource(bitmap);
        WpfClipboard.SetImage(bitmapSource);
    }

    public void SetText(string text)
    {
        WpfClipboard.SetText(text);
    }
}
