// author: eng-fe-desktop
// phase: engineering
// preserve: ToggleMode() 원본 switch 순서 동일 — migration-mapping.md §5-1

using Capture.Models;

namespace Capture.Services;

public class CaptureModeService : ICaptureModeService
{
    public CaptureMode CurrentMode { get; private set; } = CaptureMode.None;
    public System.Drawing.Rectangle LastCapturedRegion { get; set; }
    public bool IsLastRegionAvailable => LastCapturedRegion != default;

    public void ToggleMode()
    {
        // preserve: 원본 CaptureIt.ToggleMethod() 순서와 동일
        // None→Region, Region→LastRegion(LastRegion 존재 시) 또는 Window, LastRegion→Window,
        // Window→ColorPick, ColorPick→Region
        switch (CurrentMode)
        {
            case CaptureMode.None:
                CurrentMode = CaptureMode.Region;
                break;
            case CaptureMode.Region:
                CurrentMode = IsLastRegionAvailable
                    ? CaptureMode.LastRegion
                    : CaptureMode.Window;
                break;
            case CaptureMode.LastRegion:
                CurrentMode = CaptureMode.Window;
                break;
            case CaptureMode.Window:
                CurrentMode = CaptureMode.ColorPick;
                break;
            case CaptureMode.ColorPick:
                CurrentMode = CaptureMode.Region;
                break;
        }
    }

    public void SetMode(CaptureMode mode)
    {
        CurrentMode = mode;
    }
}
