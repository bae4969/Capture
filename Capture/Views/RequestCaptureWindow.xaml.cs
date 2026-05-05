// author: eng-fe-desktop
// phase: engineering
// preserve: 전체화면 오버레이, 마우스 이벤트, 선택 영역 직사각형 그리기
// natural-fix: Window.Closed 명시 Dispose (종료자 제거)
// 3차 DPI fix: ToDrawingPoint static → 인스턴스 메서드 (DpiHelper.DipToPixels 사용, ADR-106)
// 4차 mixed-DPI fix: ToDrawingPoint — PresentationSource 기반 GetScale 대신 ViewModel.MonitorScale 사용
//   (SourceInitialized 직후 PresentationSource CompositionTarget 갱신 타이밍 문제 회피)
// 5차 fix: UpdateCrosshair 추가 — 원본 FormRequestCapture.OnPaint line 346-351 Magenta crosshair 복원
// 9차: UpdateModeOverlays — Window 모드 빨간 rect, LastRegion 모드 파란 점선 rect (원본 line 352-370)
// 10차 cross-monitor: SetCapture + GetCursorPos + IsDragging 가드 + 절대좌표 selectionRect
// 11차 cross-monitor v2: WPF Mouse.Capture 페어링 추가 (WPF 라우팅 레이어까지 캡처) +
//   Window_MouseUp 분기 IsDragging 단일 조건 (IsCrossMonitorDrag 회귀 차단) +
//   OnWindowClosed 캡처 누수 안전망

using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using Capture.Imaging;
using Capture.Interop;
using Capture.Models;
using Capture.ViewModels;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfColor = System.Windows.Media.Color;
using WpfSolidColorBrush = System.Windows.Media.SolidColorBrush;

// WPF/WinForms 타입 충돌 방지 — 명시적 별칭
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseButtonState = System.Windows.Input.MouseButtonState;
using WpfRect = System.Windows.Rect;

namespace Capture.Views;

public partial class RequestCaptureWindow : Window
{
    public RequestCaptureViewModel ViewModel { get; }

    // 현재 선택 영역 사각형
    private System.Windows.Shapes.Rectangle? _selectionRect;
    private System.Windows.Point _startPoint;
    private bool _isDrawing;

    public RequestCaptureWindow(RequestCaptureViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;
        Closed += OnWindowClosed;

        // 스크린샷을 배경으로 설정
        if (vm.ScreenBitmap != null)
        {
            BackgroundImage.Source = BitmapInterop.ToBitmapSource(vm.ScreenBitmap);
        }

        // 9차: 모드 전환 시 오버레이 갱신 구독
        vm.ModeChanged += UpdateModeOverlays;
        // 10차 cross-monitor: 절대좌표 selectionRect 갱신 구독
        vm.SelectionUpdated += OnCrossMonitorSelectionUpdated;
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        ViewModel.OnKeyDown(e.Key);
    }

    /// <summary>
    /// 12차 hover-fix: 윈도우 표시 직후, 마우스가 정지 호버 상태일 때도 단일 모니터에만
    /// InfoPanel(매그니파이어/crosshair) 이 표시되도록 초기 동기화.
    /// _infoVisible 초기값을 false 로 내렸기 때문에 MouseMove 가 발화하지 않으면 어떤
    /// 윈도우도 패널을 표시하지 않음. 여기서 GetCursorPos 로 자기 모니터 위에 있는지
    /// 직접 확인 후 활성화한다. SourceInitialized(SetWindowPosPhysical) 이후 발화하므로
    /// ScreenBounds/MonitorScale 은 이미 설정 완료 상태.
    /// </summary>
    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        User32.GetCursorPos(out var absPt);
        if (!ViewModel.SyncCursorOnLoad(absPt)) return;

        // 자기 모니터 위 — crosshair / InfoPanel 회피 위치도 즉시 갱신.
        var bounds = ViewModel.ScreenBounds;
        var (sx, sy) = ViewModel.MonitorScale;
        var dipPos = new System.Windows.Point(
            (absPt.X - bounds.X) / sx,
            (absPt.Y - bounds.Y) / sy);
        UpdateCrosshair(dipPos);
        UpdateInfoPanelPosition(dipPos);
    }

    private void Window_MouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.LeftButton == WpfMouseButtonState.Pressed)
        {
            _startPoint = e.GetPosition(this);
            _isDrawing = true;
            // 10차 cross-monitor: 마우스가 모니터 경계를 넘어가도 본 윈도우가 계속
            // MouseMove/MouseUp 을 받도록 SetCapture. GetCursorPos 로 virtual screen
            // 절대 픽셀을 획득해 ViewModel 의 GrabPointAbsolute 기준점으로 사용.
            // 11차: WPF Mouse.Capture 도 함께 호출해 WPF hit-test/routing 레이어까지 캡처를
            // 묶는다. Win32 SetCapture 만으로는 같은 프로세스의 형제 RequestCaptureWindow 가
            // 활성화되며 본 윈도우의 Window_MouseMove 이벤트가 끊기는 환경이 존재.
            this.CaptureMouse();
            var hwnd = new WindowInteropHelper(this).Handle;
            User32.SetCapture(hwnd);
            User32.GetCursorPos(out var absPt);
            ViewModel.OnMouseDown(ToDrawingPoint(_startPoint), absPt);
            StartSelectionRect(_startPoint);
        }
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        bool leftDown = e.LeftButton == WpfMouseButtonState.Pressed;

        // 11차 v3: cross-monitor 드래그 중이면 자기 Window_MouseMove 의 로컬 갱신
        // (InfoVisible=true, UpdateCrosshair, UpdateInfoPanelPosition, OnMouseMove의 _cursorPoint
        //  로컬 갱신) 을 우회하고 broadcast 만 발화한다.
        // 이유: 시작 윈도우(A)는 커서가 B 로 가도 SetCapture 때문에 자기 MouseMove 가 계속 발화 →
        //       여기서 InfoVisible=true 로 패널이 다시 켜지면 A·B 양쪽에 매그니파이어가 잔존.
        //       broadcast 수신측(OnCrossMonitorBroadcast)이 InfoVisible 단일 진리원천이 되도록
        //       자기 윈도우도 cross-monitor 동안에는 broadcast 만 발화하고 즉시 return.
        if (ViewModel.IsDragging && leftDown)
        {
            User32.GetCursorPos(out var absPt);
            ViewModel.RaiseCrossMonitorMove(absPt, leftDown);
            return;
        }

        ViewModel.OnMouseMove(ToDrawingPoint(pos), leftDown);

        if (_isDrawing && leftDown)
        {
            // 비-Region 모드 — 기존 로컬 좌표 경로
            UpdateSelectionRect(_startPoint, pos);
        }

        ViewModel.InfoVisible = true;

        // 십자가 갱신 — 원본 FormRequestCapture.OnPaint line 346-351
        UpdateCrosshair(pos);
        // 9차: Window/LastRegion 모드 시각 피드백 갱신 — 원본 OnPaint line 352-370
        UpdateModeOverlays();
        // 원본 line 723-734: 커서가 정보 패널 근처(우상단 또는 좌하단)에 가면 반대 모서리로 회피.
        UpdateInfoPanelPosition(pos);
    }

    /// <summary>
    /// 정보 패널 회피 — 우측 고정. 기본 우상단. 커서가 현재 패널 영역에서 ProximityThreshold 이내로 근접하면
    /// 반대편(우하단↔우상단)으로 이동. 멀어져도 즉시 복귀하지 않고, 반대편에서 ProximityThreshold 이내로
    /// 근접해야 다시 전환(양방향 hysteresis 효과).
    /// </summary>
    private void UpdateInfoPanelPosition(System.Windows.Point cursor)
    {
        if (ActualWidth <= 0 || ActualHeight <= 0) return;
        InfoPanel.HorizontalAlignment = System.Windows.HorizontalAlignment.Right;

        double panelW = InfoPanel.ActualWidth > 0 ? InfoPanel.ActualWidth : 340;
        double panelH = InfoPanel.ActualHeight > 0 ? InfoPanel.ActualHeight : 380;
        double rightX = ActualWidth - panelW;

        var topRect = new WpfRect(rightX, 0, panelW, panelH);
        var bottomRect = new WpfRect(rightX, ActualHeight - panelH, panelW, panelH);

        const double ProximityThreshold = 80.0;
        bool currentlyBottom = InfoPanel.VerticalAlignment == System.Windows.VerticalAlignment.Bottom;

        if (currentlyBottom)
        {
            if (DistanceFromRect(cursor, bottomRect) < ProximityThreshold)
                InfoPanel.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        }
        else
        {
            if (DistanceFromRect(cursor, topRect) < ProximityThreshold)
                InfoPanel.VerticalAlignment = System.Windows.VerticalAlignment.Bottom;
        }
    }

    private static double DistanceFromRect(System.Windows.Point p, WpfRect r)
    {
        double dx = Math.Max(0, Math.Max(r.Left - p.X, p.X - r.Right));
        double dy = Math.Max(0, Math.Max(r.Top - p.Y, p.Y - r.Bottom));
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private void Window_MouseUp(object sender, WpfMouseButtonEventArgs e)
    {
        _isDrawing = false;
        // 10차 cross-monitor: SetCapture 명시 해제.
        // 11차: WPF Mouse.Capture 도 함께 해제 (페어링).
        if (this.IsMouseCaptured) this.ReleaseMouseCapture();
        User32.ReleaseCapture();
        ClearSelectionRect();

        // 11차 cross-monitor v2: IsDragging 단일 조건. 10차의 추가 가드(IsCrossMonitorDrag)는
        // 사용자가 cross-monitor 도중 시작 모니터로 다시 돌아와 MouseUp 하면 false 로 평가되어
        // 기존 단일 모니터 OnMouseUp 경로로 회귀했음 — 부정확한 e.GetPosition 좌표 +
        // 절대좌표 합성 캡쳐 path 누락의 원인. Region 모드는 IsDragging=true 만으로 절대좌표
        // 경로 통합 (단일 모니터 드래그도 동등한 결과: ScreenCaptureService.CaptureRegion).
        if (ViewModel.IsDragging)
        {
            User32.GetCursorPos(out var absPt);
            ViewModel.RaiseCrossMonitorUp(absPt);
            return;
        }

        ViewModel.OnMouseUp(ToDrawingPoint(e.GetPosition(this)));
    }

    private void Window_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        // 10차 cross-monitor: 드래그 중에는 모니터 경계 벗어나도 상태 유지.
        // SetCapture 로 인해 MouseMove 가 계속 들어오므로 selectionRect 도 유지해야 함.
        if (ViewModel.IsDragging) return;
        _isDrawing = false;
        ClearSelectionRect();
        ViewModel.OnMouseLeave();
        CrosshairV.Visibility = Visibility.Collapsed;
        CrosshairH.Visibility = Visibility.Collapsed;
    }

    private void StartSelectionRect(System.Windows.Point start)
    {
        ClearSelectionRect();
        EnsureSelectionRect();
        var rect = _selectionRect!;
        Canvas.SetLeft(rect, start.X);
        Canvas.SetTop(rect, start.Y);
        rect.Width = 0;
        rect.Height = 0;
    }

    /// <summary>
    /// 10차 cross-monitor: _selectionRect 가 null 이면 lazy 생성. 형제 윈도우(드래그 시작
    /// 안 한)는 MouseDown 이벤트를 받지 못해 StartSelectionRect 호출이 일어나지 않으므로
    /// SelectionUpdated 이벤트 수신 시 여기서 생성한다.
    /// </summary>
    private void EnsureSelectionRect()
    {
        if (_selectionRect != null) return;
        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = WpfBrushes.Red,
            StrokeThickness = 1,
            Fill = new WpfSolidColorBrush(WpfColor.FromArgb(30, 255, 0, 0))
        };
        SelectionCanvas.Children.Add(_selectionRect);
    }

    private void UpdateSelectionRect(System.Windows.Point start, System.Windows.Point current)
    {
        if (_selectionRect == null) return;
        double x = Math.Min(start.X, current.X);
        double y = Math.Min(start.Y, current.Y);
        double w = Math.Abs(start.X - current.X);
        double h = Math.Abs(start.Y - current.Y);
        Canvas.SetLeft(_selectionRect, x);
        Canvas.SetTop(_selectionRect, y);
        _selectionRect.Width = w;
        _selectionRect.Height = h;
    }

    private void ClearSelectionRect()
    {
        if (_selectionRect != null)
        {
            SelectionCanvas.Children.Remove(_selectionRect);
            _selectionRect = null;
        }
    }

    /// <summary>
    /// 10차 cross-monitor: ViewModel.OnMouseMoveAbsolute 가 발화한 SelectionUpdated 이벤트
    /// 핸들러. ViewModel.CurrentSelectionAbsolute(절대 픽셀) 를 자기 ScreenBounds 로 클리핑한
    /// 부분만 _selectionRect 로 표시. 다른 모니터에 걸친 부분은 그 모니터의 윈도우가 자기
    /// 클리핑으로 표시함.
    /// 11차 v3: 커서가 자기 모니터에 있으면 crosshair/InfoPanel 위치도 함께 갱신
    /// (broadcast 수신 윈도우는 자기 Window_MouseMove 가 발화 안 되므로 여기서 직접 갱신).
    /// </summary>
    private void OnCrossMonitorSelectionUpdated()
    {
        if (!ViewModel.IsDragging) return;
        EnsureSelectionRect();
        UpdateSelectionRectAbsolute();

        // 커서가 자기 모니터 위에 있으면 crosshair + InfoPanel 회피 위치 갱신.
        // 절대 픽셀 → 자기 모니터 상대 픽셀 → DIP 변환.
        var cursorAbs = ViewModel.CursorPointAbsolute;
        var bounds = ViewModel.ScreenBounds;
        if (bounds.Contains(cursorAbs))
        {
            var (sx, sy) = ViewModel.MonitorScale;
            var dipPos = new System.Windows.Point(
                (cursorAbs.X - bounds.X) / sx,
                (cursorAbs.Y - bounds.Y) / sy);
            UpdateCrosshair(dipPos);
            UpdateInfoPanelPosition(dipPos);
        }
        else
        {
            // 커서가 다른 모니터에 있으면 자기 crosshair 숨김 (InfoVisible 은 ViewModel 이 false 로
            // 내려 InfoPanel Binding 으로 자동 숨김됨).
            CrosshairV.Visibility = Visibility.Collapsed;
            CrosshairH.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// 절대좌표 선택 사각형을 자기 ScreenBounds 로 클리핑 → 자기 모니터 DPI 로 DIP 환산
    /// → _selectionRect 위치/크기 설정. 클리핑 결과 빈 사각형이면 Visibility=Collapsed.
    /// </summary>
    private void UpdateSelectionRectAbsolute()
    {
        if (_selectionRect == null) return;
        var sel = ViewModel.CurrentSelectionAbsolute;
        var bounds = ViewModel.ScreenBounds;
        sel.Intersect(bounds);

        if (sel.Width <= 0 || sel.Height <= 0)
        {
            _selectionRect.Visibility = Visibility.Collapsed;
            return;
        }

        var (sx, sy) = ViewModel.MonitorScale;
        double dipX = (sel.X - bounds.X) / sx;
        double dipY = (sel.Y - bounds.Y) / sy;
        double dipW = sel.Width / sx;
        double dipH = sel.Height / sy;

        Canvas.SetLeft(_selectionRect, dipX);
        Canvas.SetTop(_selectionRect, dipY);
        _selectionRect.Width = dipW;
        _selectionRect.Height = dipH;
        _selectionRect.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// 마우스 위치 기준 Magenta 십자가 두 라인을 갱신한다.
    /// 원본 FormRequestCapture.OnPaint line 346-351 — InfoPanel 표시 중일 때만 표시.
    /// </summary>
    private void UpdateCrosshair(System.Windows.Point pos)
    {
        if (!ViewModel.InfoVisible)
        {
            CrosshairV.Visibility = Visibility.Collapsed;
            CrosshairH.Visibility = Visibility.Collapsed;
            return;
        }
        CrosshairV.X1 = pos.X;
        CrosshairV.X2 = pos.X;
        CrosshairV.Y2 = ActualHeight;
        CrosshairH.Y1 = pos.Y;
        CrosshairH.Y2 = pos.Y;
        CrosshairH.X2 = ActualWidth;
        CrosshairV.Visibility = Visibility.Visible;
        CrosshairH.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// WPF DIP 좌표 → 물리 픽셀 변환 (ADR-106 PerMonitorV2 fix).
    /// ViewModel.MonitorScale 을 사용해 그 모니터의 DPI 스케일을 적용한다.
    /// PresentationSource 기반 GetScale 은 SetWindowPos 직후 CompositionTarget 갱신 타이밍이
    /// 보장되지 않아 Mixed-DPI 환경에서 부정확할 수 있으므로 사전 설정된 MonitorScale 을 우선한다.
    /// ScreenBitmap (물리 픽셀) 좌표계와 일치시키기 위해 사용한다.
    /// </summary>
    private System.Drawing.Point ToDrawingPoint(System.Windows.Point dipPoint)
    {
        var (sx, sy) = ViewModel.MonitorScale;
        return new System.Drawing.Point(
            (int)Math.Round(dipPoint.X * sx),
            (int)Math.Round(dipPoint.Y * sy));
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        // 11차 cross-monitor v2: 캡처 누수 방지 — MouseUp/ESC/Tab 누락 경로로도 정리.
        if (this.IsMouseCaptured) this.ReleaseMouseCapture();
        User32.ReleaseCapture();

        // 9차: ModeChanged 구독 해제
        ViewModel.ModeChanged -= UpdateModeOverlays;
        // 10차 cross-monitor: SelectionUpdated 구독 해제
        ViewModel.SelectionUpdated -= OnCrossMonitorSelectionUpdated;
        // natural-fix: 종료자 대신 명시 Dispose
        ViewModel.Dispose();
    }

    // ── 9차: UpdateModeOverlays — 원본 OnPaint line 352-370 ──────────────────
    /// <summary>
    /// 현재 캡처 모드에 따라 WindowRect(Red)·LastRegionRect(RoyalBlue Dash) 를 갱신한다.
    /// Window 모드: ViewModel.LastWindowRect (물리 픽셀) → DIP 변환 후 Canvas 위치·크기 설정.
    /// LastRegion 모드: ViewModel.LastCapturedRegion (물리 픽셀) → DIP 변환.
    /// 다른 모드: 두 rect 모두 Collapsed.
    /// </summary>
    private void UpdateModeOverlays()
    {
        var (sx, sy) = ViewModel.MonitorScale;

        switch (ViewModel.CurrentMode)
        {
            case CaptureMode.Window:
            {
                var r = ViewModel.LastWindowRect;
                if (r.Width > 0 && r.Height > 0)
                {
                    double dipX = r.X / sx;
                    double dipY = r.Y / sy;
                    double dipW = r.Width / sx;
                    double dipH = r.Height / sy;
                    Canvas.SetLeft(WindowRect, dipX);
                    Canvas.SetTop(WindowRect, dipY);
                    WindowRect.Width = dipW;
                    WindowRect.Height = dipH;
                    WindowRect.Visibility = Visibility.Visible;
                }
                else
                {
                    WindowRect.Visibility = Visibility.Collapsed;
                }
                LastRegionRect.Visibility = Visibility.Collapsed;
                break;
            }
            case CaptureMode.LastRegion:
            {
                var r = ViewModel.LastCapturedRegion;
                if (r.Width > 0 && r.Height > 0)
                {
                    double dipX = r.X / sx;
                    double dipY = r.Y / sy;
                    double dipW = r.Width / sx;
                    double dipH = r.Height / sy;
                    Canvas.SetLeft(LastRegionRect, dipX);
                    Canvas.SetTop(LastRegionRect, dipY);
                    LastRegionRect.Width = dipW;
                    LastRegionRect.Height = dipH;
                    LastRegionRect.Visibility = Visibility.Visible;
                }
                else
                {
                    LastRegionRect.Visibility = Visibility.Collapsed;
                }
                WindowRect.Visibility = Visibility.Collapsed;
                break;
            }
            default:
                WindowRect.Visibility = Visibility.Collapsed;
                LastRegionRect.Visibility = Visibility.Collapsed;
                break;
        }
    }
}
