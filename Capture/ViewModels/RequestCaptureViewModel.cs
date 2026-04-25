// author: eng-fe-desktop
// phase: engineering
// preserve: 마우스 캡처 로직·색상 피커·모드 표시 — migration-mapping.md §14-1
// natural-fix: ~FormRequestCapture() 종료자 제거 → Window.Closed 명시 Dispose
// 4차 mixed-DPI fix: MonitorScale 프로퍼티 추가 — ToDrawingPoint 가 ViewModel.MonitorScale 사용
// 9차: CurrentMode·LastCapturedRegion·LastWindowRect 프로퍼티 노출. ModeChanged 이벤트 추가.

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
    [ObservableProperty] private bool _infoVisible = true;
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

    public RequestCaptureViewModel(
        ICaptureModeService captureMode,
        IWindowEnumService windowEnum)
    {
        _captureMode = captureMode;
        _windowEnum = windowEnum;
        UpdateModeText();
    }

    public void OnMouseDown(Point position)
    {
        _isGrab = true;
        _grabPoint = position;
        _cursorPoint = position;

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

    public void OnMouseUp(Point position)
    {
        _cursorPoint = position;
        _isGrab = false;

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
            }
        }
    }

    public void OnMouseLeave()
    {
        _isGrab = false;
        InfoVisible = false;
    }

    public void OnKeyDown(WpfKey key)
    {
        switch (key)
        {
            case WpfKey.Escape:
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
