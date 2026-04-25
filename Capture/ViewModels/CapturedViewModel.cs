// author: eng-fe-desktop
// phase: engineering
// preserve: 줌 30~1000%, 채널 추출, 히스토그램, 불투명도 — migration-mapping.md §13-1
// natural-fix: "Calcaulate Histogram" → "Calculate Histogram" (오타 수정)
// natural-fix: silent catch → MessageBox.Show (ADR 명시)
// drop: live capture timer (ADR-111 defer)
// drop: Overlay 전부 제거 (사용자 결정 2026-04-25, ADR-006)
// drop: Email 제거 (사용자 결정 2026-04-25, ADR-006)
// 3차 DPI fix: SetImage(bitmap, dpi) 오버로드 + _displayDpi 필드 + RefreshDisplayedImage DPI 적용 (ADR-106)
// 8차: EffectiveOpacity 파생 프로퍼티 추가 (IsCompact 시 1.0, 아닐 시 WindowOpacity)

using System.Drawing;
using System.Drawing.Imaging;
using Capture.Imaging;
using Capture.Models;
using Capture.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WpfMessageBox = System.Windows.MessageBox;
using WpfMessageBoxButton = System.Windows.MessageBoxButton;
using WpfMessageBoxImage = System.Windows.MessageBoxImage;

namespace Capture.ViewModels;

public partial class CapturedViewModel : ObservableObject, IDisposable
{
    public const int COMPACT_SIZE = 10;
    public const int MIN_SIZE_RATIO = 30;
    public const int MAX_SIZE_RATIO = 1000;
    public const int STEP_SIZE_RATIO = 10;

    private readonly IFileSaveService _fileSave;
    private readonly ISettingsService _settings;
    private readonly IClipboardService _clipboard;

    private System.Drawing.Bitmap? _originalBitmap;
    private bool _disposed;

    /// <summary>
    /// 표시 DPI. ToBitmapSource 호출 시 전달된다 (ADR-106 PerMonitorV2 fix).
    /// 96 = 100%, 120 = 125%, 144 = 150%, 192 = 200%.
    /// </summary>
    private double _displayDpi = 96.0;

    [ObservableProperty] private System.Windows.Media.Imaging.BitmapSource? _displayedImage;
    [ObservableProperty] private int _sizeRatio = 100;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EffectiveOpacity))]
    private double _windowOpacity = 1.0;

    [ObservableProperty] private bool _isLiveCaptureMode = false; // defer ADR-111
    [ObservableProperty] private string _titleInfo = string.Empty;

    // 8차: IsCompact 변경 시 EffectiveOpacity 도 함께 갱신
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNormal))]
    [NotifyPropertyChangedFor(nameof(EffectiveOpacity))]
    private bool _isCompact;

    public bool IsNormal => !IsCompact;

    /// <summary>
    /// 실제 창에 적용할 불투명도.
    /// 컴팩트 모드일 때는 항상 1.0 (인디케이터는 완전 불투명).
    /// 일반 모드일 때는 WindowOpacity 를 그대로 사용.
    /// </summary>
    public double EffectiveOpacity => IsCompact ? 1.0 : WindowOpacity;

    // 캡처 메타데이터
    public Rectangle Bounds { get; set; }
    public Rectangle BoundsCapture { get; set; }
    public int IdxScreen { get; set; }
    public bool LoadFromFile { get; set; }

    public event Action<System.Drawing.Bitmap?>? OpenHistogram;

    /// <summary>
    /// SizeRatio 가 바뀌었을 때 (oldRatio, newRatio) 를 전달한다.
    /// View 가 InkCanvas 의 stroke 좌표를 비율로 스케일하기 위해 구독한다.
    /// </summary>
    public event Action<int, int>? SizeRatioChanged;

    partial void OnSizeRatioChanged(int oldValue, int newValue)
        => SizeRatioChanged?.Invoke(oldValue, newValue);

    /// <summary>
    /// View 가 InkCanvas 등 오버레이 비트맵을 원본 픽셀 해상도로 렌더링해 반환한다.
    /// SaveAs 시 호출되어 원본 위에 합성된다. View 가 Dispose 책임을 양도하므로
    /// VM 은 using 으로 감싸 해제한다.
    /// </summary>
    public Func<System.Drawing.Bitmap?>? GetOverlayBitmap { get; set; }

    public int OriginalPixelWidth => _originalBitmap?.Width ?? 0;
    public int OriginalPixelHeight => _originalBitmap?.Height ?? 0;
    public double DisplayDpi => _displayDpi;

    public CapturedViewModel(
        IFileSaveService fileSave,
        ISettingsService settings,
        IClipboardService clipboard)
    {
        _fileSave = fileSave;
        _settings = settings;
        _clipboard = clipboard;
    }

    /// <summary>
    /// 캡처 비트맵을 설정한다. DPI = 96 (100%) 기본값.
    /// 파일 로드, 채널 추출 등 화면 DPI 와 무관한 경우에 사용한다.
    /// </summary>
    public void SetImage(System.Drawing.Bitmap bitmap)
        => SetImage(bitmap, dpi: 96.0);

    /// <summary>
    /// 캡처 비트맵과 표시 DPI 를 함께 설정한다 (ADR-106 PerMonitorV2 fix).
    /// MainViewModel.OnCaptureCompleted 에서 캡처 영역 모니터의 DPI 를 전달해 호출한다.
    /// </summary>
    public void SetImage(System.Drawing.Bitmap bitmap, double dpi)
    {
        _originalBitmap?.Dispose();
        _originalBitmap = bitmap;
        _displayDpi = dpi;
        RefreshDisplayedImage();
    }

    /// <summary>
    /// CapturedWindow 가 DpiChanged 이벤트를 받았을 때 호출한다 (ADR-201).
    /// 표시 DPI 만 갱신하고 BitmapSource 를 새 DPI 메타로 재생성한다.
    /// _originalBitmap 은 그대로 유지 (Dispose 하지 않음 — use-after-free 방지).
    /// </summary>
    public void RefreshDpi(double newDpi)
    {
        if (_originalBitmap == null) return;
        _displayDpi = newDpi;
        RefreshDisplayedImage();
    }

    private void RefreshDisplayedImage()
    {
        if (_originalBitmap == null) return;

        int w = _originalBitmap.Width * SizeRatio / 100;
        int h = _originalBitmap.Height * SizeRatio / 100;
        if (w < 1) w = 1;
        if (h < 1) h = 1;

        System.Drawing.Bitmap rendered;
        if (SizeRatio == 100)
        {
            rendered = (System.Drawing.Bitmap)_originalBitmap.Clone();
        }
        else
        {
            rendered = new System.Drawing.Bitmap(w, h);
            using var g = Graphics.FromImage(rendered);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(_originalBitmap, 0, 0, w, h);
        }

        // 3차 DPI fix: _displayDpi 로 BitmapSource 재생성 — 올바른 물리 픽셀 크기로 표시 (ADR-106)
        DisplayedImage = BitmapInterop.ToBitmapSource(rendered, _displayDpi);
        rendered.Dispose();
    }

    // ── 줌 커맨드 ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ZoomIn()
    {
        if (SizeRatio < MAX_SIZE_RATIO)
            SizeRatio = Math.Min(SizeRatio + STEP_SIZE_RATIO, MAX_SIZE_RATIO);
        RefreshDisplayedImage();
    }

    [RelayCommand]
    private void ZoomOut()
    {
        if (SizeRatio > MIN_SIZE_RATIO)
            SizeRatio = Math.Max(SizeRatio - STEP_SIZE_RATIO, MIN_SIZE_RATIO);
        RefreshDisplayedImage();
    }

    [RelayCommand]
    private void ResetSize()
    {
        SizeRatio = 100;
        RefreshDisplayedImage();
    }

    // ── 저장 ─────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SaveAs()
    {
        if (_originalBitmap == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|BMP Image (*.bmp)|*.bmp",
            Title = "Save As"
        };
        if (dlg.ShowDialog() == true)
        {
            var ext = System.IO.Path.GetExtension(dlg.FileName).ToLowerInvariant();
            var format = ext switch
            {
                ".jpg" or ".jpeg" => ImageFormat.Jpeg,
                ".bmp"            => ImageFormat.Bmp,
                _                 => ImageFormat.Png
            };
            try
            {
                using var overlay = GetOverlayBitmap?.Invoke();
                if (overlay != null)
                {
                    using var composite = new System.Drawing.Bitmap(_originalBitmap.Width, _originalBitmap.Height, PixelFormat.Format32bppArgb);
                    using (var g = Graphics.FromImage(composite))
                    {
                        g.DrawImage(_originalBitmap, 0, 0, _originalBitmap.Width, _originalBitmap.Height);
                        g.DrawImage(overlay, 0, 0, _originalBitmap.Width, _originalBitmap.Height);
                    }
                    _fileSave.SaveAs(composite, dlg.FileName, format);
                }
                else
                {
                    _fileSave.SaveAs(_originalBitmap, dlg.FileName, format);
                }
            }
            catch (Exception ex)
            {
                // natural-fix: silent catch → MessageBox
                WpfMessageBox.Show($"저장 실패: {ex.Message}", "오류", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
            }
        }
    }

    // ── 불투명도 ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SetOpacity(string param)
    {
        if (double.TryParse(param, out double val))
            WindowOpacity = val;
    }

    // ── 컴팩트 토글 (원본 ToggleCompact, 가시성 개선) ─────────────────────────

    [RelayCommand]
    private void ToggleCompact() => IsCompact = !IsCompact;

    // ── 채널 추출 ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ExtractChannel(string channelName)
    {
        if (_originalBitmap == null) return;
        if (!Enum.TryParse<ImageChannelSplitter.ImageChannels>(channelName, out var channel)) return;
        try
        {
            var extracted = ImageChannelSplitter.ExtractChannel(_originalBitmap, channel);
            var vm = new CapturedViewModel(_fileSave, _settings, _clipboard);
            vm.SetImage(extracted);
            var win = new Views.CapturedWindow(vm);
            win.Show();
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"채널 추출 실패: {ex.Message}", "오류", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    // ── 히스토그램 ─────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ShowHistogram()
    {
        // natural-fix: "Calcaulate Histogram" 오타 수정 (메뉴 텍스트는 View 에서 "Calculate Histogram")
        OpenHistogram?.Invoke(_originalBitmap);
    }

    // ── 파일에서 로드 ─────────────────────────────────────────────────────────

    public void LoadFromImageFile(string path)
    {
        try
        {
            var bmp = new System.Drawing.Bitmap(path);
            LoadFromFile = true;
            SetImage(bmp);
        }
        catch (Exception ex)
        {
            WpfMessageBox.Show($"이미지 로드 실패: {ex.Message}", "오류", WpfMessageBoxButton.OK, WpfMessageBoxImage.Error);
        }
    }

    // ── 마우스 휠 줌 (Behavior 에서 ICommand 로 호출) ──────────────────────────

    public void OnMouseWheel(int delta)
    {
        if (delta > 0) ZoomIn();
        else ZoomOut();
    }

    /// <summary>
    /// MouseWheelZoomBehavior 가 ICommand 로 호출합니다.
    /// CommandParameter = MouseWheelEventArgs.Delta (int — 박싱됨)
    /// </summary>
    [RelayCommand]
    private void MouseWheel(object? deltaObj)
    {
        if (deltaObj is int delta)
            OnMouseWheel(delta);
    }

    /// <summary>
    /// DropImageBehavior 가 ICommand 로 호출합니다.
    /// CommandParameter = 드롭된 파일 경로 문자열
    /// </summary>
    [RelayCommand]
    private void DropFile(string? path)
    {
        if (!string.IsNullOrEmpty(path))
            LoadFromImageFile(path);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _originalBitmap?.Dispose();
            _originalBitmap = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
