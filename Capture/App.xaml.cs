// author: eng-fe-desktop
// phase: engineering
// ADR-101: DI container (Microsoft.Extensions.DependencyInjection)
// ADR-102: H.NotifyIcon.Wpf — MainWindow XAML 에서 TaskbarIcon 선언
// ADR-103: HotKeyService.Start() on startup, Stop() on exit
// ADR-110: LegacySettingsImporter.TryMigrate() on startup
// ShutdownMode=OnExplicitShutdown (트레이 앱)
// UseWindowsForms=true 로 인해 Application, StartupEventArgs, ExitEventArgs 모두 모호 → 전체 명시
// 8차: IEmailService DI 등록 제거, MainViewModel ctor 인자 변경 반영 (ADR-006)

using System.IO;
using System.Windows;
using Capture.Services;
using Capture.ViewModels;
using Capture.Views;
using Microsoft.Extensions.DependencyInjection;
// WinForms 충돌 타입 명시 억제
using Application = System.Windows.Application;
using StartupEventArgs = System.Windows.StartupEventArgs;
using ExitEventArgs = System.Windows.ExitEventArgs;

namespace Capture;

public partial class App : Application
{
    private ServiceProvider? _services;
    private MainWindow? _mainWindow;
    private IHotKeyService? _hotKey;

    // 단일 인스턴스 가드 — 두 번째 프로세스는 즉시 종료.
    // 두 인스턴스가 동시에 실행되면 PrintScreen 훅을 둘 다 잡아 캡쳐 세션이 중복 발사됨.
    // 이름에 GUID 박아 다른 앱과 충돌 회피, Local\ prefix 로 세션 격리(서로 다른 RDP/사용자 세션 고려).
    private const string SingleInstanceMutexName = "Local\\Capture.BitWiz.SingleInstance.7A8F3C2D-9E1B-4F6A-B5D8-1C3E2A0F8B49";
    private static Mutex? _singleInstanceMutex;

    // 단일 EXE 가 %TEMP%\.net\Capture\<id>\ 에 풀어둔 파일의 삭제 금지 핸들 — 프로세스 수명 동안 보유.
    // static 으로 루트 유지 — GC 되면 종료자가 핸들을 닫아 잠금이 풀린다.
    private static List<FileStream>? _extractedFileLocks;

    // 9차 추가 (ADR-301): View codebehind 에서 ISettingsService 등 서비스 접근
    public IServiceProvider Services
        => _services ?? throw new InvalidOperationException("DI not initialized.");

    protected override void OnStartup(StartupEventArgs e)
    {
        // 단일 인스턴스 가드 강화: initiallyOwned:false 로 생성 후 WaitOne(0) 으로 ownership 명시 시도.
        // createdNew 만 의존하던 이전 패턴은 (1) 첫 인스턴스가 비정상 종료로 mutex 를 abandon 한 직후
        // 두 번째 인스턴스가 실행되는 경계 케이스, (2) initiallyOwned:true 의 ownership 시도 결과가
        // createdNew 와 분리되는 .NET 동작 모서리에서 가드를 우회시킬 수 있음.
        // 또 base.OnStartup 미호출 상태에서의 Shutdown() 은 일부 환경에서 프로세스 잔존 위험 → Environment.Exit
        // 으로 즉시 종료 보장.
        _singleInstanceMutex = new Mutex(initiallyOwned: false, SingleInstanceMutexName, out bool createdNew);
        bool acquired;
        try { acquired = _singleInstanceMutex.WaitOne(TimeSpan.Zero, exitContext: false); }
        catch (AbandonedMutexException) { acquired = true; /* 이전 인스턴스 비정상 종료 — 본 인스턴스가 인수 */ }
        if (!acquired)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Environment.Exit(0);
            return;
        }

        LockExtractedFiles();

        base.OnStartup(e);

        // DI 컨테이너 구성 (ADR-101)
        var collection = new ServiceCollection();

        // Services — Singleton
        collection.AddSingleton<ICaptureModeService, CaptureModeService>();
        collection.AddSingleton<IScreenCaptureService, ScreenCaptureService>();
        collection.AddSingleton<IWindowEnumService, WindowEnumService>();
        collection.AddSingleton<IClipboardService, ClipboardService>();
        collection.AddSingleton<IFileSaveService, FileSaveService>();
        collection.AddSingleton<IScreenRecordService, ScreenRecordService>();
        collection.AddSingleton<ISettingsService, SettingsService>();
        // IEmailService 제거 — 사용자 결정 2026-04-25 (ADR-006)
        collection.AddSingleton<LegacySettingsImporter>();

        // HotKeyService — UI Dispatcher 필요
        collection.AddSingleton<IHotKeyService>(sp =>
            new HotKeyService(Dispatcher));

        _services = collection.BuildServiceProvider();

        // 설정 로드 (ADR-108)
        var settings = _services.GetRequiredService<ISettingsService>();
        settings.Load();

        // 레거시 설정 1회 마이그레이션 (ADR-110)
        var importer = _services.GetRequiredService<LegacySettingsImporter>();
        importer.TryMigrate();

        // MainViewModel 수동 생성
        var captureMode  = _services.GetRequiredService<ICaptureModeService>();
        var screenCap    = _services.GetRequiredService<IScreenCaptureService>();
        var windowEnum   = _services.GetRequiredService<IWindowEnumService>();
        var clipboard    = _services.GetRequiredService<IClipboardService>();
        var fileSave     = _services.GetRequiredService<IFileSaveService>();
        var screenRecord = _services.GetRequiredService<IScreenRecordService>();

        // NullTrayHost: TaskbarIcon 은 MainWindow XAML 이 직접 관리
        var nullTray = new NullTrayHost();
        var mainVm = new MainViewModel(captureMode, screenCap, windowEnum, clipboard, fileSave, screenRecord, settings, nullTray);

        // MainViewModel 받는 생성자로 — TrayIcon.DataContext 동시 할당
        _mainWindow = new MainWindow(mainVm);
        _mainWindow.Show();

        // HotKey 시작 (ADR-103)
        _hotKey = _services.GetRequiredService<IHotKeyService>();
        _hotKey.Start();

        // Tab 키 → MainViewModel
        _hotKey.TabPressed += (_, _) => mainVm.OnTabKeyPressed();
        _hotKey.PrintScreenPressed += async (_, _) =>
        {
            if (mainVm.CaptureCommand.CanExecute(null))
                await mainVm.CaptureCommand.ExecuteAsync(null);
        };
    }

    // 포터블 단일 EXE(IncludeAllContentForSelfExtract — C++/CLI 인 ScreenRecorderLib 때문에 필요)는 실행 시
    // 모든 DLL 을 %TEMP%\.net\Capture\<id>\ 에 풀고 필요할 때 불러온다. 아직 안 불러온 DLL 은 잠겨 있지 않아
    // 임시파일 정리(저장소 센스 등)가 지울 수 있고, 그러면 첫 사용 시 FileNotFoundException 으로 죽는다
    // (예: 앱을 켜고 오래 지난 뒤 첫 캡쳐 — Graphics.Dispose 가 System.Numerics.Vectors 를 처음 불러옴).
    // 풀린 파일을 전부 삭제 금지(FileShare.Read)로 열어 둬 실행 중에는 지워지지 않게 한다.
    // 꺼져 있는 동안 지워진 파일은 다음 실행 때 호스트가 다시 풀어 준다.
    private static void LockExtractedFiles()
    {
        // 풀린 폴더에서 실행 중일 때만 — 일반 빌드(exe 와 같은 폴더)는 건너뛴다.
        string baseDir = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        string? exeDir = Path.GetDirectoryName(Environment.ProcessPath);
        if (exeDir == null || string.Equals(baseDir, exeDir, StringComparison.OrdinalIgnoreCase))
            return;

        _extractedFileLocks = new List<FileStream>();
        try
        {
            foreach (string file in Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories))
            {
                try { _extractedFileLocks.Add(new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* 못 잠근 파일은 건너뜀 */ }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 열거 중 폴더가 사라지는 등 — 보호를 일부 못 해도 앱 시작은 막지 않는다(잠근 만큼은 유지).
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _hotKey?.Stop();
        _services?.GetService<ISettingsService>()?.Save();
        _services?.Dispose();

        if (_singleInstanceMutex != null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch { /* 다른 스레드 소유 시 무시 */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    /// <summary>TaskbarIcon 이 XAML 에서 직접 관리되는 경우 사용하는 No-op 구현</summary>
    private sealed class NullTrayHost : ITrayHost
    {
        public void Start() { }
        public void ShowBalloonTip(string title, string message) { }
        public void Dispose() { }
    }
}
