// author: eng-fe-desktop
// phase: engineering
// preserve: 256-bin 히스토그램 데이터 — migration-mapping.md §15-1
// FormHistogram 의 OnPaint 로직을 ViewModel 데이터로 변환
// 8차: ImageJ 스타일 재구현 — 통계(N/Mean/StdDev/Min/Max/Mode/ModeCount) + 헤더 + hover 프로퍼티 추가
// 9차: IsLogScale·IsRgbMode·RedBins/GreenBins/BlueBins 추가. CopyStatistics·ShowList·ToggleLog·ToggleRgb RelayCommand
// followup 1차: ComputeFromBitmap GetPixel 루프 → LockBitmap + byte[] 직접 접근 (ADR-202)

using Capture.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Capture.ViewModels;

public partial class HistogramViewModel : ObservableObject
{
    [ObservableProperty]
    private int[] _bins = new int[256];

    [ObservableProperty]
    private string _channelLabel = "Gray";

    [ObservableProperty]
    private int _maxBinValue = 1;

    // ── ImageJ 헤더 ──────────────────────────────────────────────────────────
    // 예: "300x246 pixels; RGB; 288K"
    [ObservableProperty]
    private string _headerText = string.Empty;

    // ── 통계 ─────────────────────────────────────────────────────────────────
    [ObservableProperty] private long _totalPixels;
    [ObservableProperty] private double _mean;
    [ObservableProperty] private double _stdDev;
    [ObservableProperty] private int _min;
    [ObservableProperty] private int _max;
    [ObservableProperty] private int _mode;       // 가장 빈도 높은 값 (0~255)
    [ObservableProperty] private int _modeCount;  // 그 빈도

    // ── 마우스 hover 정보 ─────────────────────────────────────────────────────
    [ObservableProperty] private int _hoverValue;  // 0~255
    [ObservableProperty] private int _hoverCount;  // bins[hoverValue]

    // ── 9차: Log 스케일 토글 ─────────────────────────────────────────────────
    [ObservableProperty] private bool _isLogScale;

    // ── 11차 (followup 2차+): 채널 순환 (Gray → R → G → B → Gray ...) ───────
    // 기존 IsRgbMode bool (Gray ↔ RGB 3분할) 폐기. 단일 채널 단계별 표시로 변경.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRgbMode))]
    private HistogramChannel _channel = HistogramChannel.Gray;

    // 호환 파생 — 기존 IsRgbMode 구독자 (HistogramListWindow 등) 가 Gray 외 채널을 RGB 로 인식
    public bool IsRgbMode => Channel != HistogramChannel.Gray;

    partial void OnChannelChanged(HistogramChannel value)
    {
        ChannelLabel = value.ToString();
    }

    // ── 9차: RGB 채널별 bins ──────────────────────────────────────────────────
    [ObservableProperty] private int[] _redBins = new int[256];
    [ObservableProperty] private int[] _greenBins = new int[256];
    [ObservableProperty] private int[] _blueBins = new int[256];

    // ── 9차: ShowList 이벤트 (HistogramWindow 가 구독 → HistogramListWindow 띄움) ─
    public event Action? ShowListRequested;

    /// <summary>
    /// 비트맵에서 그레이스케일 256-bin 히스토그램과 통계를 계산한다.
    /// ImageJ 스타일 헤더도 함께 생성한다.
    /// 9차: RGB 채널별 bin 도 함께 계산.
    /// </summary>
    public void ComputeFromBitmap(System.Drawing.Bitmap bitmap, string sourceTitle = "")
    {
        var newBins = new int[256];
        var redBins = new int[256];
        var greenBins = new int[256];
        var blueBins = new int[256];
        long pixelCount = (long)bitmap.Width * bitmap.Height;

        // ADR-202: GetPixel 루프 → LockBitmap + byte[] 직접 접근 (Marshal.Copy, unsafe 미도입)
        var lb = new LockBitmap(bitmap);
        lb.LockBits();
        try
        {
            int bpp = lb.Depth / 8;       // 3 (24bpp) or 4 (32bpp)
            int stride = lb.Stride;
            byte[] pixels = lb.Pixels;
            int w = lb.Width;
            int h = lb.Height;

            for (int y = 0; y < h; y++)
            {
                int row = y * stride;
                for (int x = 0; x < w; x++)
                {
                    int idx = row + x * bpp;
                    byte b = pixels[idx + 0];
                    byte g = pixels[idx + 1];
                    byte r = pixels[idx + 2];
                    // BT.601 grayscale
                    int gray = (int)(0.299 * r + 0.587 * g + 0.114 * b);
                    if (gray < 0) gray = 0; else if (gray > 255) gray = 255;
                    newBins[gray]++;
                    // RGB 채널별 binning (9차)
                    redBins[r]++;
                    greenBins[g]++;
                    blueBins[b]++;
                }
            }
        }
        finally
        {
            lb.UnlockBits();
        }

        // ── 통계 계산 ────────────────────────────────────────────────────────

        // Min / Max (bins 가 0이 아닌 첫/마지막 값)
        int minVal = 0;
        int maxVal = 255;
        for (int i = 0; i < 256; i++) { if (newBins[i] > 0) { minVal = i; break; } }
        for (int i = 255; i >= 0; i--) { if (newBins[i] > 0) { maxVal = i; break; } }

        // Mean: Σ(value × count) / N
        double sum = 0.0;
        for (int i = 0; i < 256; i++) sum += (double)i * newBins[i];
        double meanVal = pixelCount > 0 ? sum / pixelCount : 0.0;

        // StdDev: sqrt( Σ((value - mean)² × count) / N )
        double variance = 0.0;
        for (int i = 0; i < 256; i++)
        {
            double diff = i - meanVal;
            variance += diff * diff * newBins[i];
        }
        double stdDevVal = pixelCount > 0 ? Math.Sqrt(variance / pixelCount) : 0.0;

        // Mode: 가장 빈도 높은 값
        int modeVal = 0;
        int modeCountVal = 0;
        for (int i = 0; i < 256; i++)
        {
            if (newBins[i] > modeCountVal)
            {
                modeCountVal = newBins[i];
                modeVal = i;
            }
        }

        // MaxBinValue (그래프 높이 정규화용)
        int maxBin = modeCountVal > 0 ? modeCountVal : 1;

        // ImageJ 헤더: "{W}x{H} pixels; RGB; {KB}K"
        long byteSize = pixelCount * 3; // RGB 24bpp 기준
        string header = $"{bitmap.Width}x{bitmap.Height} pixels; RGB; {byteSize / 1024}K";

        // ── 프로퍼티 일괄 갱신 ──────────────────────────────────────────────
        Bins = newBins;
        RedBins = redBins;
        GreenBins = greenBins;
        BlueBins = blueBins;
        MaxBinValue = maxBin;
        TotalPixels = pixelCount;
        Min = minVal;
        Max = maxVal;
        Mean = Math.Round(meanVal, 3);
        StdDev = Math.Round(stdDevVal, 3);
        Mode = modeVal;
        ModeCount = modeCountVal;
        HeaderText = header;

        // hover 초기값 리셋
        HoverValue = 0;
        HoverCount = newBins[0];
    }

    // ── 9차: RelayCommand — CopyStatistics ───────────────────────────────────
    [RelayCommand]
    private void CopyStatistics()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(HeaderText);
        sb.AppendLine("N\tMean\tStdDev\tMin\tMax\tMode\tModeCount");
        sb.AppendLine($"{TotalPixels}\t{Mean:0.000}\t{StdDev:0.000}\t{Min}\t{Max}\t{Mode}\t{ModeCount}");
        System.Windows.Clipboard.SetText(sb.ToString());
    }

    // ── 9차: RelayCommand — ShowList ─────────────────────────────────────────
    [RelayCommand]
    private void ShowList() => ShowListRequested?.Invoke();

    // ── 9차: RelayCommand — ToggleLog ────────────────────────────────────────
    [RelayCommand]
    private void ToggleLog() => IsLogScale = !IsLogScale;

    // ── 11차: RelayCommand — CycleChannel (Gray → R → G → B → Gray) ──────────
    [RelayCommand]
    private void CycleChannel()
    {
        Channel = (HistogramChannel)(((int)Channel + 1) % 4);
    }
}

/// <summary>히스토그램 표시 채널. Gray=intensity, R/G/B=각 채널.</summary>
public enum HistogramChannel
{
    Gray = 0,
    R = 1,
    G = 2,
    B = 3
}
