// author: eng-fe-desktop
// phase: engineering
// preserve: 트레이 메뉴 커맨드, CaptureCommand (async), 모드 전환 — migration-mapping.md §12-1
// natural-fix: Thread.Sleep(100) → await Task.Delay(100)
// drop: mGlobalMouseHook (GlobalMouseHook — migration-mapping.md §4-4)
// drop: tmrTick (빈 Timer — migration-mapping.md §4-4)
// drop: IEmailService 제거 (사용자 결정 2026-04-25, ADR-006)
// 3차 DPI fix: RequestCaptureAsync·OnCaptureCompleted 에서 DpiHelper 변환 추가 (ADR-106)
// 4차 mixed-DPI fix: Screen.AllScreens → DpiHelper.EnumMonitors, SourceInitialized + SetWindowPosPhysical
//   WPF Window.Left/Top/Width/Height 직접 할당 전부 제거 — SetWindowPosPhysical 로 통일 (ADR-106)
// 8차: _emailService 필드·ctor 인자 제거, CreateCapturedViewModel 시그니처 변경 (ADR-006)

using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using Capture.Imaging;
using Capture.Interop;
using Capture.Models;
using Capture.Services;
using Capture.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfMessageBox = System.Windows.MessageBox;

namespace Capture.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ICaptureModeService _captureMode;
    private readonly IScreenCaptureService _screenCapture;
    private readonly IWindowEnumService _windowEnum;
    private readonly IClipboardService _clipboard;
    private readonly IFileSaveService _fileSave;
    private readonly ISettingsService _settings;
    private readonly ITrayHost _trayHost;

    private readonly List<RequestCaptureWindow> _requestCaptureWindows = new();

    public MainViewModel(
        ICaptureModeService captureMode,
        IScreenCaptureService screenCapture,
        IWindowEnumService windowEnum,
        IClipboardService clipboard,
        IFileSaveService fileSave,
        ISettingsService settings,
        ITrayHost trayHost)
    {
        _captureMode = captureMode;
        _screenCapture = screenCapture;
        _windowEnum = windowEnum;
        _clipboard = clipboard;
        _fileSave = fileSave;
        _settings = settings;
        _trayHost = trayHost;

        // 초기화: 첫 토글 (None → Region)
        _captureMode.ToggleMode();
    }

    // ── 트레이 더블클릭 / Capture 메뉴 ──────────────────────────────────────

    [RelayCommand]
    private async Task CaptureAsync()
    {
        await RequestCaptureAsync();
    }

    [RelayCommand]
    private void LoadFromFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png)|*.jpg;*.jpeg;*.jpe;*.jfif;*.png",
            Title = "Load from image file"
        };
        if (dlg.ShowDialog() == true)
        {
            try
            {
                var bmp = new System.Drawing.Bitmap(dlg.FileName);
                var capturedVm = CreateCapturedViewModel();
                capturedVm.LoadFromFile = true;
                capturedVm.SetImage(bmp);
                var win = new CapturedWindow(capturedVm);
                win.Show();
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"이미지 로드 실패: {ex.Message}", "오류");
            }
        }
    }

    [RelayCommand]
    private void ExitApp()
    {
        System.Windows.Application.Current?.Shutdown();
    }

    // ── Tab 키 — 모드 전환 ──────────────────────────────────────────────────

    public void OnTabKeyPressed()
    {
        lock (_requestCaptureWindows)
        {
            if (_requestCaptureWindows.Count == 0) return;
            _captureMode.ToggleMode();
            foreach (var win in _requestCaptureWindows)
                win.ViewModel.CaptureMethodChanged();
        }
    }

    // ── 캡처 요청 플로우 ─────────────────────────────────────────────────────

    private async Task RequestCaptureAsync()
    {
        _windowEnum.UpdateVisibleWindowList();

        // natural-fix: Thread.Sleep(100) → await Task.Delay(100)
        await Task.Delay(100);

        lock (_requestCaptureWindows)
        {
            if (_requestCaptureWindows.Count > 0) return;
            ReleaseRequestCaptureWindows();

            // 4차 mixed-DPI fix: Screen.AllScreens 제거 → DpiHelper.EnumMonitors 사용
            // EnumDisplayMonitors + GetMonitorInfo + GetDpiForMonitor 로 물리 픽셀 좌표·DPI 직접 획득
            var monitors = DpiHelper.EnumMonitors();
            for (int i = 0; i < monitors.Count; i++)
            {
                var mon = monitors[i];

                // CaptureRegion 에 물리 픽셀 좌표 직접 전달 — CaptureMonitor(Screen) 의존 제거
                var bmp = _screenCapture.CaptureRegion(mon.PhysicalBounds);

                var vm = new RequestCaptureViewModel(_captureMode, _windowEnum)
                {
                    ScreenBitmap = bmp,
                    IdxScreen = i,
                    // ScreenBounds 는 물리 픽셀 그대로 유지 — 비트맵 좌표 계산용
                    ScreenBounds = mon.PhysicalBounds,
                    // MonitorScale 을 설정해 ToDrawingPoint 가 올바른 DPI 로 변환하도록
                    MonitorScale = (mon.ScaleX, mon.ScaleY)
                };
                vm.CaptureCompleted += (rect, cropped) => OnCaptureCompleted(vm, rect, cropped);
                vm.CaptureCancel += () => OnCaptureCancel();

                // 4차 mixed-DPI fix: WPF Window.Left/Top/Width/Height 직접 할당 제거
                // SourceInitialized 직후 SetWindowPosPhysical 로 물리 픽셀 좌표 강제 설정
                // → WPF DIP 추상화 우회로 Mixed-DPI 환경에서도 정확한 창 배치 보장
                var win = new RequestCaptureWindow(vm);
                // mon 은 값 타입(record struct) 이므로 람다 캡처 안전
                win.SourceInitialized += (_, _) =>
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    DpiHelper.SetWindowPosPhysical(
                        hwnd,
                        mon.PhysicalBounds.Left,
                        mon.PhysicalBounds.Top,
                        mon.PhysicalBounds.Width,
                        mon.PhysicalBounds.Height);
                };
                win.Show();
                _requestCaptureWindows.Add(win);
            }
        }
    }

    private void OnCaptureCompleted(RequestCaptureViewModel senderVm, Rectangle rectCropped, System.Drawing.Bitmap? bmpCropped)
    {
        if (_captureMode.CurrentMode == CaptureMode.ColorPick)
        {
            var color = senderVm.PickedColor;
            string hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
            _clipboard.SetText(hex);
            WpfMessageBox.Show($"Color value [{hex}] has been copied to clipboard.");
        }
        else if (bmpCropped != null)
        {
            var bitmap = (System.Drawing.Bitmap)bmpCropped.Clone();
            _clipboard.SetImage(bitmap);

            if (rectCropped.Width > 0 && rectCropped.Height > 0)
            {
                // preserve: Bounds 1px 보정 (FormCaptured.Bounds = rectCropped 위치 - 1px)
                var capturedBounds = new Rectangle(
                    rectCropped.Left + senderVm.ScreenBounds.Left - 1,
                    rectCropped.Top + senderVm.ScreenBounds.Top - 1,
                    bitmap.Width + 2,
                    bitmap.Height + 2);

                _captureMode.LastCapturedRegion = capturedBounds;

                var capturedVm = CreateCapturedViewModel();
                capturedVm.Bounds = capturedBounds;
                capturedVm.BoundsCapture = rectCropped;
                capturedVm.IdxScreen = senderVm.IdxScreen;

                // 3차 DPI fix 유지: capturedBounds 위치의 모니터 DPI 를 얻어 SetImage 에 전달 (ADR-106)
                // capturedVm.Bounds·BoundsCapture 는 물리 픽셀 그대로 유지 — 비트맵 처리용
                var (capturedScaleX, _) = DpiHelper.GetScaleForPoint(
                    capturedBounds.Left, capturedBounds.Top);
                double capturedDpi = capturedScaleX * 96.0;
                capturedVm.SetImage(bitmap, dpi: capturedDpi);

                // 4차 mixed-DPI fix: WPF Window.Left/Top 직접 할당 제거
                // SourceInitialized 직후 SetWindowPosPhysical 로 위치 강제 설정
                // width=0·height=0 → SWP_NOSIZE 자동 적용으로 위치만 변경
                // (SizeToContent="WidthAndHeight" 라 WPF 가 크기는 자동 계산)
                var win = new CapturedWindow(capturedVm);
                int capturedLeft = capturedBounds.Left;
                int capturedTop = capturedBounds.Top;
                win.SourceInitialized += (_, _) =>
                {
                    var hwnd = new WindowInteropHelper(win).Handle;
                    DpiHelper.SetWindowPosPhysical(hwnd, capturedLeft, capturedTop, 0, 0);
                };
                win.Show();
            }
        }

        ReleaseRequestCaptureWindows();
    }

    private void OnCaptureCancel()
    {
        ReleaseRequestCaptureWindows();
    }

    private void ReleaseRequestCaptureWindows()
    {
        lock (_requestCaptureWindows)
        {
            foreach (var win in _requestCaptureWindows)
            {
                win.ViewModel.Dispose();
                win.Close();
            }
            _requestCaptureWindows.Clear();
        }
    }

    private CapturedViewModel CreateCapturedViewModel()
    {
        return new CapturedViewModel(_fileSave, _settings, _clipboard);
    }
}
