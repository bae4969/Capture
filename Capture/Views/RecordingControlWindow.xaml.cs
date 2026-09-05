// author: eng-fe-desktop
// phase: engineering
// new(frame-based): 프레임 기반 녹화 컨트롤 바 창. RecordingBorderWindow 의 물리 픽셀 배치 패턴을 차용하되
//   클릭 가능해야 하므로 WS_EX_TRANSPARENT(클릭통과)를 붙이지 않는다.
//   버튼/경과시간의 상태는 MainViewModel(DataContext)에 바인딩 — 상태머신과 타이머를 한곳에 모아
//   Pause 시 타이머 정지 로직을 단순하게 유지한다. 이 창은 배치(PlaceForRegion)·DataContext 할당 +
//   대기·녹화 중 region 이동용 배경 드래그(RegionDragStarted/RegionDraggedTo 발화)를 담당한다.

using System.Windows;
using System.Windows.Interop;
using Capture.Interop;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;

namespace Capture.Views;

public partial class RecordingControlWindow : Window
{
    // 녹화 대상 영역 (virtual-screen 물리 픽셀 절대좌표) — 컨트롤 바를 이 영역 바깥에 붙인다.
    // 대기·녹화 중 드래그 이동 시 PlaceForRegion 이 갱신.
    private System.Drawing.Rectangle _region;

    // 영역과 컨트롤 바 사이 여백(WPF DIP) — 그 모니터 DPI 로 환산해 물리 픽셀 간격으로 배치.
    private const double GapDip = 6.0;

    // 바 배경 드래그로 region 을 이동. 시작 알림 + 시작 대비 '총' 이동(물리 픽셀)을 발화한다.
    // 증분 누적이 아니라 드래그 시작 스냅샷 기준 절대 이동이라, 시작 순간 점프가 없다.
    public event Action? RegionDragStarted;
    public event Action<int, int>? RegionDraggedTo;

    // 드래그 상태 + 드래그 시작 시점의 절대 커서 위치(물리 픽셀).
    private bool _dragging;
    private System.Drawing.Point _dragStartCursor;

    public RecordingControlWindow(System.Drawing.Rectangle region, object dataContext)
    {
        InitializeComponent();
        _region = region;
        DataContext = dataContext;
        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        PlaceForRegion(_region);
    }

    // 컨트롤 바를 region 기준으로 물리 픽셀 배치한다. 대기·녹화 중 드래그 이동·접힘/펼침 후 재호출.
    public void PlaceForRegion(System.Drawing.Rectangle region)
    {
        _region = region;
        var hwnd = new WindowInteropHelper(this).Handle;

        // SizeToContent 로 이미 확정된 물리 픽셀 크기 획득 (ActualWidth/Height 는 DIP 라 스케일 환산).
        var (sx, sy) = DpiHelper.GetScaleForPoint(_region.Left, _region.Top);
        int barW = (int)Math.Ceiling(ActualWidth * sx);
        int barH = (int)Math.Ceiling(ActualHeight * sy);
        int gap = (int)Math.Round(GapDip * sy);

        // 모든 모니터 작업 영역(WorkArea)의 합집합 — 폴백용 경계.
        var monitors = DpiHelper.EnumMonitors();
        int vLeft = monitors.Min(m => m.WorkArea.Left);
        int vTop = monitors.Min(m => m.WorkArea.Top);
        int vRight = monitors.Max(m => m.WorkArea.Right);
        int vBottom = monitors.Max(m => m.WorkArea.Bottom);

        // region 좌상단이 속한 모니터 — 바를 이 모니터 안에 배치·clamp 한다. virtual screen 전체를
        // 기준으로 하면 세로 오프셋 멀티모니터에서 '위/아래 바깥'이 어떤 모니터에도 없는 빈 공간이 되어
        // 바가 사라진다(사용자 보고: 모니터 가장자리로 가면 핸들이 없어짐). 못 찾으면 합집합 폴백.
        // 경계는 모니터 전체(PhysicalBounds)가 아니라 작업 영역(WorkArea)을 쓴다 — 전체를 쓰면 영역이
        // 화면 아래쪽일 때 바가 작업표시줄 자리에 놓여 그 아래로 가려진다(사용자 보고).
        // 모니터 탐색 자체는 PhysicalBounds 기준 — region 좌상단이 작업표시줄 위일 수도 있어서.
        var mon = monitors.FirstOrDefault(m => m.PhysicalBounds.Contains(_region.Left, _region.Top));
        var bounds = mon.WorkArea.Width > 0
            ? mon.WorkArea
            : System.Drawing.Rectangle.FromLTRB(vLeft, vTop, vRight, vBottom);

        int x = _region.Left;

        // 세로 배치: 영역 '위 바깥' 우선(녹화 영상에 안 찍힘). 그 모니터 위 공간 없으면 '아래 바깥',
        // 둘 다 없으면(전체화면 높이) 영역 '안 상단'(버튼 항상 클릭 가능, 전체화면은 자동 접힘으로 가림 최소).
        int y = _region.Top - gap - barH;
        if (y < bounds.Top)
        {
            int below = _region.Bottom + gap;
            y = below + barH <= bounds.Bottom ? below : _region.Top + gap;
        }

        // 좌우·세로 모두 그 모니터 안으로 clamp — 어떤 가장자리에서도 바가 화면 밖으로 사라지지 않게.
        if (x + barW > bounds.Right) x = bounds.Right - barW;
        if (x < bounds.Left) x = bounds.Left;
        if (y + barH > bounds.Bottom) y = bounds.Bottom - barH;
        if (y < bounds.Top) y = bounds.Top;

        DpiHelper.SetWindowPosPhysical(hwnd, x, y, barW, barH);
    }

    // ── 대기·녹화 중 region 이동 (컨트롤 바 배경 드래그) ─────────────────────────────
    // 버튼 클릭은 ButtonBase 가 MouseLeftButtonDown 을 e.Handled 처리하므로 여기 도달하지 않는다
    // (배경/빈 영역 드래그만 이동). 캡처 페어링·절대 커서 delta 는 RequestCaptureWindow 패턴 재사용.

    private void Window_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        // 캡처 획득(CaptureMouse/SetCapture)이 MouseMove 를 동기 트리거할 수 있어, 시작 커서·스냅샷을
        // '먼저' 확정한다. 안 그러면 두 번째 드래그부터 캡처 유발 MouseMove 가 이전 _dragStartCursor·
        // _dragStartRegion 기준 큰 delta 를 적용해 region 이 점프한다(첫 드래그는 스냅샷이 Empty 라 가드에 걸려 무증상).
        User32.GetCursorPos(out _dragStartCursor);
        // 시작 시점 알림 — MainViewModel 이 현재 region 을 스냅샷으로 잡는다(이후 총 이동을 여기 더함).
        RegionDragStarted?.Invoke();
        _dragging = true;
        // WPF + Win32 캡처 페어링 — 커서가 바 밖으로 나가도 MouseMove/Up 을 계속 받게.
        CaptureMouse();
        var hwnd = new WindowInteropHelper(this).Handle;
        User32.SetCapture(hwnd);
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != WpfMouseButtonState.Pressed) return;
        User32.GetCursorPos(out var cur);
        // 드래그 시작 대비 '총' 이동 — MainViewModel 이 시작 스냅샷 region 에 더해 절대 위치로 배치.
        // 증분 누적이 아니라 시작 기준이라 프레임 누락·첫 delta 오차로 인한 점프가 없다.
        RegionDraggedTo?.Invoke(cur.X - _dragStartCursor.X, cur.Y - _dragStartCursor.Y);
    }

    private void Window_MouseLeftButtonUp(object sender, WpfMouseButtonEventArgs e)
    {
        if (!_dragging) return;
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        User32.ReleaseCapture();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        // 창 닫힘(완료/실패/취소) 경로로도 캡처 누수 방지 — RequestCaptureWindow 안전망 패턴.
        if (IsMouseCaptured) ReleaseMouseCapture();
        User32.ReleaseCapture();
    }
}
