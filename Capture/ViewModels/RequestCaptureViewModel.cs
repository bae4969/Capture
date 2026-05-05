// author: eng-fe-desktop
// phase: engineering
// preserve: 마우스 캡처 로직·색상 피커·모드 표시 — migration-mapping.md §14-1
// natural-fix: ~FormRequestCapture() 종료자 제거 → Window.Closed 명시 Dispose
// 4차 mixed-DPI fix: MonitorScale 프로퍼티 추가 — ToDrawingPoint 가 ViewModel.MonitorScale 사용
// 9차: CurrentMode·LastCapturedRegion·LastWindowRect 프로퍼티 노출. ModeChanged 이벤트 추가.
// 10차 cross-monitor: GrabPointAbsolute/CursorPointAbsolute/IsDragging + CrossMonitorMove/Up/SelectionUpdated 이벤트
//   — 모니터 경계 가로지르는 드래그 지원 (OnMouseLeave 가드 포함)
// 11차 cross-monitor v2: OnMouseMoveAbsolute → OnCrossMonitorBroadcast (grabAbsolute 도 propagate)
//   — 형제 ViewModel 의 IsDragging guard 로 selectionRect 미생성되던 버그 fix

using System.Drawing;
using Capture.Imaging;
using Capture.Interop;
using Capture.Models;
using Capture.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
// WPF CaptureMode 와 충돌 방지 — 명시적 별칭
using AppCaptureMode = Capture.Models.CaptureMode;
using WpfKey = System.Windows.Input.Key;

namespace Capture.ViewModels;

public partial class RequestCaptureViewModel : ObservableObject, IDisposable
{
    public const int MINIMUM_CAPTURE_SIZE = 10;
    public const int SIZE_MAGNIFIER = 65;

    private readonly ICaptureModeService _captureMode;
    private readonly IWindowEnumService _windowEnum;

    [ObservableProperty] private string _modeText = string.Empty;
    [ObservableProperty] private string _indicatorText = string.Empty;
    // 12차 hover-fix: InfoPanel 초기값 false. 호버 단계에서 마우스가 들어간 적 없는 형제
    // 윈도우는 OnMouseMove 가 발화 안 되어 true 가 그대로 남아 모든 모니터에 매그니파이어
    // 패널이 표시되던 버그. SyncCursorOnLoad 또는 Window_MouseMove 가 자기 모니터에서
    // 활성화 시점에만 true 로 set.
    [ObservableProperty] private bool _infoVisible = false;
    [ObservableProperty] private System.Drawing.Bitmap? _magnifierBitmap;
    [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _magnifierSource;
    [ObservableProperty] private bool _isBlinking;

    // 캡처 상태
    private bool _isGrab;
    private Point _grabPoint;
    private Point _cursorPoint;
    private Rectangle _lastWindowRect;
    private Point _lastWindowGrabPosition;

    public System.Drawing.Bitmap? ScreenBitmap { get; set; }
    public int IdxScreen { get; set; }
    public Color PickedColor { get; private set; }
    public Rectangle ScreenBounds { get; set; }

    // ── 10차 cross-monitor: 모니터 경계 가로지르는 드래그용 절대좌표 상태 ─────
    // virtual screen 절대 픽셀 기준. ScreenBounds 와 무관하게 모든 형제 윈도우가 공유.
    // MouseDown 시 1회 설정 후 변경되지 않는 기준점.
    public Point GrabPointAbsolute { get; private set; }
    // MouseMove 시 갱신되는 현재 커서 절대 픽셀.
    public Point CursorPointAbsolute { get; private set; }
    // 드래그 진행 플래그. Region 모드에서만 true. MouseLeave 가드 + broadcast 게이트.
    public bool IsDragging { get; private set; }

    /// <summary>현재 선택 영역 (virtual screen 절대 픽셀, 정규화)</summary>
    public Rectangle CurrentSelectionAbsolute
    {
        get
        {
            int x = Math.Min(GrabPointAbsolute.X, CursorPointAbsolute.X);
            int y = Math.Min(GrabPointAbsolute.Y, CursorPointAbsolute.Y);
            int w = Math.Abs(GrabPointAbsolute.X - CursorPointAbsolute.X);
            int h = Math.Abs(GrabPointAbsolute.Y - CursorPointAbsolute.Y);
            return new Rectangle(x, y, w, h);
        }
    }

    /// <summary>현재 선택 영역이 본 ViewModel 의 ScreenBounds 를 벗어나는가</summary>
    public bool IsCrossMonitorDrag
    {
        get
        {
            var sel = CurrentSelectionAbsolute;
            if (sel.Width == 0 || sel.Height == 0) return false;
            return !ScreenBounds.Contains(sel);
        }
    }

    /// <summary>
    /// 이 ViewModel 이 담당하는 모니터의 DPI 스케일 (ScaleX, ScaleY).
    /// MainViewModel.RequestCaptureAsync 에서 EnumMonitors() 결과로 설정된다.
    /// RequestCaptureWindow.ToDrawingPoint 가 DIP → 물리 픽셀 변환 시 사용한다.
    /// SourceInitialized 이전 PresentationSource 갱신 타이밍 문제를 회피하기 위해
    /// PresentationSource 기반 GetScale 대신 이 값을 사용한다.
    /// </summary>
    public (double X, double Y) MonitorScale { get; set; } = (1.0, 1.0);

    // ── 9차: View 에서 읽는 노출 프로퍼티 ───────────────────────────────────
    /// <summary>현재 캡처 모드 (UpdateModeOverlays 에서 참조)</summary>
    public AppCaptureMode CurrentMode => _captureMode.CurrentMode;

    /// <summary>마지막 캡처 영역 (LastRegion 모드 시각 피드백용)</summary>
    public Rectangle LastCapturedRegion => _captureMode.LastCapturedRegion;

    /// <summary>Window 모드에서 마우스 아래 윈도우 rect (물리 픽셀, 스크린 상대)</summary>
    public Rectangle LastWindowRect => _lastWindowRect;

    /// <summary>모드 전환 시 View 에 알림 (UpdateModeOverlays 갱신 트리거)</summary>
    public event Action? ModeChanged;

    // 결과 이벤트
    public event Action<Rectangle, System.Drawing.Bitmap?>? CaptureCompleted;
    public event Action? CaptureCancel;

    // ── 10차 cross-monitor broadcast 시그널 (11차: 시그니처 확장) ────────────
    /// <summary>View 가 cross-monitor 드래그 진행 중 형제 윈도우 동기화를 요청한다.
    /// 11차: 형제 ViewModel 도 selectionRect 를 그릴 수 있도록 grabAbsolute 도 함께 전달.
    /// 인자: (grabAbsolute, cursorAbsolute, leftButtonDown)</summary>
    public event Action<Point, Point, bool>? CrossMonitorMove;
    /// <summary>View 가 cross-monitor 드래그 종료를 알린다. 인자: 절대좌표 픽셀</summary>
    public event Action<Point>? CrossMonitorUp;
    /// <summary>OnMouseMoveAbsolute 수신 후 View 가 selectionRect 다시 그리도록 알림</summary>
    public event Action? SelectionUpdated;

    public RequestCaptureViewModel(
        ICaptureModeService captureMode,
        IWindowEnumService windowEnum)
    {
        _captureMode = captureMode;
        _windowEnum = windowEnum;
        UpdateModeText();
    }

    public void OnMouseDown(Point position, Point absolutePosition)
    {
        _isGrab = true;
        _grabPoint = position;
        _cursorPoint = position;

        // 10차 cross-monitor: Region 모드만 IsDragging 활성화.
        // 다른 모드(Window/LastRegion/ColorPick)는 cross-monitor 경로 진입 차단.
        if (_captureMode.CurrentMode == AppCaptureMode.Region)
        {
            GrabPointAbsolute = absolutePosition;
            CursorPointAbsolute = absolutePosition;
            IsDragging = true;
        }
        else
        {
            IsDragging = false;
        }

        if (_captureMode.CurrentMode == AppCaptureMode.LastRegion)
        {
            if (_captureMode.LastCapturedRegion.Contains(position))
            {
                _lastWindowGrabPosition = new Point(
                    _captureMode.LastCapturedRegion.X,
                    _captureMode.LastCapturedRegion.Y);
            }
        }
        UpdateIndicator();
    }

    /// <summary>
    /// 12차 hover-fix: Window.Loaded 시점에 호출. 절대 픽셀 커서 위치가 자기 ScreenBounds
    /// 안에 있으면 _cursorPoint 동기화 + InfoVisible=true + 매그니파이어/인디케이터 즉시 갱신.
    /// 영역 밖이면 InfoVisible=false 유지 (다른 모니터 윈도우만 패널 표시).
    /// MouseMove 가 한 번도 발화하지 않은 정지 호버 상태에서도 단일 모니터 패널 보장.
    /// </summary>
    /// <returns>커서가 자기 모니터에 있어 활성화됐는지 여부</returns>
    public bool SyncCursorOnLoad(Point cursorAbsolute)
    {
        if (!ScreenBounds.Contains(cursorAbsolute))
        {
            InfoVisible = false;
            return false;
        }
        _cursorPoint = new Point(
            cursorAbsolute.X - ScreenBounds.X,
            cursorAbsolute.Y - ScreenBounds.Y);
        InfoVisible = true;
        UpdateMagnifier();
        UpdateIndicator();
        return true;
    }

    public void OnMouseMove(Point position, bool leftButtonDown)
    {
        _cursorPoint = position;

        if (leftButtonDown && _captureMode.CurrentMode == AppCaptureMode.LastRegion && _isGrab)
        {
            int nx = _lastWindowGrabPosition.X - (_grabPoint.X - _cursorPoint.X);
            int ny = _lastWindowGrabPosition.Y - (_grabPoint.Y - _cursorPoint.Y);
            _captureMode.LastCapturedRegion = new Rectangle(
                nx, ny,
                _captureMode.LastCapturedRegion.Width,
                _captureMode.LastCapturedRegion.Height);
        }

        if (_captureMode.CurrentMode == AppCaptureMode.Window)
        {
            var pt = new System.Drawing.Point(
                position.X + ScreenBounds.X,
                position.Y + ScreenBounds.Y);
            _lastWindowRect = _windowEnum.GetWindowRectFromPoint(pt);
            // 스크린 상대 좌표로 변환
            _lastWindowRect.Offset(-ScreenBounds.X, -ScreenBounds.Y);
        }

        UpdateMagnifier();
        UpdateIndicator();
    }

    /// <summary>
    /// 11차 cross-monitor v2: MainViewModel 의 broadcast 진입점. 모든 형제 ViewModel 이
    /// 호출되며, 형제는 MouseDown 을 받지 않아 GrabPointAbsolute/IsDragging 이 default 상태.
    /// 본 메서드가 grabAbsolute 까지 받아 동기화하므로 형제 윈도우의 selectionRect 도
    /// 자기 ScreenBounds 클리핑으로 정상 표시된다.
    /// (10차의 OnMouseMoveAbsolute 는 IsDragging guard 로 형제를 즉시 return 시켜
    ///  형제 selectionRect 가 절대 그려지지 않는 버그가 있었음.)
    /// 11차 v3: 커서가 위치한 모니터의 InfoPanel(매그니파이어/인디케이터) 도 활성화되도록
    /// InfoVisible=true + UpdateMagnifier 호출. 자기 ScreenBounds 안에 커서가 있을 때만
    /// 매그니파이어를 갱신하고, 영역 밖이면 InfoPanel 숨김 (다른 모니터 InfoPanel 만 표시).
    /// </summary>
    public void OnCrossMonitorBroadcast(Point grabAbsolute, Point cursorAbsolute, bool leftButtonDown)
    {
        if (!leftButtonDown) return;

        GrabPointAbsolute = grabAbsolute;
        CursorPointAbsolute = cursorAbsolute;
        IsDragging = true;

        // 자기 모니터 영역 안에서의 로컬 픽셀로도 동기화 — UpdateIndicator 의
        // _cursorPoint 의존성과 ScreenBitmap.GetPixel 색상 표시를 위해.
        _cursorPoint = new Point(
            cursorAbsolute.X - ScreenBounds.X,
            cursorAbsolute.Y - ScreenBounds.Y);

        // 커서가 자기 모니터 위에 있을 때만 InfoPanel(매그니파이어 포함) 활성화 + 갱신.
        // 다른 모니터의 윈도우는 InfoVisible=false 로 숨겨 한 화면에만 패널이 표시되도록.
        bool cursorOnThisMonitor = ScreenBounds.Contains(cursorAbsolute);
        if (cursorOnThisMonitor)
        {
            InfoVisible = true;
            UpdateMagnifier();
        }
        else
        {
            InfoVisible = false;
        }

        SelectionUpdated?.Invoke();
        UpdateIndicator();
    }

    /// <summary>
    /// 10차 cross-monitor: View 가 cross-monitor 드래그 종료 시 호출. 절대좌표
    /// 사각형을 CaptureCompleted 로 발사 (bitmap=null 로 MainViewModel 이
    /// ScreenCaptureService.CaptureRegion 으로 합성 캡쳐 책임).
    /// </summary>
    public void OnMouseUpAbsolute(Point absolutePosition)
    {
        if (!IsDragging) return;
        CursorPointAbsolute = absolutePosition;
        var sel = CurrentSelectionAbsolute;
        IsDragging = false;
        _isGrab = false;

        if (sel.Width * sel.Height < MINIMUM_CAPTURE_SIZE)
        {
            CaptureCancel?.Invoke();
            return;
        }

        // bitmap=null → MainViewModel 이 sel(절대좌표) 로 합성 캡쳐 처리
        CaptureCompleted?.Invoke(sel, null);
    }

    /// <summary>10차 cross-monitor: View → MainViewModel broadcast 트리거.
    /// 11차: GrabPointAbsolute 도 함께 propagate 해 형제 ViewModel 이 selectionRect 를 그리도록.</summary>
    public void RaiseCrossMonitorMove(Point absolutePosition, bool leftButtonDown)
    {
        CrossMonitorMove?.Invoke(GrabPointAbsolute, absolutePosition, leftButtonDown);
    }

    /// <summary>10차 cross-monitor: View → MainViewModel broadcast 트리거</summary>
    public void RaiseCrossMonitorUp(Point absolutePosition)
    {
        CrossMonitorUp?.Invoke(absolutePosition);
    }

    public void OnMouseUp(Point position)
    {
        _cursorPoint = position;
        _isGrab = false;
        IsDragging = false;  // 10차: 단일 모니터 경로 종료 시 cross-monitor 상태 클리어

        int x = Math.Min(_grabPoint.X, _cursorPoint.X);
        int y = Math.Min(_grabPoint.Y, _cursorPoint.Y);
        int w = Math.Abs(_grabPoint.X - _cursorPoint.X);
        int h = Math.Abs(_grabPoint.Y - _cursorPoint.Y);

        if (_captureMode.CurrentMode == AppCaptureMode.Window)
        {
            x = _lastWindowRect.Left;
            y = _lastWindowRect.Top;
            w = _lastWindowRect.Width;
            h = _lastWindowRect.Height;
        }
        else if (_captureMode.CurrentMode == AppCaptureMode.LastRegion)
        {
            x = _captureMode.LastCapturedRegion.Left - ScreenBounds.Left;
            y = _captureMode.LastCapturedRegion.Top - ScreenBounds.Top;
            w = _captureMode.LastCapturedRegion.Width;
            h = _captureMode.LastCapturedRegion.Height;
        }

        if (_captureMode.CurrentMode == AppCaptureMode.ColorPick)
        {
            CaptureCompleted?.Invoke(default, null);
            return;
        }

        if (w * h < MINIMUM_CAPTURE_SIZE)
        {
            CaptureCompleted?.Invoke(default, ScreenBitmap);
            return;
        }

        var rect = new Rectangle(x, y, w, h);

        if (_captureMode.CurrentMode != AppCaptureMode.LastRegion
            || rect.Contains(new System.Drawing.Point(position.X, position.Y)))
        {
            if (ScreenBitmap != null)
            {
                rect.Intersect(new Rectangle(0, 0, ScreenBitmap.Width, ScreenBitmap.Height));
                var cropped = ScreenBitmap.Clone(rect, ScreenBitmap.PixelFormat);
                CaptureCompleted?.Invoke(rect, cropped);
                return;
            }
        }

        // 위 분기에 모두 실패해도 (LastRegion 모드 + 클릭이 영역 밖, 또는 ScreenBitmap null)
        // 반드시 CaptureCompleted 또는 CaptureCancel 을 발사해 윈도우를 닫는다.
        // 안 그러면 RequestCaptureWindow 가 그대로 떠 있어 사용자가 두 번째 드래그를 할 수 있게 된다.
        CaptureCancel?.Invoke();
    }

    public void OnMouseLeave()
    {
        // 10차 cross-monitor: 드래그 중에는 모니터 경계를 벗어나도 상태 유지.
        // SetCapture 가 잡혀있어 다른 모니터로 가도 MouseMove 가 계속 들어옴.
        if (IsDragging) return;
        _isGrab = false;
        InfoVisible = false;
    }

    public void OnKeyDown(WpfKey key)
    {
        switch (key)
        {
            case WpfKey.Escape:
                IsDragging = false;  // 10차: ESC 시 cross-monitor 상태 클리어
                CaptureCancel?.Invoke();
                break;
            case WpfKey.Up:
                User32.GetCursorPos(out var ptUp);
                User32.SetCursorPos(ptUp.X, ptUp.Y - 1);
                break;
            case WpfKey.Down:
                User32.GetCursorPos(out var ptDown);
                User32.SetCursorPos(ptDown.X, ptDown.Y + 1);
                break;
            case WpfKey.Left:
                User32.GetCursorPos(out var ptLeft);
                User32.SetCursorPos(ptLeft.X - 1, ptLeft.Y);
                break;
            case WpfKey.Right:
                User32.GetCursorPos(out var ptRight);
                User32.SetCursorPos(ptRight.X + 1, ptRight.Y);
                break;
        }
    }

    public void CaptureMethodChanged()
    {
        _isGrab = false;
        IsDragging = false;  // 10차: 모드 전환 시 cross-monitor 드래그 취소
        UpdateModeText();
        // 9차: 모드 전환 시 View 에 알림 (Window/LastRegion rect 오버레이 갱신)
        ModeChanged?.Invoke();
    }

    private void UpdateModeText()
    {
        ModeText = EnumEx.Description(_captureMode.CurrentMode);
    }

    private void UpdateIndicator()
    {
        if (ScreenBitmap == null) return;
        int px = Math.Clamp(_cursorPoint.X, 0, ScreenBitmap.Width - 1);
        int py = Math.Clamp(_cursorPoint.Y, 0, ScreenBitmap.Height - 1);
        PickedColor = ScreenBitmap.GetPixel(px, py);

        byte r = PickedColor.R;
        byte g = PickedColor.G;
        byte b = PickedColor.B;
        int w = Math.Abs(_grabPoint.X - _cursorPoint.X) + 1;
        int h = Math.Abs(_grabPoint.Y - _cursorPoint.Y) + 1;

        if (_isGrab)
            IndicatorText = $"[ {w:0,0} x {h:0,0} ] [ #{r:X2}{g:X2}{b:X2} ] [ {r} {g} {b} ] {ColorName.GetColorName(PickedColor)}";
        else
            IndicatorText = $"[ 0 x 0 ] [ #{r:X2}{g:X2}{b:X2} ] [ {r} {g} {b} ] {ColorName.GetColorName(PickedColor)}";
    }

    private void UpdateMagnifier()
    {
        if (ScreenBitmap == null) return;
        // preserve: 35x35 확대경 (NN 보간)
        int half = SIZE_MAGNIFIER / 2;
        var magBmp = new System.Drawing.Bitmap(SIZE_MAGNIFIER * 4, SIZE_MAGNIFIER * 4);
        using var g = Graphics.FromImage(magBmp);
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        var srcRect = new Rectangle(
            Math.Max(0, _cursorPoint.X - half),
            Math.Max(0, _cursorPoint.Y - half),
            SIZE_MAGNIFIER, SIZE_MAGNIFIER);
        srcRect.Intersect(new Rectangle(0, 0, ScreenBitmap.Width, ScreenBitmap.Height));
        g.DrawImage(ScreenBitmap, new Rectangle(0, 0, magBmp.Width, magBmp.Height), srcRect, GraphicsUnit.Pixel);

        MagnifierSource = BitmapInterop.ToBitmapSource(magBmp);
        magBmp.Dispose();
    }

    public void Dispose()
    {
        // natural-fix: 종료자 대신 명시 Dispose — Window.Closed 이벤트에서 호출
        ScreenBitmap?.Dispose();
        ScreenBitmap = null;
        MagnifierBitmap?.Dispose();
        MagnifierBitmap = null;
        GC.SuppressFinalize(this);
    }
}
