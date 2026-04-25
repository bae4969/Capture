// author: eng-fe-desktop
// phase: engineering

using System.Drawing.Imaging;

namespace Capture.Services;

public interface IFileSaveService
{
    /// <summary>자동저장 — yyyyMMddHHmmss 타임스탬프 파일명 (natural-fix: yyyMMddHHmmss → yyyyMMddHHmmss)</summary>
    string SaveAuto(System.Drawing.Bitmap bitmap);
    /// <summary>다른 이름으로 저장</summary>
    void SaveAs(System.Drawing.Bitmap bitmap, string path, ImageFormat format);
}
