// author: eng-fe-desktop
// phase: engineering
// 보강 2차 호출 — handoff §4-1 #6
// preserve: FileDrop DataFormat 처리, 첫 번째 파일 경로 전달 (migration-mapping.md §13-1)
// 패턴: Attached Property Behavior (NuGet 추가 없음)
// 용도: UIElement 에 첨부 시 AllowDrop=True + Drop 이벤트 → ICommand(CommandParameter=파일경로) 실행
//       ViewModel: void LoadFromImageFile(string path) → RelayCommand<string> 또는 ICommand 연결

using System.Windows;
using System.Windows.Input;

// UseWindowsForms=true 환경에서 WinForms DragEventArgs 와 충돌하므로 명시 별칭 사용
using WpfDragEventArgs = System.Windows.DragEventArgs;

namespace Capture.Behaviors;

/// <summary>
/// UIElement 에 첨부하면 AllowDrop=True 설정 후 외부 파일 드롭 이벤트를 ICommand 로 전달합니다.
/// CommandParameter 는 드롭된 파일의 첫 번째 경로 문자열입니다.
/// XAML: behaviors:DropImageBehavior.Command="{Binding LoadFromFileCommand}"
/// </summary>
public static class DropImageBehavior
{
    // ── Command 첨부 속성 ─────────────────────────────────────────────────────

    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.RegisterAttached(
            "Command",
            typeof(ICommand),
            typeof(DropImageBehavior),
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
        {
            element.Drop -= OnDrop;
        }

        if (e.NewValue is ICommand)
        {
            // AllowDrop 활성화 (원본 XAML AllowDrop="True" 이전)
            element.SetValue(UIElement.AllowDropProperty, true);
            element.Drop += OnDrop;
        }
        else
        {
            // Command 제거 시 AllowDrop 비활성화
            element.SetValue(UIElement.AllowDropProperty, false);
        }
    }

    private static void OnDrop(object sender, WpfDragEventArgs e)
    {
        if (sender is not DependencyObject d) return;
        var command = GetCommand(d);
        if (command is null) return;

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is not string[] files
            || files.Length == 0) return;

        var filePath = files[0];
        if (command.CanExecute(filePath))
            command.Execute(filePath);
    }
}
