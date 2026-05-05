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
// 10차 cross-monitor: BroadcastCrossMonitorMove/Up + OnCaptureCompleted 절대좌표 분기 +
//   OnTabKeyPressed 에 ReleaseCapture — 모니터 경계 가로지르는 드래그 지원
// 11차 cross-monitor v2: BroadcastCrossMonitorMove 시그니처 확장 (grabAbsolute propagate)
//   — OnCrossMonitorBroadcast 호출로 변경, 형제 ViewModel selectionRect 미생성 버그 fix

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
        // 트레이 ContextMenu 가 닫히는 도중에 ShowDialog 를 호출하면 포커스 경쟁으로
        // 파일 선택창이 떴다가 즉시 닫힌다. ContextLost(Background) 우선순위로 한 틱 미뤄
        // 메뉴 닫힘이 완전히 끝난 뒤 다이얼로그를 띄운다.
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(
            new Action(ShowLoadFromFileDialog),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private void ShowLoadFromFileDialog()
    {
        // owner 미전달 + 트레이 메뉴 클릭 직후 H.NotifyIcon 의 SetForegroundWindow(이전 앱) 와
        // 경합 → 다이얼로그가 다른 프로세스 뒤에 깔려 사용자는 깜빡임만 보고 끝.
        // 해결: 숨겨진 MainWindow 를 owner 로 명시 (HWND 만 있으면 됨, 시각적 안 뜸) +
        // 직전에 Activate 로 우리 프로세스에 foreground 권한 회복.
        var owner = System.Windows.Application.Current?.MainWindow;
        owner?.Activate();

        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.jpe, *.jfif, *.png)|*.jpg;*.jpeg;*.jpe;*.jfif;*.png",
            Title = "Load from image file"
        };
        bool? result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        if (result == true)
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
        // ShutdownMode=OnExplicitShutdown 이라 모든 창을 명시 Close 한 뒤 Shutdown 호출.
        // CapturedWindow / RequestCaptureWindow 가 떠 있으면 Shutdown 만으로는 정리 누락 가능.
        var app = System.Windows.Application.Current;
        if (app == null) return;

        ReleaseRequestCaptureWindows();

        // ToList() 로 스냅샷 — Close 가 컬렉션을 변경할 수 있음
        foreach (var win in app.Windows.Cast<System.Windows.Window>().ToList())
        {
            try { win.Close(); } catch { /* 이미 닫혔을 수 있음 */ }
        }

        app.Shutdown();
    }

    // ── Tab 키 — 모드 전환 ──────────────────────────────────────────────────

    public void OnTabKeyPressed()
    {
        lock (_requestCaptureWindows)
        {
            if (_requestCaptureWindows.Count == 0) return;
            // 10차 cross-monitor: 모드 전환 시 SetCapture 누수 방지 — 명시적 해제
            User32.ReleaseCapture();
            _captureMode.ToggleMode();
            foreach (var win in _requestCaptureWindows)
                win.ViewModel.CaptureMethodChanged();
        }
    }

    // ── 10차 cross-monitor: broadcast 허브 ───────────────────────────────────
    /// <summary>
    /// 드래그 시작 윈도우의 ViewModel 이 발화한 cross-monitor 마우스 이동 시그널을
    /// 모든 형제 RequestCaptureWindow ViewModel 에 push 한다 (sender 자신 포함 — 일관 경로).
    /// 11차: 시그니처에 grabAbsolute 추가 + OnCrossMonitorBroadcast 호출. 형제도
    /// GrabPointAbsolute/IsDragging 이 동기화되어 selectionRect 가 자기 ScreenBounds
    /// 클리핑으로 표시됨.
    /// 재진입 안전성: SelectionUpdated 이벤트가 동기 호출이고 View 핸들러
    /// (OnCrossMonitorSelectionUpdated → EnsureSelectionRect + UpdateSelectionRectAbsolute) 는
    /// broadcast 를 다시 트리거하지 않으므로 lock 재진입 없음.
    /// </summary>
    private void BroadcastCrossMonitorMove(RequestCaptureViewModel sender, Point grabAbsolute, Point cursorAbsolute, bool leftButtonDown)
    {
        lock (_requestCaptureWindows)
        {
            foreach (var win in _requestCaptureWindows)
                win.ViewModel.OnCrossMonitorBroadcast(grabAbsolute, cursorAbsolute, leftButtonDown);
        }
    }

    /// <summary>
    /// 드래그 시작 윈도우의 ViewModel 이 발화한 cross-monitor MouseUp 시그널을 처리.
    /// sender 의 OnMouseUpAbsolute → CaptureCompleted 이벤트 → OnCaptureCompleted 진입.
    /// </summary>
    private void BroadcastCrossMonitorUp(RequestCaptureViewModel sender, Point absolutePixel)
    {
        sender.OnMouseUpAbsolute(absolutePixel);
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
                // 10차 cross-monitor: View → ViewModel(자신) → MainViewModel(허브) → 모든 형제 ViewModel
                // 11차: CrossMonitorMove 시그니처 확장 (grabAbsolute, cursorAbsolute, leftDown)
                vm.CrossMonitorMove += (grab, cur, ld) => BroadcastCrossMonitorMove(vm, grab, cur, ld);
                vm.CrossMonitorUp += abs => BroadcastCrossMonitorUp(vm, abs);

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
        // 캡쳐 오버레이 창은 후속 처리(ShowDialog/Show 의 순간 메시지 펌프)에서
        // 큐에 쌓인 mouse-down/up 이 또 캡쳐를 트리거하지 않도록 가장 먼저 닫는다.
        // ReleaseRequestCaptureWindows 가 멱등이라 끝에서도 한 번 더 호출 가능 (안전).
        ReleaseRequestCaptureWindows();

        // 10차 cross-monitor: bmpCropped 가 null 이고 rectCropped 가 유효하면
        // rectCropped 는 이미 virtual screen 절대좌표. ScreenCaptureService 로 합성 캡쳐.
        // (드래그가 모니터 경계를 가로지르는 경로 — RequestCaptureViewModel.OnMouseUpAbsolute 발사)
        bool isAbsolute = bmpCropped == null
            && rectCropped.Width > 0 && rectCropped.Height > 0
            && _captureMode.CurrentMode == CaptureMode.Region;
        if (isAbsolute)
        {
            bmpCropped = _screenCapture.CaptureRegion(rectCropped);
        }

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
                // CapturedWindow 의 외곽 Border 두께만큼 윈도우를 확장·시프트해야
                // 이미지 콘텐츠 픽셀이 캡쳐한 원본 화면 좌표에 정확히 겹친다.
                // CapturedWindow.xaml 의 wrapping Border BorderThickness 와 반드시 일치.
                const int BorderPad = 2;
                // 10차 cross-monitor: isAbsolute 면 rectCropped 가 이미 절대좌표 → ScreenBounds 더하지 않음
                var capturedBounds = isAbsolute
                    ? new Rectangle(
                        rectCropped.Left - BorderPad,
                        rectCropped.Top - BorderPad,
                        bitmap.Width + 2 * BorderPad,
                        bitmap.Height + 2 * BorderPad)
                    : new Rectangle(
                        rectCropped.Left + senderVm.ScreenBounds.Left - BorderPad,
                        rectCropped.Top + senderVm.ScreenBounds.Top - BorderPad,
                        bitmap.Width + 2 * BorderPad,
                        bitmap.Height + 2 * BorderPad);

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
