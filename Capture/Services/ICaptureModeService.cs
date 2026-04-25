// author: eng-fe-desktop
// phase: engineering
// preserve: CaptureMethod 도메인 상태 — migration-mapping.md §5-1

using Capture.Models;

namespace Capture.Services;

public interface ICaptureModeService
{
    CaptureMode CurrentMode { get; }
    System.Drawing.Rectangle LastCapturedRegion { get; set; }
    bool IsLastRegionAvailable { get; }
    void ToggleMode();
    void SetMode(CaptureMode mode);
}
