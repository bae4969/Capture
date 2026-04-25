// author: eng-fe-desktop
// phase: engineering
// preserve: 원본 OnPaint — 256개 수직 선 그리기 (Canvas 방식으로 이식)
// 8차: ImageJ 스타일 재구현 — DrawHistogram 갱신, MouseMove hover, KeyDown(ESC → Close)
// 9차: DrawHistogram IsLogScale·IsRgbMode 분기. RGB 3채널 반투명 막대. ShowListRequested 구독.
// namespace 명시: WPF/WinForms MouseEventArgs·KeyEventArgs 충돌 방지 (UseWindowsForms=true 환경)

using System.Windows;
using System.Windows.Media;
using System.Windows.Shapes;
using Capture.ViewModels;
// HistogramChannel enum 직접 참조 — Capture.ViewModels 네임스페이스
using WpfBrushes = System.Windows.Media.Brushes;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfKey = System.Windows.Input.Key;

namespace Capture.Views;

public partial class HistogramWindow : Window
{
    public HistogramViewModel ViewModel { get; }

    public HistogramWindow(HistogramViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistogramViewModel.Bins)
                || e.PropertyName == nameof(HistogramViewModel.IsLogScale)
                || e.PropertyName == nameof(HistogramViewModel.Channel))
                DrawHistogram();
        };

        // ShowListRequested 이벤트 구독 → HistogramListWindow 띄움
        vm.ShowListRequested += OnShowListRequested;

        // ComputeFromBitmap 가 ctor 호출 전에 이미 Bins 를 채운 경우(타이밍),
        // PropertyChanged 가 구독 전 발화돼 한 번도 안 그려진다.
        // Loaded 시점(Canvas ActualWidth/Height 확정 후) 1회 강제 호출로 보장.
        Loaded += (_, _) => DrawHistogram();
    }

    private void HistogramCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        DrawHistogram();
    }

    // ── 9차: DrawHistogram — IsLogScale·IsRgbMode 분기 ───────────────────────

    private void DrawHistogram()
    {
        HistogramCanvas.Children.Clear();
        double canvasH = HistogramCanvas.ActualHeight;
        double canvasW = HistogramCanvas.ActualWidth;
        if (canvasH <= 0 || canvasW <= 0) return;

        // 11차 (followup 2차+): 채널 순환 (Gray → R → G → B → Gray ...) 단일 채널 표시
        (int[]? bins, System.Windows.Media.Color color, string? label) = ViewModel.Channel switch
        {
            HistogramChannel.R => (ViewModel.RedBins,   System.Windows.Media.Color.FromArgb(255, 220,  50,  50), "R"),
            HistogramChannel.G => (ViewModel.GreenBins, System.Windows.Media.Color.FromArgb(255,  50, 180,  50), "G"),
            HistogramChannel.B => (ViewModel.BlueBins,  System.Windows.Media.Color.FromArgb(255,  50, 100, 220), "B"),
            _                  => (ViewModel.Bins,      System.Windows.Media.Color.FromArgb(255,  30,  30,  30), null),
        };
        if (bins == null) return;
        DrawChannelInArea(bins, color, label, 0, canvasH, canvasW);
    }

    /// <summary>
    /// 지정한 Y 영역 [topY, topY+areaH] 안에 단일 채널 막대를 그린다.
    /// IsLogScale 이면 로그 스케일 높이 계산을 적용한다.
    /// label 이 있으면 좌상단에 채널 라벨을 표시한다.
    /// </summary>
    private void DrawChannelInArea(int[] bins, System.Windows.Media.Color color, string? label,
                                    double topY, double areaH, double canvasW)
    {
        if (bins == null) return;
        int max = 0;
        for (int i = 0; i < bins.Length; i++)
            if (bins[i] > max) max = bins[i];
        if (max <= 0) return;

        var brush = new SolidColorBrush(color);
        bool logScale = ViewModel.IsLogScale;
        double baselineY = topY + areaH;

        for (int i = 0; i < bins.Length; i++)
        {
            if (bins[i] <= 0) continue;

            double height;
            if (logScale)
                height = areaH * Math.Log(bins[i] + 1) / Math.Log(max + 1);
            else
                height = areaH * bins[i] / (double)max;

            double x = canvasW * i / 256.0;
            double thickness = canvasW / 256.0;
            if (thickness < 1.0) thickness = 1.0;

            var line = new Line
            {
                X1 = x, Y1 = baselineY,
                X2 = x, Y2 = baselineY - height,
                Stroke = brush,
                StrokeThickness = thickness
            };
            HistogramCanvas.Children.Add(line);
        }

        if (!string.IsNullOrEmpty(label))
        {
            var tb = new System.Windows.Controls.TextBlock
            {
                Text = label,
                FontWeight = FontWeights.Bold,
                FontSize = 10,
                Foreground = brush
            };
            System.Windows.Controls.Canvas.SetLeft(tb, 4);
            System.Windows.Controls.Canvas.SetTop(tb, topY + 2);
            HistogramCanvas.Children.Add(tb);
        }
    }

    /// <summary>RGB 채널 분할용 얇은 회색 가이드 라인.</summary>
    private void DrawDividerLine(double x1, double y, double x2)
    {
        var line = new Line
        {
            X1 = x1, Y1 = y, X2 = x2, Y2 = y,
            Stroke = WpfBrushes.LightGray,
            StrokeThickness = 0.5,
            StrokeDashArray = new DoubleCollection { 2, 2 }
        };
        HistogramCanvas.Children.Add(line);
    }

    // ── hover: X 좌표 → bin index → HoverValue/HoverCount 갱신 ────────────────

    private void HistogramCanvas_MouseMove(object sender, WpfMouseEventArgs e)
    {
        var bins = ViewModel.Bins;
        if (bins == null) return;

        double canvasW = HistogramCanvas.ActualWidth;
        if (canvasW <= 0) return;

        double x = e.GetPosition(HistogramCanvas).X;
        int binIndex = (int)(x / canvasW * 256);
        binIndex = Math.Clamp(binIndex, 0, 255);

        ViewModel.HoverValue = binIndex;
        ViewModel.HoverCount = bins[binIndex];
    }

    private void HistogramCanvas_MouseLeave(object sender, WpfMouseEventArgs e)
    {
        // 마우스가 Canvas 를 벗어나면 hover 값 리셋
        ViewModel.HoverValue = 0;
        ViewModel.HoverCount = ViewModel.Bins?[0] ?? 0;
    }

    // ── ESC 키 → 창 닫기 ─────────────────────────────────────────────────────

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfKey.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    // ── 9차: ShowList 이벤트 → HistogramListWindow 띄움 ─────────────────────
    // followup 2차: ctor 시그니처 단순화 — vm 1개만 전달 (BL-007, ADR-302)
    private void OnShowListRequested()
    {
        var listWin = new HistogramListWindow(ViewModel)
        {
            Owner = this
        };
        listWin.Show();
    }
}
