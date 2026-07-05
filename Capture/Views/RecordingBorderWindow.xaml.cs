// author: eng-fe-desktop
// phase: engineering
// new: 녹화 중 영역 경계 표시 오버레이. 테두리를 녹화 영역 '바깥'에 그려 녹화 영상에 안 찍히게 하고,
//      WS_EX_TRANSPARENT 로 마우스를 밑 창에 투과. 물리 픽셀 배치(SetWindowPosPhysical, ADR-106 계열).

using System.Windows;
using System.Windows.Interop;
using Capture.Interop;

namespace Capture.Views;

public partial class RecordingBorderWindow : Window
{
    // 테두리 두께(WPF DIP) — RecordingBorderWindow.xaml 의 Border.BorderThickness 와 반드시 일치.
    private const double BorderDip = 3.0;

    // 녹화 대상 영역 (virtual-screen 물리 픽셀 절대좌표). 대기 상태 드래그 이동 시 PlaceForRegion 이 갱신.
    private System.Drawing.Rectangle _region;

    public RecordingBorderWindow(System.Drawing.Rectangle region, object dataContext)
    {
        InitializeComponent();
        _region = region;
        DataContext = dataContext;   // 테두리 BorderBrush 를 RecordingBorderBrush(상태 색)에 바인딩.
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;

        // 클릭 통과 — 녹화 중 사용자가 영역 안의 앱을 계속 조작하도록 마우스를 밑 창으로 투과.
        int ex = User32.GetWindowLong(hwnd, NativeConstants.GWL_EXSTYLE);
        User32.SetWindowLong(hwnd, NativeConstants.GWL_EXSTYLE,
            ex | NativeConstants.WS_EX_LAYERED | NativeConstants.WS_EX_TRANSPARENT);

        PlaceForRegion(_region);
    }

    // 테두리 창을 region '바깥' 링에 물리 픽셀로 배치한다. 대기 상태 드래그 이동 시 재호출된다.
    // 테두리를 녹화 영역 '바깥'에 그리기 위해 창을 두께만큼 확장 배치한다.
    // BorderThickness(3 DIP)가 그 모니터 DPI 로 렌더되면 3*scale 물리 픽셀이 되므로,
    // 확장량(pad)도 동일 물리 픽셀로 맞춰야 링 안쪽 경계가 region 경계와 정확히 겹친다.
    // → region 내부(녹화 영상)에는 테두리 픽셀이 들어가지 않는다.
    public void PlaceForRegion(System.Drawing.Rectangle region)
    {
        _region = region;
        var hwnd = new WindowInteropHelper(this).Handle;

        var (sx, sy) = DpiHelper.GetScaleForPoint(_region.Left, _region.Top);
        int padX = (int)Math.Round(BorderDip * sx);
        int padY = (int)Math.Round(BorderDip * sy);

        DpiHelper.SetWindowPosPhysical(
            hwnd,
            _region.Left - padX,
            _region.Top - padY,
            _region.Width + 2 * padX,
            _region.Height + 2 * padY);
    }
}
