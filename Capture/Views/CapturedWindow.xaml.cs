// author: eng-fe-desktop
// phase: engineering
// 보강 2차 호출: Window_MouseLeftButtonDown → WindowDragBehavior, Image_MouseWheel → MouseWheelZoomBehavior,
//               Image_Drop → DropImageBehavior 로 이전.
// 8차: Image_MouseMove/MouseUp(오버레이 전용) 제거, OnKeyDown(ESC → Close) 추가 (ADR-006)
// 10차: Ctrl+드래그 드로잉 (InkCanvas 토글 + 색상 팔레트 + Save 합성 + Clear/Undo)
// 11차: 부유 팔레트 제거 → 우클릭 ContextMenu 통합. Ctrl+Z Undo / Ctrl+Shift+Z Redo (자체 스택).
//       줌 시 stroke 좌표 ScaleTransform 으로 유지. 스포이드 툴(메뉴 토글 → 다음 좌클릭으로 색 추출).
// namespace 명시: WPF 타입과 WinForms 타입 충돌 방지

using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Capture.Imaging;
using Capture.Services;
using Capture.ViewModels;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

// WinForms 와 WPF 동시 참조 시 충돌하는 타입을 명시적으로 별칭 지정
using WpfApplication = System.Windows.Application;
using WpfInput = System.Windows.Input;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDpiChangedEventArgs = System.Windows.DpiChangedEventArgs;

namespace Capture.Views;

public partial class CapturedWindow : Window
{
    public CapturedViewModel ViewModel { get; }

    private bool _ctrlDrawingActive;
    private bool _eyedropperMode;
    private bool _internalStrokeEdit;

    // 자체 Undo/Redo 스택. 한 stroke 이벤트(Add)당 하나의 항목.
    // Stroke 객체는 참조 공유 → 줌 시 in-place Transform 으로 좌표가 자동 갱신된다.
    private readonly List<StrokeCollection> _undoStack = new();
    private readonly List<StrokeCollection> _redoStack = new();

    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }

    public CapturedWindow(CapturedViewModel vm)
    {
        InitializeComponent();
        ViewModel = vm;
        DataContext = vm;

        UndoCommand = new RelayCommand(UndoStroke);
        RedoCommand = new RelayCommand(RedoStroke);

        vm.OpenHistogram += OnOpenHistogram;
        vm.GetOverlayBitmap = RenderInkOverlay;
        vm.SizeRatioChanged += OnSizeRatioChanged;
        Closed += OnWindowClosed;
        DpiChanged += OnDpiChanged;          // ADR-201

        // 기본 펜 속성 (마젠타 강조 — 조리개 듀오톤과 일관)
        DrawingCanvas.DefaultDrawingAttributes.Color = System.Windows.Media.Color.FromRgb(0xE9, 0x1E, 0x63);
        DrawingCanvas.DefaultDrawingAttributes.Width = 4;
        DrawingCanvas.DefaultDrawingAttributes.Height = 4;
        DrawingCanvas.DefaultDrawingAttributes.FitToCurve = true;

        // Stroke 추가 감지 → undo 스택에 push, redo 스택 비움
        DrawingCanvas.Strokes.StrokesChanged += OnStrokesChanged;
    }

    // ── 이미지 마우스 이벤트 ──────────────────────────────────────────────────

    private void Image_MouseDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (_eyedropperMode && e.ChangedButton == WpfInput.MouseButton.Left && e.ClickCount == 1)
        {
            HandleEyedropperPick(e.GetPosition(MainImage));
            e.Handled = true;
            return;
        }
        if (e.ChangedButton == WpfInput.MouseButton.Left && e.ClickCount == 2)
        {
            ViewModel.ToggleCompactCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void Image_MouseMove(object sender, WpfInput.MouseEventArgs e)
    {
        if (!_eyedropperMode) return;
        UpdateEyedropperPreview(e.GetPosition(MainImage));
    }

    private void Image_MouseLeave(object sender, WpfInput.MouseEventArgs e)
    {
        if (_eyedropperMode) EyedropperPopup.IsOpen = false;
    }

    // 컴팩트 인디케이터 더블클릭 → 펼침
    private void CompactIndicator_MouseLeftButtonDown(object sender, WpfMouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ViewModel.ToggleCompactCommand.Execute(null);
            e.Handled = true;
        }
    }

    // ── ESC 키 → 스포이드 모드 해제 또는 창 닫기 ──────────────────────────────

    private void OnKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.Escape)
        {
            if (_eyedropperMode) { ExitEyedropperMode(); e.Handled = true; return; }
            Close();
            e.Handled = true;
        }
    }

    // ── Ctrl 키 → InkCanvas 활성/비활성 ───────────────────────────────────────

    private void OnPreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.LeftCtrl || e.Key == WpfInput.Key.RightCtrl)
            SetDrawingActive(true);
    }

    private void OnPreviewKeyUp(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == WpfInput.Key.LeftCtrl || e.Key == WpfInput.Key.RightCtrl)
            SetDrawingActive(false);
    }

    // 창이 비활성화되면 Ctrl 키-업 이벤트를 못 받을 수 있으므로 안전하게 끈다.
    private void OnDeactivated(object? sender, EventArgs e) => SetDrawingActive(false);

    private void SetDrawingActive(bool active)
    {
        if (_ctrlDrawingActive == active) return;
        _ctrlDrawingActive = active;
        // 스포이드 모드 중에는 InkCanvas 가 입력을 가져가지 않게 강제 비활성.
        if (_eyedropperMode) active = false;
        DrawingCanvas.IsHitTestVisible = active;
        DrawingCanvas.EditingMode = active ? InkCanvasEditingMode.Ink : InkCanvasEditingMode.None;
    }

    // ── ContextMenu → 색상 / 두께 ──────────────────────────────────────────────

    private static readonly string[] _colorMenuNames =
        new[] { "MenuColor0","MenuColor1","MenuColor2","MenuColor3","MenuColor4","MenuColor5","MenuColor6","MenuColor7" };
    private static readonly string[] _thickMenuNames =
        new[] { "MenuThick0","MenuThick1","MenuThick2" };

    private void ColorMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string hex) return;
        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
        ApplyPenColor(color);
        SyncColorMenuChecks(color);
    }

    private void ApplyPenColor(System.Windows.Media.Color color)
    {
        DrawingCanvas.DefaultDrawingAttributes.Color = color;
        SyncColorMenuChecks(color);
    }

    private void SyncColorMenuChecks(System.Windows.Media.Color color)
    {
        foreach (var name in _colorMenuNames)
        {
            if (FindName(name) is not MenuItem mi) continue;
            if (mi.Tag is string hex)
            {
                var c = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                mi.IsChecked = (c == color);
            }
        }
    }

    private void ThickMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi || mi.Tag is not string s) return;
        if (!int.TryParse(s, out var width)) return;
        ApplyPenThickness(width);
    }

    private void ApplyPenThickness(double width)
    {
        DrawingCanvas.DefaultDrawingAttributes.Width = width;
        DrawingCanvas.DefaultDrawingAttributes.Height = width;
        foreach (var name in _thickMenuNames)
        {
            if (FindName(name) is not MenuItem mi) continue;
            if (mi.Tag is string s && int.TryParse(s, out var w))
                mi.IsChecked = ((int)width == w);
        }
    }

    // ── Undo / Redo / Clear ──────────────────────────────────────────────────

    private void OnStrokesChanged(object? sender, StrokeCollectionChangedEventArgs e)
    {
        if (_internalStrokeEdit) return;
        if (e.Added.Count > 0)
        {
            var added = new StrokeCollection();
            foreach (var s in e.Added) added.Add(s);
            _undoStack.Add(added);
            _redoStack.Clear();
        }
        // Removed by user 는 발생 시나리오 없음 — 무시
    }

    private void UndoStrokeMenuItem_Click(object sender, RoutedEventArgs e) => UndoStroke();
    private void RedoStrokeMenuItem_Click(object sender, RoutedEventArgs e) => RedoStroke();

    private void UndoStroke()
    {
        if (_undoStack.Count == 0) return;
        var top = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _internalStrokeEdit = true;
        foreach (var s in top) DrawingCanvas.Strokes.Remove(s);
        _internalStrokeEdit = false;
        _redoStack.Add(top);
    }

    private void RedoStroke()
    {
        if (_redoStack.Count == 0) return;
        var top = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _internalStrokeEdit = true;
        foreach (var s in top) DrawingCanvas.Strokes.Add(s);
        _internalStrokeEdit = false;
        _undoStack.Add(top);
    }

    private void ClearStrokesMenuItem_Click(object sender, RoutedEventArgs e)
    {
        _internalStrokeEdit = true;
        DrawingCanvas.Strokes.Clear();
        _internalStrokeEdit = false;
        _undoStack.Clear();
        _redoStack.Clear();
    }

    // ── 줌 변경 시 stroke 좌표 스케일 (좌표계 유지) ──────────────────────────

    private void OnSizeRatioChanged(int oldRatio, int newRatio)
    {
        if (oldRatio == newRatio || oldRatio <= 0) return;
        if (DrawingCanvas.Strokes.Count == 0) return;
        double scale = (double)newRatio / oldRatio;
        var matrix = Matrix.Identity;
        matrix.Scale(scale, scale);
        // applyToStylusTip:false → 좌표만 스케일, 펜 굵기는 그대로 유지(시각적 일관성)
        DrawingCanvas.Strokes.Transform(matrix, applyToStylusTip: false);
    }

    // ── 스포이드 툴 ───────────────────────────────────────────────────────────

    private void EyedropperMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem mi) return;
        if (mi.IsChecked) EnterEyedropperMode();
        else ExitEyedropperMode();
    }

    private void EnterEyedropperMode()
    {
        _eyedropperMode = true;
        if (FindName("MenuEyedropper") is MenuItem mi) mi.IsChecked = true;
        Mouse.OverrideCursor = System.Windows.Input.Cursors.Cross;
        // 스포이드가 우선이므로 InkCanvas 입력 비활성.
        DrawingCanvas.IsHitTestVisible = false;
        DrawingCanvas.EditingMode = InkCanvasEditingMode.None;
        // 미리보기는 첫 MouseMove 에서 열린다 — 여기서는 닫힌 상태 유지.
    }

    private void ExitEyedropperMode()
    {
        _eyedropperMode = false;
        if (FindName("MenuEyedropper") is MenuItem mi) mi.IsChecked = false;
        Mouse.OverrideCursor = null;
        EyedropperPopup.IsOpen = false;
        // Ctrl 이 여전히 눌려 있다면 InkCanvas 즉시 복귀.
        if (_ctrlDrawingActive)
        {
            DrawingCanvas.IsHitTestVisible = true;
            DrawingCanvas.EditingMode = InkCanvasEditingMode.Ink;
        }
    }

    private const int EyedropperSampleSize = 11; // 홀수 → 가운데 픽셀 존재

    private void UpdateEyedropperPreview(System.Windows.Point posInImageDip)
    {
        var src = ViewModel.DisplayedImage;
        if (src == null || MainImage.ActualWidth <= 0 || MainImage.ActualHeight <= 0)
        {
            EyedropperPopup.IsOpen = false;
            return;
        }

        // DIP → 픽셀 좌표 (중심)
        int cx = (int)(posInImageDip.X * src.PixelWidth / MainImage.ActualWidth);
        int cy = (int)(posInImageDip.Y * src.PixelHeight / MainImage.ActualHeight);

        // 11×11 샘플 영역 (이미지 경계 clamp)
        int half = EyedropperSampleSize / 2;
        int x0 = Math.Clamp(cx - half, 0, Math.Max(0, src.PixelWidth - EyedropperSampleSize));
        int y0 = Math.Clamp(cy - half, 0, Math.Max(0, src.PixelHeight - EyedropperSampleSize));
        int w = Math.Min(EyedropperSampleSize, src.PixelWidth);
        int h = Math.Min(EyedropperSampleSize, src.PixelHeight);

        // Bgra32 보장
        var bgra = (src.Format == PixelFormats.Bgra32)
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);

        try
        {
            var crop = new CroppedBitmap(bgra, new Int32Rect(x0, y0, w, h));
            crop.Freeze();
            EyedropperPreviewImage.Source = crop;
        }
        catch
        {
            EyedropperPopup.IsOpen = false;
            return;
        }

        // 중심 1픽셀 색 추출
        int rcx = Math.Clamp(cx, 0, src.PixelWidth - 1);
        int rcy = Math.Clamp(cy, 0, src.PixelHeight - 1);
        var buf = new byte[4];
        bgra.CopyPixels(new Int32Rect(rcx, rcy, 1, 1), buf, 4, 0);
        var color = System.Windows.Media.Color.FromArgb(0xFF, buf[2], buf[1], buf[0]);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";
        EyedropperHexText.Text = hex;
        EyedropperRgbText.Text = $"RGB({color.R}, {color.G}, {color.B})";
        EyedropperSwatch.Background = new SolidColorBrush(color);

        // Popup 위치 — 커서 우하단 +20,+20 오프셋
        EyedropperPopup.HorizontalOffset = posInImageDip.X + 20;
        EyedropperPopup.VerticalOffset = posInImageDip.Y + 20;
        if (!EyedropperPopup.IsOpen) EyedropperPopup.IsOpen = true;
    }

    private void HandleEyedropperPick(System.Windows.Point posInImageDip)
    {
        var src = ViewModel.DisplayedImage;
        if (src == null) { ExitEyedropperMode(); return; }
        if (MainImage.ActualWidth <= 0 || MainImage.ActualHeight <= 0) { ExitEyedropperMode(); return; }

        int px = (int)(posInImageDip.X * src.PixelWidth / MainImage.ActualWidth);
        int py = (int)(posInImageDip.Y * src.PixelHeight / MainImage.ActualHeight);
        if (px < 0 || py < 0 || px >= src.PixelWidth || py >= src.PixelHeight)
        { ExitEyedropperMode(); return; }

        // 32bpp Bgra32 로 변환 보장 (BitmapInterop 가 제공하는 형식이 가변할 수 있음)
        var bgra = (src.Format == PixelFormats.Bgra32)
            ? src
            : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var buf = new byte[4];
        bgra.CopyPixels(new Int32Rect(px, py, 1, 1), buf, 4, 0);
        var color = System.Windows.Media.Color.FromArgb(0xFF, buf[2], buf[1], buf[0]);
        var hex = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        // 1차 목적: 클립보드에 #RRGGBB 복사
        try { System.Windows.Clipboard.SetText(hex); }
        catch { /* 다른 프로세스가 점유 중이면 silent — 다음 시도 가능 */ }

        // 부수: 펜 색에도 적용 (드로잉으로 자연스럽게 이어지게)
        ApplyPenColor(color);
        ExitEyedropperMode();
    }

    // ── 저장 시 stroke 합성 ──────────────────────────────────────────────────
    // VM.SaveAs 가 GetOverlayBitmap 델리게이트를 호출하면 InkCanvas를 원본 픽셀 해상도로
    // 렌더링한 비트맵을 반환한다. 호출자가 Dispose 책임을 진다.

    private System.Drawing.Bitmap? RenderInkOverlay()
    {
        if (DrawingCanvas.Strokes.Count == 0) return null;
        int wPix = ViewModel.OriginalPixelWidth;
        int hPix = ViewModel.OriginalPixelHeight;
        if (wPix <= 0 || hPix <= 0) return null;
        double dpi = ViewModel.DisplayDpi;
        if (dpi < 1) dpi = 96.0;

        // 줌 상태에 무관하게 원본 픽셀 해상도로 stroke 를 굽기 위해
        // InkCanvas 의 현재 좌표계(SizeRatio 100% 기준 좌표) 와 동일하게 환산되도록
        // 임시로 strokes 를 100% 좌표로 클론하여 렌더한다.
        // 단순화: 현재 SizeRatio 가 100% 가 아니어도 strokes 는 100% 기준으로 다시 스케일.

        int ratio = ViewModel.SizeRatio;
        if (ratio <= 0) ratio = 100;

        // RenderTargetBitmap 의 dpi 를 _displayDpi 로 두면 InkCanvas 의 DIP 좌표가
        // 그대로 픽셀에 매핑된다(현재 줌이 적용된 픽셀 수 기준).
        int rtbWidth = wPix * ratio / 100;
        int rtbHeight = hPix * ratio / 100;
        if (rtbWidth < 1) rtbWidth = 1;
        if (rtbHeight < 1) rtbHeight = 1;

        var rt = new RenderTargetBitmap(rtbWidth, rtbHeight, dpi, dpi, PixelFormats.Pbgra32);
        DrawingCanvas.UpdateLayout();
        rt.Render(DrawingCanvas);

        // 줌이 100%가 아니면 원본 픽셀 해상도로 다운/업스케일.
        if (rtbWidth == wPix && rtbHeight == hPix)
            return BitmapInterop.ToBitmap(rt);

        var scaled = new TransformedBitmap(rt, new ScaleTransform((double)wPix / rtbWidth, (double)hPix / rtbHeight));
        return BitmapInterop.ToBitmap(scaled);
    }

    // ── 히스토그램 창 열기 ───────────────────────────────────────────────────

    private void OnOpenHistogram(System.Drawing.Bitmap? bitmap)
    {
        if (bitmap == null) return;
        var histVm = new HistogramViewModel();

        // 9차 추가 (BL-006, ADR-301): 마지막 사용자 토글 상태 복원
        // 11차: IsRgbMode bool → Channel enum (Gray/R/G/B 순환)
        var settings = ((App)WpfApplication.Current).Services.GetRequiredService<ISettingsService>();
        histVm.IsLogScale = settings.IsHistogramLogScale;
        histVm.Channel    = (HistogramChannel)settings.HistogramChannel;
        // ChannelLabel 은 Channel partial method hook 이 자동 동기화

        histVm.ComputeFromBitmap(bitmap, ViewModel.TitleInfo);

        // persist 구독 (토글 변경 즉시 SettingsService in-memory 갱신)
        // 디스크 Save 는 App.OnExit 통합 호출
        histVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(HistogramViewModel.IsLogScale))
                settings.IsHistogramLogScale = histVm.IsLogScale;
            else if (e.PropertyName == nameof(HistogramViewModel.Channel))
                settings.HistogramChannel = (int)histVm.Channel;
        };

        var histWin = new HistogramWindow(histVm)
        {
            Left = Left,
            Top = Top + Height
        };
        histWin.Show();
    }

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        ViewModel.SizeRatioChanged -= OnSizeRatioChanged;
        DrawingCanvas.Strokes.StrokesChanged -= OnStrokesChanged;
        ViewModel.GetOverlayBitmap = null;
        ViewModel.Dispose();
    }

    // ── DPI 변경 핸들러 (ADR-201) ────────────────────────────────────────────
    private void OnDpiChanged(object sender, WpfDpiChangedEventArgs e)
    {
        ViewModel.RefreshDpi(e.NewDpi.PixelsPerDip * 96.0);
    }
}
