// author: eng-fe-desktop
// phase: engineering
// new: PerMonitorV2 DPI 변환 헬퍼 (ADR-106) — DIP ↔ 물리 픽셀 상호 변환
// ADR-107: DllImport 유지 (후속 사이클에서 LibraryImport 전환)
// ADR-401: LibraryImport 적용 (변환 4 + fallback 1: GetMonitorInfo 의 MONITORINFOEX ByValTStr non-blittable). 클래스에 partial 키워드 추가
// 4차 mixed-DPI fix: EnumMonitors, SetWindowPosPhysical, MONITORINFOEX, MonitorEnumProc 추가

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

// Capture.Interop 에 이미 POINT struct 와 System.Drawing.Point 가 존재하므로
// WPF Point 와 Drawing Point 를 명시적 별칭으로 구분한다.
using WpfPoint = System.Windows.Point;
using WpfRect = System.Windows.Rect;

namespace Capture.Interop;

/// <summary>
/// PerMonitorV2 DPI 인식 환경에서 WPF DIP 좌표와 물리 픽셀 간 변환을 담당한다 (ADR-106).
/// MonitorFromPoint + GetDpiForMonitor(Shcore.dll) 를 사용해 위치별 DPI 를 정확히 파악한다.
/// 4차: EnumDisplayMonitors + GetMonitorInfo 로 모니터별 물리 픽셀 좌표 직접 획득.
///      SetWindowPos 로 WPF Left/Top/Width/Height 추상화를 우회해 Mixed-DPI 환경에서 정확 배치.
/// </summary>
public static partial class DpiHelper  // ADR-401: partial 키워드 추가 (LibraryImport SourceGenerator 요구)
{
    // ── Win32 P/Invoke ────────────────────────────────────────────────────────

    private const uint MONITOR_DEFAULTTONEAREST = 0x00000002;
    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint SWP_NOZORDER = 0x0004;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_NOSIZE = 0x0001;

    // ── #24 변환 ──
    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    // ── #25 변환 ──
    [LibraryImport("Shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    // ── #26 fallback (managed delegate — MonitorEnumProc 캡처 보유 inline lambda) ──
    // EnumMonitors() 내 inline lambda 가 list 를 캡처. LibraryImport SourceGenerator 가
    // closure 보유 managed delegate 를 unmanaged function pointer 로 자동 변환 불가.
    // (ADR-401 §Decision §2 #26, §Consequences §후속 사이클 검토)
#pragma warning disable SYSLIB1054 // ADR-401 fallback: managed delegate marshalling (MonitorEnumProc inline lambda 캡처)
    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip,
        MonitorEnumProc lpfnEnum, IntPtr dwData);
#pragma warning restore SYSLIB1054

    // ── #27 fallback (MONITORINFOEX ByValTStr non-blittable) ──
    // MONITORINFOEX.szDevice 의 [MarshalAs(ByValTStr, SizeConst=32)] 는
    // LibraryImport StringMarshalling 으로 인라인 fixed-size 버퍼 마샬링 불가.
    // Custom IMarshaller<MONITORINFOEX> 작성 비용 > 사용자 가시 가치.
    // (ADR-401 §Decision §2 #27, §Alternatives §Custom IMarshaller)
#pragma warning disable SYSLIB1054 // ADR-401 fallback: MONITORINFOEX [ByValTStr] non-blittable
    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);
#pragma warning restore SYSLIB1054

    // ── #28 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int X, int Y, int cx, int cy, uint uFlags);

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    // ── 공개 타입 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 단일 모니터의 물리 픽셀 좌표 및 DPI 스케일 정보.
    /// EnumMonitors() 가 반환하는 요소.
    /// </summary>
    public readonly record struct MonitorInfo(
        System.Drawing.Rectangle PhysicalBounds,
        double ScaleX,
        double ScaleY,
        bool IsPrimary,
        IntPtr HMonitor);

    // ── 공개 API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// 모든 모니터의 물리 픽셀 좌표 + DPI 스케일을 직접 반환한다.
    /// EnumDisplayMonitors + GetMonitorInfo + GetDpiForMonitor 사용.
    /// WinForms Screen.AllScreens 의존을 제거하며 Mixed-DPI 환경에서 정확한 물리 좌표를 제공한다.
    /// </summary>
    public static IReadOnlyList<MonitorInfo> EnumMonitors()
    {
        var list = new List<MonitorInfo>();

        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero,
            (hMonitor, hdc, lprcMonitor, dwData) =>
            {
                var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
                if (!GetMonitorInfo(hMonitor, ref info))
                    return true; // 계속 열거

                var rc = info.rcMonitor;
                var physBounds = new System.Drawing.Rectangle(
                    rc.Left, rc.Top, rc.Right - rc.Left, rc.Bottom - rc.Top);

                var pt = new POINT(rc.Left, rc.Top);
                int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
                double sx = hr == 0 ? dpiX / 96.0 : 1.0;
                double sy = hr == 0 ? dpiY / 96.0 : 1.0;
                bool isPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;

                list.Add(new MonitorInfo(physBounds, sx, sy, isPrimary, hMonitor));
                return true; // 계속 열거
            },
            IntPtr.Zero);

        return list;
    }

    /// <summary>
    /// HWND 를 물리 픽셀 좌표로 직접 위치·크기를 설정한다.
    /// WPF Left/Top/Width/Height 추상화를 우회하므로 PerMonitorV2 + Mixed-DPI 환경에서 정확한 배치가 가능하다.
    /// width &lt;= 0 또는 height &lt;= 0 이면 SWP_NOSIZE 플래그를 추가해 위치만 변경한다.
    /// </summary>
    public static void SetWindowPosPhysical(IntPtr hwnd, int x, int y, int width, int height)
    {
        uint flags = SWP_NOZORDER | SWP_NOACTIVATE;
        if (width <= 0 || height <= 0)
            flags |= SWP_NOSIZE;
        SetWindowPos(hwnd, IntPtr.Zero, x, y, width, height, flags);
    }

    /// <summary>
    /// 주어진 Visual 이 현재 표시되는 모니터의 DPI 스케일을 반환한다.
    /// 예: 125% → (1.25, 1.25). Visual 이 아직 표시 전(PresentationSource == null)이면
    /// 시스템 기본 DPI 를 fallback 으로 사용한다.
    /// </summary>
    public static (double X, double Y) GetScale(Visual visual)
    {
        var source = PresentationSource.FromVisual(visual);
        if (source?.CompositionTarget != null)
        {
            var m = source.CompositionTarget.TransformToDevice;
            return (m.M11, m.M22);
        }

        // Fallback: 시스템 기본 DPI (PresentationSource 없는 경우)
        return GetSystemDpiScale();
    }

    /// <summary>
    /// 주어진 물리 픽셀 좌표(physicalX, physicalY)가 속하는 모니터의 DPI 스케일을 반환한다.
    /// Window 가 아직 표시되지 않은 단계(SourceInitialized 이전)에서 사용한다.
    /// PerMonitorV2 환경에서 모니터마다 DPI 가 다를 수 있다.
    /// </summary>
    public static (double X, double Y) GetScaleForPoint(int physicalX, int physicalY)
    {
        var pt = new POINT(physicalX, physicalY);
        IntPtr hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
        if (hMonitor == IntPtr.Zero) return GetSystemDpiScale();

        int hr = GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out uint dpiY);
        if (hr != 0) return GetSystemDpiScale();

        return (dpiX / 96.0, dpiY / 96.0);
    }

    /// <summary>
    /// 물리 픽셀 좌표 (physicalX, physicalY) → WPF DIP Point 변환.
    /// 해당 위치의 모니터 DPI 를 기준으로 한다.
    /// </summary>
    public static WpfPoint PixelsToDip(int physicalX, int physicalY)
    {
        var (sx, sy) = GetScaleForPoint(physicalX, physicalY);
        return new WpfPoint(physicalX / sx, physicalY / sy);
    }

    /// <summary>
    /// 물리 픽셀 사각형 (System.Drawing.Rectangle) → WPF DIP Rect 변환.
    /// Location 기준 모니터 DPI 를 사용한다 (모니터 경계 가로지르는 경우는 Location 기준 단순화).
    /// </summary>
    public static WpfRect PixelsToDip(System.Drawing.Rectangle physical)
    {
        var (sx, sy) = GetScaleForPoint(physical.Left, physical.Top);
        return new WpfRect(
            physical.Left / sx,
            physical.Top / sy,
            physical.Width / sx,
            physical.Height / sy);
    }

    /// <summary>
    /// WPF DIP 좌표 → 물리 픽셀 변환.
    /// 주어진 Visual 이 표시되는 모니터의 DPI 스케일을 사용한다.
    /// </summary>
    public static System.Drawing.Point DipToPixels(Visual visual, WpfPoint dip)
    {
        var (sx, sy) = GetScale(visual);
        return new System.Drawing.Point(
            (int)Math.Round(dip.X * sx),
            (int)Math.Round(dip.Y * sy));
    }

    // ── 내부 헬퍼 ────────────────────────────────────────────────────────────

    /// <summary>
    /// 시스템 기본 DPI 스케일. PresentationSource 또는 MonitorFromPoint 가 실패한 경우에만
    /// 호출되는 최후 fallback. System.Drawing.Graphics.DpiX 로 물리 DPI 를 읽는다.
    /// </summary>
    private static (double X, double Y) GetSystemDpiScale()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return (g.DpiX / 96.0, g.DpiY / 96.0);
    }
}
