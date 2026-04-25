// author: eng-fe-desktop
// phase: engineering

namespace Capture.Models;

/// <summary>
/// 캡처 요청 컨텍스트 — 트리거 시 생성
/// </summary>
public class CaptureRequest
{
    public CaptureMode Mode { get; init; }
    public DateTime RequestedAt { get; init; } = DateTime.Now;
}
