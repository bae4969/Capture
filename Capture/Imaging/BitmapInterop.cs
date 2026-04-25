// author: eng-fe-desktop
// phase: engineering
// new: System.Drawing.Bitmap ↔ BitmapSource 변환 (ADR-105) — migration-mapping.md §11-1
// CreateBitmapSourceFromHBitmap + DeleteObject (GDI 핸들 누수 방지)
// 3차 DPI fix: ToBitmapSource(bitmap, dpi) 오버로드 추가 — BitmapSource.Create 로 DPI 메타 재지정
// ADR-401: LibraryImport 적용 (DeleteObject). 클래스에 partial 키워드 추가

using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media.Imaging;

// WpfImaging 으로 Capture.Imaging 네임스페이스와 충돌 방지
using WpfImaging = System.Windows.Interop.Imaging;

namespace Capture.Imaging;

/// <summary>
/// System.Drawing.Bitmap ↔ WPF BitmapSource 변환 헬퍼 (ADR-105)
/// </summary>
public static partial class BitmapInterop  // ADR-401: partial 키워드 추가 (LibraryImport SourceGenerator 요구)
{
    // ── #29 변환 (기존 [return:MarshalAs(Bool)] 보존) ──
    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr hObject);

    /// <summary>
    /// System.Drawing.Bitmap 을 WPF BitmapSource 로 변환 (DPI = 96 고정).
    /// 반드시 GDI 핸들을 DeleteObject 로 해제한다.
    /// </summary>
    public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap)
        => ToBitmapSource(bitmap, dpi: 96.0);

    /// <summary>
    /// System.Drawing.Bitmap 을 WPF BitmapSource 로 변환, DPI 를 명시적으로 지정한다.
    /// dpi == 96 이면 CreateBitmapSourceFromHBitmap 결과를 그대로 반환한다.
    /// dpi != 96 이면 BitmapSource.Create 로 픽셀은 그대로 유지하고 DPI 메타데이터만 재지정한다.
    /// PerMonitorV2(ADR-106) 환경에서 CapturedWindow 의 표시 DPI 를 정확히 반영하기 위해 사용한다.
    /// </summary>
    public static BitmapSource ToBitmapSource(System.Drawing.Bitmap bitmap, double dpi)
    {
        IntPtr hBitmap = bitmap.GetHbitmap();
        try
        {
            var src = WpfImaging.CreateBitmapSourceFromHBitmap(
                hBitmap,
                IntPtr.Zero,
                Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());

            if (Math.Abs(dpi - 96.0) < 0.01)
                return src;

            // 픽셀 데이터는 그대로 복사하고 DPI 메타데이터만 변경한다.
            // BitmapSource.Create 에 dpiX/dpiY 를 지정하면 WPF 렌더러가
            // PixelWidth * 96 / dpi = DIP 로 계산하므로 올바른 물리 픽셀 크기가 표시된다.
            int bpp = src.Format.BitsPerPixel;
            int stride = (src.PixelWidth * bpp + 7) / 8;
            byte[] pixels = new byte[src.PixelHeight * stride];
            src.CopyPixels(pixels, stride, 0);

            return BitmapSource.Create(
                src.PixelWidth, src.PixelHeight,
                dpi, dpi,
                src.Format, src.Palette,
                pixels, stride);
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// WPF BitmapSource 를 System.Drawing.Bitmap 으로 변환.
    /// 호출자가 반환값의 Dispose() 를 담당한다.
    /// </summary>
    public static System.Drawing.Bitmap ToBitmap(BitmapSource bitmapSource)
    {
        // 32bpp ARGB 형식으로 변환
        var formatConverted = new FormatConvertedBitmap(
            bitmapSource,
            System.Windows.Media.PixelFormats.Bgra32,
            null, 0);

        int width = formatConverted.PixelWidth;
        int height = formatConverted.PixelHeight;
        int stride = width * 4;
        byte[] pixels = new byte[height * stride];
        formatConverted.CopyPixels(pixels, stride, 0);

        var bitmap = new System.Drawing.Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        var bitmapData = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.WriteOnly,
            bitmap.PixelFormat);
        Marshal.Copy(pixels, 0, bitmapData.Scan0, pixels.Length);
        bitmap.UnlockBits(bitmapData);
        return bitmap;
    }
}
