// author: eng-fe-desktop
// phase: engineering
// 보강 2차 호출 — handoff §4-1 #6
// preserve: SendMessage WM_NCLBUTTONDOWN + HT_CAPTION 드래그 이동 방식 (migration-mapping.md §13-1)
// 패턴: Attached Property Behavior (NuGet 추가 없음)
// 용도: Window 에 WindowDragBehavior.IsEnabled="True" 첨부 시 MouseLeftButtonDown → DragMove(Win32)

using System.Windows;
using System.Windows.Interop;
using Capture.Interop;

namespace Capture.Behaviors;

/// <summary>
/// Window 에 첨부하면 MouseLeftButtonDown 시 SendMessage(WM_NCLBUTTONDOWN, HT_CAPTION) 을 호출해
/// 창 타이틀바 드래그와 동일하게 창 이동을 활성화합니다.
/// XAML: behaviors:WindowDragBehavior.IsEnabled="True"
/// </summary>
public static class WindowDragBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(WindowDragBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject obj)
        => (bool)obj.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject obj, bool value)
        => obj.SetValue(IsEnabledProperty, value);

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window) return;

        if ((bool)e.NewValue)
            window.MouseLeftButtonDown += OnMouseLeftButtonDown;
        else
            window.MouseLeftButtonDown -= OnMouseLeftButtonDown;
    }

    private static void OnMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is not Window window) return;
        // Ctrl 누른 상태에서는 드래그-이동을 양보하여 InkCanvas 등 자식 컨트롤이 입력을 받게 한다.
        if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) != 0) return;
        // 자식 컨트롤이 이미 e.Handled = true 로 처리한 경우(InkCanvas가 stroke 시작) DragMove 시작 금지.
        if (e.Handled) return;
        var handle = new WindowInteropHelper(window).Handle;
        User32.ReleaseCapture();
        User32.SendMessage(handle, NativeConstants.WM_NCLBUTTONDOWN, NativeConstants.HT_CAPTION, 0);
    }
}
