// author: eng-fe-desktop
// phase: engineering
// HistogramWindow 히스토그램 막대 높이 변환

using System.Globalization;
using System.Windows.Data;

namespace Capture.Converters;

/// <summary>
/// 히스토그램 bin 값 → 픽셀 높이 변환.
/// ConverterParameter 로 최대값을 받아 정규화.
/// 단순 버전: 80px 최대 (공간 고정).
/// </summary>
public class BinToHeightConverter : IValueConverter
{
    public static readonly BinToHeightConverter Instance = new();

    private const double MaxHeight = 80.0;

    // maxBinValue 는 HistogramViewModel.MaxBinValue 가 MultiBinding 으로 전달
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not int bin) return 0.0;
        if (bin <= 0) return 0.0;

        // parameter: MaxBinValue
        int max = 1;
        if (parameter is int p) max = p;
        else if (parameter is string s && int.TryParse(s, out int ps)) max = ps;

        return MaxHeight * bin / max;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
