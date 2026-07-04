// author: eng-fe-desktop
// phase: engineering
// N차: 영역 화면 녹화 서비스 (무음 MP4/H264) — ScreenRecorderLib 래핑

using System.Drawing;

namespace Capture.Services;

public interface IScreenRecordService : IDisposable
{
    /// <summary>영역(virtual-screen 물리 픽셀 절대좌표) 녹화 시작 — outputPath 에 무음 MP4 기록</summary>
    void Start(Rectangle region, string outputPath);
    void Stop();
    bool IsRecording { get; }
    event Action<string>? RecordingCompleted;   // 성공 시 저장된 파일 경로
    event Action<string>? RecordingFailed;      // 실패 시 에러 메시지
}
