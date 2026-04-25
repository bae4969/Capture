// author: eng-fe-desktop
// phase: engineering
// 보강 2차 호출 — handoff §4-1 #6
// preserve: 줌 범위 30~1000%, 단계 10% (migration-mapping.md §13-1 / CapturedViewModel 상수)
// 패턴: Attached Property Behavior (NuGet 추가 없음)
// 용도: UIElement(Image 등) 에 MouseWheelZoomBehavior.Command 첨부 시 MouseWheel → ICommand 실행
//       CommandParameter = MouseWheelEventArgs.Delta (양수=확대, 음수=축소)
//       ViewModel.OnMouseWheel(delta) 을 ICommand 또는 직접 메서드로 연결 가능

using System.Windows;
using System.Windows.Input;

namespace Capture.Behaviors;

/// <summary>
/// UIElement 에 첨부하면 MouseWheel 이벤트를 ICommand 로 전달합니다.
/// CommandParameter 는 <see cref="MouseWheelEventArgs.Delta"/> 정수값입니다.
/// XAML: behaviors:MouseWheelZoomBehavior.Command="{Binding MouseWheelCommand}"
/// </summary>
public static class MouseWheelZoomBehavior
{
    // ── Command 첨부 속성 ─────────────────────────────────────────────────────

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(MouseWheelZoomBehavior),
            new PropertyMetadata(null, OnCommandChanged));

    public static ICommand? GetCommand(DependencyObject obj)
        => (ICommand?)obj.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject obj, ICommand? value)
        => obj.SetValue(CommandProperty, value);

    // ── 내부 이벤트 구독 관리 ────────────────────────────────────────────────

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element) return;

        if (e.OldValue is ICommand)
            element.MouseWheel -= OnMouseWheel;

        if (e.NewValue is ICommand)
            element.MouseWheel += OnMouseWheel;
    }

    private static void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var command = GetCommand(d);
        if (command is null) return;

        // Delta 를 정수 박싱으로 CommandParameter 전달
        // ViewModel: void OnMouseWheel(int delta) → delta > 0 ? ZoomIn : ZoomOut
        if (command.CanExecute(e.Delta))
            command.Execute(e.Delta);
    }
}
