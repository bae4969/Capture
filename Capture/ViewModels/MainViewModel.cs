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
// frame-based: 녹화 UX 를 "영역 선택 → 프레임+컨트롤 바 대기 → 시작/일시정지/중단" 상태머신으로 재설계.
//   임시경로로 즉시 시작 → 중단 완료 시 SaveFileDialog 로 최종 위치 받아 Move(취소 시 임시파일 삭제).
//   종료 버그 fix: ExitApp 에서 _shuttingDown + StopAndWait(5s), 완료 콜백 UI 마샬링 스킵 → 데드락 회피.

using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows.Interop;
using System.Windows.Threading;
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
    private readonly IScreenRecordService _screenRecord;
    private readonly ISettingsService _settings;
    private readonly ITrayHost _trayHost;

    private readonly List<RequestCaptureWindow> _requestCaptureWindows = new();

    // 영역 오버레이를 캡쳐와 공유하므로, 완료 콜백에서 "캡쳐"와 "녹화 대기 진입"을 구분하는 플래그.
    private bool _recordingRequested;

    // 녹화 중 표시되는 영역 경계 오버레이 (녹화 아닐 땐 null).
    private RecordingBorderWindow? _recordingBorder;
    // 프레임 기반 녹화 컨트롤 바 창 (녹화 대기~중 표시, 그 외 null).
    private RecordingControlWindow? _recordingControls;

    // 영역 선택 완료 후 "대기" 상태로 보관하는 녹화 대상 영역 — PrimaryActionCommand(Idle)가 이걸로 시작.
    private Rectangle? _pendingRecordRegion;
    // 임시경로로 녹화 시작 후 보관 — 중단 완료 시 SaveFileDialog 로 받은 최종 위치로 Move 한다.
    private string? _tempRecordPath;
    // 앱 종료 진행 중 플래그 — 완료 콜백의 UI 마샬링을 스킵해 StopAndWait 대기와의 데드락을 막는다.
    private bool _shuttingDown;

    // 드래그 시작 시점의 영역 스냅샷 — 총 이동(total delta)을 여기 더해 절대 위치로 배치(시작 점프 방지).
    private Rectangle _dragStartRegion;
    // 녹화 중(Recording) 활성 영역 — Start 시 저장, 녹화 중 드래그 시 UpdateRegion 크롭 이동의 기준.
    private Rectangle _activeRecordRegion;

    // 경과시간 표시용 — 컨트롤 바가 바인딩하는 상태를 상태머신과 한곳에서 관리(Pause 시 타이머 정지).
    private readonly Stopwatch _elapsed = new();
    private DispatcherTimer? _elapsedTimer;

    // ── 컨트롤 바 바인딩 상태 (RecordingControlWindow.xaml) ──────────────────────

    // 녹화 상태를 테두리(region) 색으로 표시 — 대기=흰/녹화=빨강/일시정지=주황/마감=회색.
    // 상태 텍스트를 대체(결정 D1). 정규화 타입명 사용 — System.Drawing 도 import 되어 Brush 이름이
    // 충돌하므로 System.Windows.Media using 을 추가하지 않고 풀네임으로 쓴다.
    [ObservableProperty]
    private System.Windows.Media.Brush _recordingBorderBrush = System.Windows.Media.Brushes.White;

    // 컨트롤 바 접힘/펼침 — 녹화 시작 시 자동 접힘으로 영상 가림을 최소화(결정 C). 접힘 땐 색 점만 노출.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlsExpanded))]
    private bool _controlsCollapsed;

    // 펼침 패널 Visibility 바인딩용 반전값 — BoolToVisibilityConverter 재사용(반전 컨버터 신설 회피).
    public bool ControlsExpanded => !ControlsCollapsed;

    [ObservableProperty]
    private string _elapsedText = "00:00";

    // 컨트롤 바 2버튼 아이콘 — 상태에 따라 재생/일시정지, 정지/취소로 전환.
    //  Primary  : Idle=▶(시작) · Recording=⏸(일시정지) · Paused=▶(재개).
    //  Secondary: Idle=✕(취소) · Recording/Paused=■(중단). Finishing 에선 둘 다 비활성.
    [ObservableProperty]
    private string _primaryIcon = "▶";

    [ObservableProperty]
    private string _secondaryIcon = "✕";

    [ObservableProperty]
    private bool _canPrimary = true;

    [ObservableProperty]
    private bool _canSecondary = true;

    public MainViewModel(
        ICaptureModeService captureMode,
        IScreenCaptureService screenCapture,
        IWindowEnumService windowEnum,
        IClipboardService clipboard,
        IFileSaveService fileSave,
        IScreenRecordService screenRecord,
        ISettingsService settings,
        ITrayHost trayHost)
    {
        _captureMode = captureMode;
        _screenCapture = screenCapture;
        _windowEnum = windowEnum;
        _clipboard = clipboard;
        _fileSave = fileSave;
        _screenRecord = screenRecord;
        _settings = settings;
        _trayHost = trayHost;

        // ScreenRecorderLib 의 완료/실패/상태 이벤트는 인코더 백그라운드 스레드에서 올 수 있어
        // UI 바인딩 갱신을 UI 스레드로 마샬링한다(OnRecordStateChanged/완료 콜백 내부에서).
        _screenRecord.RecordingCompleted += OnRecordingCompleted;
        _screenRecord.RecordingFailed += OnRecordingFailed;
        _screenRecord.StateChanged += OnRecordStateChanged;

        // 초기화: 첫 토글 (None → Region)
        _captureMode.ToggleMode();
    }

    // ── 트레이 더블클릭 / Capture 메뉴 ──────────────────────────────────────

    [RelayCommand]
    private async Task CaptureAsync()
    {
        await RequestCaptureAsync();
    }

    // ── 트레이 녹화 진입 (결정 D: 트레이는 "대기 진입"만, 시작/일시정지/중단은 컨트롤 바) ──────

    [RelayCommand]
    private async Task ToggleRecordingAsync()
    {
        // 이미 대기~녹화 중이면(컨트롤 바가 떠 있으면) 트레이 재진입은 무시 — 컨트롤 바로 조작.
        if (_recordingControls != null || _screenRecord.IsRecording || _pendingRecordRegion != null)
            return;

        // 녹화는 Region 오버레이를 재사용하므로 모드를 Region 으로 보장(복원 안 함 — 사용자 결정).
        // SetMode 는 CurrentMode 만 바꿔 Tab 순환/LastRegion 상태를 건드리지 않아 가장 외과적.
        _captureMode.SetMode(CaptureMode.Region);
        _recordingRequested = true;
        await RequestCaptureAsync();
    }

    // ── 프레임 기반 녹화 커맨드 (컨트롤 바 버튼) ───────────────────────────────────

    // 주 버튼(▶/⏸): Idle→시작, Recording→일시정지, Paused→재개. 아이콘·활성은 OnRecordStateChanged 가 반영.
    [RelayCommand]
    private void PrimaryAction()
    {
        switch (_screenRecord.State)
        {
            case RecordingState.Idle:
                // 대기(_pendingRecordRegion) 상태에서만 시작. SaveFileDialog 없이 임시경로로 즉시 시작하고,
                // 최종 저장 위치는 중단 완료(OnRecordingCompleted) 시점에 받는다(결정 B — 중단 후 저장).
                if (_pendingRecordRegion is not Rectangle region) return;
                _tempRecordPath = System.IO.Path.Combine(
                    System.IO.Path.GetTempPath(),
                    $"Capture_{DateTime.Now:yyyyMMddHHmmss}.mp4");
                _screenRecord.Start(region, _tempRecordPath);
                _activeRecordRegion = region;   // 녹화 중 드래그 이동의 기준
                _pendingRecordRegion = null;
                break;
            case RecordingState.Recording:
                _screenRecord.Pause();
                break;
            case RecordingState.Paused:
                _screenRecord.Resume();
                break;
        }
    }

    // 보조 버튼(■/✕): Idle→대기 취소, 녹화 중/일시정지→중단(정지 신호, 저장은 완료 콜백에서).
    [RelayCommand]
    private void SecondaryAction()
    {
        if (_screenRecord.State == RecordingState.Idle)
            // 대기 취소 — 오버레이 ESC 취소와 동일 정리 재사용(녹화 미시작이라 임시파일도 없음).
            OnCaptureCancel();
        else
            _screenRecord.Stop();
    }

    // 접기/펼치기 토글 — 접힘 시 색 점만, 펼침 시 버튼+경과시간. 녹화 중 사용자가 펼쳐 일시정지/중단.
    [RelayCommand]
    private void ToggleControlsCollapsed() => ControlsCollapsed = !ControlsCollapsed;

    // 드래그 시작 — 현재 영역을 스냅샷으로 잡는다. 이후 OnRegionDraggedTo 의 '총' 이동을 여기 더한다.
    private void OnRegionDragStart()
    {
        _dragStartRegion = _screenRecord.State == RecordingState.Recording
            ? _activeRecordRegion
            : (_pendingRecordRegion ?? Rectangle.Empty);
    }

    // 드래그 시작 대비 '총' 이동으로 영역을 절대 배치. 대기(Idle)면 예약 region, 녹화 중(Recording)이면
    // UpdateRegion 으로 크롭을 실시간 이동(같은 크기라 왜곡 없음). Paused/Finishing 은 무시.
    // 시작 스냅샷 기준 절대 위치라 증분 누적/시작 점프가 없다. 크기 보존, virtual screen 밖 clamp.
    private void OnRegionDraggedTo(int totalDx, int totalDy)
    {
        var state = _screenRecord.State;
        if (state != RecordingState.Idle && state != RecordingState.Recording) return;
        if (_dragStartRegion.Width <= 0 || _dragStartRegion.Height <= 0) return;

        var monitors = DpiHelper.EnumMonitors();
        int vLeft = monitors.Min(m => m.PhysicalBounds.Left);
        int vTop = monitors.Min(m => m.PhysicalBounds.Top);
        int vRight = monitors.Max(m => m.PhysicalBounds.Right);
        int vBottom = monitors.Max(m => m.PhysicalBounds.Bottom);

        var r = _dragStartRegion;
        int newX = Math.Max(vLeft, Math.Min(r.X + totalDx, vRight - r.Width));
        int newY = Math.Max(vTop, Math.Min(r.Y + totalDy, vBottom - r.Height));
        var moved = new Rectangle(newX, newY, r.Width, r.Height);

        if (state == RecordingState.Recording)
        {
            _activeRecordRegion = moved;
            _screenRecord.UpdateRegion(moved);   // 녹화 크롭 실시간 이동(같은 크기 → 왜곡 없음)
        }
        else
        {
            _pendingRecordRegion = moved;
        }
        _recordingBorder?.PlaceForRegion(moved);
        _recordingControls?.PlaceForRegion(moved);
    }

    // 녹화 상태(State) 변화 → 컨트롤 바 버튼/테두리 색/경과 타이머 반영.
    // 인코더 백그라운드 스레드에서 올 수 있어 UI 마샬링. 종료 중이면 UI 를 만지지 않는다(데드락 회피).
    private void OnRecordStateChanged(RecordingState state)
    {
        if (_shuttingDown) return;
        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            switch (state)
            {
                case RecordingState.Recording:
                    PrimaryIcon = "⏸";      // 일시정지
                    SecondaryIcon = "■";     // 중단
                    CanPrimary = true;
                    CanSecondary = true;
                    RecordingBorderBrush = System.Windows.Media.Brushes.Red;    // 녹화(재개 포함)=빨강
                    ControlsCollapsed = true;   // 녹화 시작 시 자동 접힘 — 영상 가림 최소화(펼치기 버튼+색 점만).
                    // 재개 포함 — Stopwatch 는 Start 가 멱등이 아니므로 Paused→Recording 시 이어서 잰다.
                    if (!_elapsed.IsRunning) _elapsed.Start();
                    _elapsedTimer?.Start();
                    break;
                case RecordingState.Paused:
                    PrimaryIcon = "▶";      // 재개
                    SecondaryIcon = "■";     // 중단
                    CanPrimary = true;
                    CanSecondary = true;
                    RecordingBorderBrush = System.Windows.Media.Brushes.Orange;  // 일시정지=주황
                    // Pause 시 경과시간 정지.
                    _elapsed.Stop();
                    _elapsedTimer?.Stop();
                    break;
                case RecordingState.Finishing:
                    CanPrimary = false;
                    CanSecondary = false;
                    RecordingBorderBrush = System.Windows.Media.Brushes.Gray;    // 마감=회색
                    _elapsed.Stop();
                    _elapsedTimer?.Stop();
                    break;
                case RecordingState.Idle:
                    // 완료/실패 콜백에서 컨트롤 바를 닫으므로 여기선 버튼만 비활성.
                    CanPrimary = false;
                    CanSecondary = false;
                    _elapsed.Stop();
                    _elapsedTimer?.Stop();
                    break;
            }
        });
    }

    private void OnRecordingCompleted(string tempPath)
    {
        // 종료 진행 중: SaveFileDialog 를 띄울 수 없으므로(UI 스킵) AutoSavePath 에 자동 저장해 데이터 손실 방지.
        // 이 경로는 UI 를 만지지 않아 StopAndWait 의 백그라운드 대기와 충돌하지 않는다(데드락 무관).
        if (_shuttingDown)
        {
            AutoSaveOnShutdown(tempPath);
            return;
        }

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            CloseRecordingBorder();
            CloseRecordingControls();
            ResetRecordingUi();

            // 중단 완료 → 최종 저장 위치를 SaveFileDialog 로 받아 임시파일을 Move.
            string? finalPath = ShowRecordSaveDialog();
            if (finalPath == null)
            {
                // 저장 취소 → 임시파일 삭제(저장 안 함).
                TryDeleteTemp(tempPath);
                _tempRecordPath = null;
                return;
            }

            try
            {
                System.IO.File.Move(tempPath, finalPath, overwrite: true);
                _trayHost.ShowBalloonTip("Recording saved", finalPath);
            }
            catch (Exception ex)
            {
                WpfMessageBox.Show($"저장 실패: {ex.Message}", "오류");
                TryDeleteTemp(tempPath);
            }
            _tempRecordPath = null;
        });
    }

    private void OnRecordingFailed(string message)
    {
        // 실패 시 임시파일은 불완전하므로 정리.
        TryDeleteTemp(_tempRecordPath);
        _tempRecordPath = null;

        if (_shuttingDown) return;

        System.Windows.Application.Current?.Dispatcher.Invoke(() =>
        {
            CloseRecordingBorder();
            CloseRecordingControls();
            ResetRecordingUi();
            WpfMessageBox.Show($"Recording failed: {message}", "Error");
        });
    }

    // 종료 중 완료 콜백 — AutoSavePath 로 자동 이동해 손실 방지. 없거나 실패하면 임시 위치 그대로 둔다.
    private void AutoSaveOnShutdown(string tempPath)
    {
        try
        {
            string dir = _settings.AutoSavePath;
            if (string.IsNullOrEmpty(dir) || !System.IO.Directory.Exists(dir))
                return; // 자동 저장 위치 없음 → 임시 위치 유지(로그성, 손실은 아님)

            string dest = System.IO.Path.Combine(
                dir, System.IO.Path.GetFileName(tempPath));
            System.IO.File.Move(tempPath, dest, overwrite: true);
        }
        catch
        {
            // 종료 경로라 UI 로 알릴 수 없음 — 임시 위치에 파일이 남는다(정리 안 함).
        }
        _tempRecordPath = null;
    }

    private static void TryDeleteTemp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        try { if (System.IO.File.Exists(path)) System.IO.File.Delete(path); }
        catch { /* 임시파일 정리 실패는 무시 */ }
    }

    // 컨트롤 바 바인딩 상태를 대기(Idle) 초기값으로 되돌린다.
    private void ResetRecordingUi()
    {
        ElapsedText = "00:00";
        PrimaryIcon = "▶";       // 시작
        SecondaryIcon = "✕";      // 취소
        CanPrimary = true;
        CanSecondary = true;
        RecordingBorderBrush = System.Windows.Media.Brushes.White;   // 대기=흰(중립)
        ControlsCollapsed = false;   // 대기 상태는 펼침
        _elapsed.Reset();
        _elapsedTimer?.Stop();
    }

    // 녹화 중 영역 경계 오버레이 — 대기 진입 시 표시, 완료/실패 시 닫기(멱등).
    // 창은 SourceInitialized 에서 자기 위치·클릭통과를 물리 픽셀로 설정하므로 여기선 생성·표시만.
    private void ShowRecordingBorder(System.Drawing.Rectangle region)
    {
        CloseRecordingBorder();
        // DataContext=this 로 테두리 BorderBrush 를 RecordingBorderBrush(상태 색)에 바인딩.
        _recordingBorder = new RecordingBorderWindow(region, this);
        _recordingBorder.Show();
    }

    private void CloseRecordingBorder()
    {
        _recordingBorder?.Close();
        _recordingBorder = null;
    }

    // 프레임 기반 컨트롤 바 — 대기 진입 시 표시(버튼으로 시작/일시정지/중단), 완료/실패/취소 시 닫기(멱등).
    private void ShowRecordingControls(System.Drawing.Rectangle region)
    {
        CloseRecordingControls();
        ResetRecordingUi();

        // 경과시간 갱신 타이머 — DispatcherTimer(UI 스레드) + Stopwatch. Pause 시 OnRecordStateChanged 가 정지.
        if (_elapsedTimer == null)
        {
            _elapsedTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _elapsedTimer.Tick += (_, _) =>
            {
                var t = _elapsed.Elapsed;
                ElapsedText = t.Hours > 0
                    ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}"
                    : $"{t.Minutes:00}:{t.Seconds:00}";
            };
        }

        _recordingControls = new RecordingControlWindow(region, this);
        // 바 배경 드래그 → region 이동(대기·녹화 중 모두). 창 폐기 시 구독도 함께 사라져 별도 해제 불필요.
        _recordingControls.RegionDragStarted += OnRegionDragStart;
        _recordingControls.RegionDraggedTo += OnRegionDraggedTo;
        _recordingControls.Show();
    }

    private void CloseRecordingControls()
    {
        _recordingControls?.Close();
        _recordingControls = null;
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

        // 종료 마감: 녹화/일시정지 중이면 파일이 손상되지 않도록 완료를 대기한다.
        // _shuttingDown 을 먼저 켜 완료 콜백의 UI 마샬링(Dispatcher.Invoke)을 스킵시킨다 — UI 스레드가
        // 아래 Wait() 로 블록된 사이 완료 콜백이 UI 마샬링을 시도하면 데드락이므로.
        // StopAndWait 는 UI 를 만지지 않는 백그라운드 Task 에서 돌려 UI 스레드 블록과 분리한다.
        _shuttingDown = true;
        if (_screenRecord.IsRecording)
        {
            try { Task.Run(() => _screenRecord.StopAndWait(TimeSpan.FromSeconds(5))).Wait(); }
            catch { /* 마감 실패해도 종료는 진행 */ }
        }

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

        // 녹화 분기 — 오버레이가 녹화 요청으로 떠 있었으면 캡쳐 대신 "녹화 대기"로 진입한다.
        // 곧바로 시작하지 않고 프레임(테두리)+컨트롤 바를 대기 상태로 띄운다. 시작/일시정지/중단은
        // 컨트롤 바 버튼(PrimaryAction/SecondaryAction)이 담당(프레임 기반 UX).
        if (_recordingRequested)
        {
            _recordingRequested = false;

            // rect 를 virtual-screen 절대 물리 픽셀로 확정 — 캡쳐와 동일 계약.
            // cross-monitor 경로(bmpCropped==null)면 rectCropped 가 이미 절대좌표,
            // 단일 모니터 로컬좌표 경로면 sender 의 ScreenBounds 오프셋을 더한다.
            if (rectCropped.Width <= 0 || rectCropped.Height <= 0) return;
            var absRect = bmpCropped == null
                ? rectCropped
                : new Rectangle(
                    rectCropped.Left + senderVm.ScreenBounds.Left,
                    rectCropped.Top + senderVm.ScreenBounds.Top,
                    rectCropped.Width,
                    rectCropped.Height);

            _pendingRecordRegion = absRect;
            ShowRecordingBorder(absRect);
            ShowRecordingControls(absRect);
            return;
        }

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
                // 12차 dpi-fix: BorderPad 는 WPF DIP 단위 (BorderThickness=2). SetWindowPos
                // 는 물리 픽셀이라 그대로 빼면 mixed-DPI 환경(cross-monitor)에서 1픽셀 밀림.
                // 캡쳐 절대좌표 위치의 모니터 DPI 로 환산해 X/Y 별도 보정.
                const double BorderDip = 2.0;
                int absLeft = isAbsolute
                    ? rectCropped.Left
                    : rectCropped.Left + senderVm.ScreenBounds.Left;
                int absTop = isAbsolute
                    ? rectCropped.Top
                    : rectCropped.Top + senderVm.ScreenBounds.Top;
                var (padScaleX, padScaleY) = DpiHelper.GetScaleForPoint(absLeft, absTop);
                int padX = (int)Math.Round(BorderDip * padScaleX);
                int padY = (int)Math.Round(BorderDip * padScaleY);
                var capturedBounds = new Rectangle(
                    absLeft - padX,
                    absTop - padY,
                    bitmap.Width + 2 * padX,
                    bitmap.Height + 2 * padY);

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
                // 캡처 직후 키보드 포커스 부여 (ESC 닫기 / Ctrl 드로잉 즉시 사용 가능).
                // XAML ShowActivated=False 는 기존 캡처 창 보호용 — 그 의도와 양립.
                win.Activate();
            }
        }

        ReleaseRequestCaptureWindows();
    }

    private void OnCaptureCancel()
    {
        // 영역 선택을 ESC 로 취소하면 녹화 플래그도 꺼야 다음 캡쳐가 녹화로 오인되지 않는다.
        // 대기 상태 잔재(프레임/컨트롤 바/보류 영역)도 멱등 정리 — 녹화는 시작되지 않은 상태.
        _recordingRequested = false;
        _pendingRecordRegion = null;
        CloseRecordingBorder();
        CloseRecordingControls();
        ResetRecordingUi();
        ReleaseRequestCaptureWindows();
    }

    private string? ShowRecordSaveDialog()
    {
        // owner/Activate 처리는 ShowLoadFromFileDialog 와 동일 이유 — 트레이 메뉴 직후
        // foreground 경합으로 다이얼로그가 뒤에 깔리는 것을 막는다.
        var owner = System.Windows.Application.Current?.MainWindow;
        owner?.Activate();

        string initialDir = _settings.AutoSavePath;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "MP4 video (*.mp4)|*.mp4",
            Title = "Save recording",
            FileName = DateTime.Now.ToString("yyyyMMddHHmmss") + ".mp4",
            InitialDirectory = System.IO.Directory.Exists(initialDir) ? initialDir : null,
        };
        bool? result = owner != null ? dlg.ShowDialog(owner) : dlg.ShowDialog();
        return result == true ? dlg.FileName : null;
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
