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
        // ContextMenu DataContext 우회용 — TrayIcon 자체 DataContext 를 명시 할당
        // (TaskbarIcon 은 별도 visual tree 라 Window DataContext 를 자동 상속하지 않음)
        TrayIcon.DataContext = vm;
    }
}
