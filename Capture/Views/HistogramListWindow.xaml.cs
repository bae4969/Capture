// author: eng-fe-desktop
// phase: engineering
// 9차 신규: HistogramListWindow — 256 bin 텍스트 그리드, ESC 닫기
// followup 2차: ctor 시그니처 변경 (HistogramViewModel vm) + PropertyChanged 구독 동기화 (BL-007, ADR-302)

using System.Text;
using System.Windows;
using Capture.ViewModels;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;

namespace Capture.Views;

public partial class HistogramListWindow : Window
{
    private readonly HistogramViewModel _vm;

    /// <summary>
    /// 부모 HistogramViewModel 을 받아 IsRgbMode·Bins·RedBins/GreenBins/BlueBins 변경에 동기화한다.
    /// (BL-007, ADR-302). 다중 List 창 동시 열림 시 각 인스턴스가 독립 구독.
    /// </summary>
    public HistogramListWindow(HistogramViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BuildText();

        _vm.PropertyChanged += OnVmPropertyChanged;
        Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;   // 메모리 누수 방지 (AC-007-3)
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // 11차: Channel 순환(Gray/R/G/B) + 4 bin 배열 변경 시 텍스트 재생성
        if (e.PropertyName == nameof(HistogramViewModel.Channel)
            || e.PropertyName == nameof(HistogramViewModel.Bins)
            || e.PropertyName == nameof(HistogramViewModel.RedBins)
            || e.PropertyName == nameof(HistogramViewModel.GreenBins)
            || e.PropertyName == nameof(HistogramViewModel.BlueBins))
        {
            BuildText();
        }
    }

    private void BuildText()
    {
        // 11차: 단일 채널 표시 (Channel 순환). 헤더 라벨은 채널명을 표시.
        int[] bins = _vm.Channel switch
        {
            HistogramChannel.R => _vm.RedBins,
            HistogramChannel.G => _vm.GreenBins,
            HistogramChannel.B => _vm.BlueBins,
            _                  => _vm.Bins,
        };
        string channelName = _vm.Channel == HistogramChannel.Gray ? "Gray" : _vm.Channel.ToString();
        string header = $" bin     {channelName,5}";

        HeaderLabel.Text = header;
        var sb = new StringBuilder();
        sb.AppendLine(header);
        sb.AppendLine(new string('-', 15));
        for (int i = 0; i < 256; i++)
            sb.AppendLine($"{i,4}  {bins[i],9}");
        BinTextBlock.Text = sb.ToString();
    }

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }
}
