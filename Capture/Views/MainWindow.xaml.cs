// author: eng-fe-desktop
// phase: engineering

using Capture.ViewModels;

namespace Capture.Views;

public partial class MainWindow : System.Windows.Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    public MainWindow(MainViewModel vm) : this()
    {
        DataContext = vm;
        // ContextMenu DataContext 우회용 — TrayIcon·ContextMenu 양쪽에 명시 할당.
        // (H.NotifyIcon.Wpf 의 ContextMenu 는 별도 visual tree 라 Window DataContext 자동 상속 X.
        //  RelativeSource PlacementTarget 바인딩도 일부 버전에서 안 잡혀 우클릭 메뉴 명령 전부 무반응.)
        TrayIcon.DataContext = vm;
        if (TrayIcon.ContextMenu != null)
            TrayIcon.ContextMenu.DataContext = vm;
    }

    protected override void OnClosed(EventArgs e)
    {
        // H.NotifyIcon.Wpf 의 TaskbarIcon 은 명시 Dispose 가 안 되면
        // 트레이에 고스트 아이콘이 남고 내부 윈도우 핸들이 살아 프로세스 종료를 막는다.
        TrayIcon?.Dispose();
        base.OnClosed(e);
    }
}
