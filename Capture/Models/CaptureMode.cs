// author: eng-fe-desktop
// phase: engineering
// preserve: enum 값·[Description] 텍스트 원본과 동일 (migration-mapping.md §2)

using System.ComponentModel;

namespace Capture.Models;

/// <summary>
/// 캡처 모드 열거형 — 원본 Capture.CaptureMethod 와 1:1 보존
/// </summary>
public enum CaptureMode
{
    [Description("")]
    None,

    [Description("Region Selection Mode")]
    Region,

    [Description("Last Region Mode")]
    LastRegion,

    [Description("Window Selection Mode")]
    Window,

    [Description("Color Picker Mode")]
    ColorPick
}
