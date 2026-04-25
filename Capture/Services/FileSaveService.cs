// author: eng-fe-desktop
// phase: engineering
// natural-fix: "yyyMMddHHmmss" → "yyyyMMddHHmmss" (4자리 연도)

using System.Drawing.Imaging;
using System.IO;

namespace Capture.Services;

public class FileSaveService : IFileSaveService
{
    private readonly ISettingsService _settings;

    public FileSaveService(ISettingsService settings)
    {
        _settings = settings;
    }

    public string SaveAuto(System.Drawing.Bitmap bitmap)
    {
        // natural-fix: "yyyMMddHHmmss" → "yyyyMMddHHmmss"
        string fileName = DateTime.Now.ToString("yyyyMMddHHmmss") + ".png";
        string dir = _settings.AutoSavePath;

        if (!Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, fileName);
        bitmap.Save(path, ImageFormat.Png);
        return path;
    }

    public void SaveAs(System.Drawing.Bitmap bitmap, string path, ImageFormat format)
    {
        bitmap.Save(path, format);
    }
}
