// author: eng-fe-desktop
// phase: engineering
// preserve: 전체화면 오버레이, 마우스 이벤트, 선택 영역 직사각형 그리기
// natural-fix: Window.Closed 명시 Dispose (종료자 제거)
// 3차 DPI fix: ToDrawingPoint static → 인스턴스 메서드 (DpiHelper.DipToPixels 사용, ADR-106)
// 4차 mixed-DPI fix: ToDrawingPoint — PresentationSource 기반 GetScale 대신 ViewModel.MonitorScale 사용
//   (SourceInitialized 직후 PresentationSource CompositionTarget 갱신 타이밍 문제 회피)
// 5차 fix: UpdateCrosshair 추가 — 원본 FormRequestCapture.OnPaint line 346-351 Magenta crosshair 복원
// 9차: UpdateModeOverlays — Window 모드 빨간 rect, LastRegion 모드 파란 점선 rect (원본 line 352-370)

using System.Windows;
using System.Windows.Controls;
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
    }

    private void Window_KeyDown(object sender, WpfKeyEventArgs e)
    {
        ViewModel.OnKeyDown(e.Key);
    }

    private void Window_MouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.LeftButton == WpfMouseButtonState.Pressed)
        {
            _startPoint = e.GetPosition(this);
            _isDrawing = true;
            ViewModel.OnMouseDown(ToDrawingPoint(_startPoint));
            StartSelectionRect(_startPoint);
        }
    }

    private void Window_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        bool leftDown = e.LeftButton == WpfMouseButtonState.Pressed;
        ViewModel.OnMouseMove(ToDrawingPoint(pos), leftDown);

        if (_isDrawing && leftDown)
            UpdateSelectionRect(_startPoint, pos);

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
        ClearSelectionRect();
        ViewModel.OnMouseUp(ToDrawingPoint(e.GetPosition(this)));
    }

    private void Window_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        _isDrawing = false;
        ClearSelectionRect();
        ViewModel.OnMouseLeave();
        CrosshairV.Visibility = Visibility.Collapsed;
        CrosshairH.Visibility = Visibility.Collapsed;
    }

    private void StartSelectionRect(System.Windows.Point start)
    {
        ClearSelectionRect();
        _selectionRect = new System.Windows.Shapes.Rectangle
        {
            Stroke = WpfBrushes.Red,
            StrokeThickness = 1,
            Fill = new WpfSolidColorBrush(WpfColor.FromArgb(30, 255, 0, 0))
        };
        Canvas.SetLeft(_selectionRect, start.X);
        Canvas.SetTop(_selectionRect, start.Y);
        _selectionRect.Width = 0;
        _selectionRect.Height = 0;
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
        // 9차: ModeChanged 구독 해제
        ViewModel.ModeChanged -= UpdateModeOverlays;
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
