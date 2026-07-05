// author: eng-fe-desktop
// phase: engineering
// N차: 영역 화면 녹화 서비스 (무음 MP4/H264) — ScreenRecorderLib 래핑
// frame-based: 프레임(테두리) 기반 시작/일시정지/중단 상태머신 — State/StateChanged/Pause/Resume/StopAndWait 추가.
//   ScreenRecorderLib RecorderStatus 는 인터페이스에 노출하지 않고 자체 RecordingState 로 래핑(의존 격리).

using System.Drawing;

namespace Capture.Services;

/// <summary>
/// 녹화 서비스가 노출하는 상태 — ScreenRecorderLib RecorderStatus 를 래핑한 자체 enum.
/// UI/ViewModel 은 이 enum 만 참조해 라이브러리 타입에 결합되지 않는다.
/// </summary>
public enum RecordingState
{
    Idle,        // 녹화 안 함 (시작 전 또는 완료 후)
    Recording,   // 녹화 중
    Paused,      // 일시정지
    Finishing,   // 중단 신호 후 인코더가 파일을 마감하는 중
}

public interface IScreenRecordService : IDisposable
{
    /// <summary>영역(virtual-screen 물리 픽셀 절대좌표) 녹화 시작 — outputPath 에 무음 MP4 기록</summary>
    void Start(Rectangle region, string outputPath);
    void Stop();

    /// <summary>일시정지 — Recording 상태에서만 동작(그 외 무시).</summary>
    void Pause();
    /// <summary>재개 — Paused 상태에서만 동작(그 외 무시).</summary>
    void Resume();

    /// <summary>중단 신호를 보낸 뒤 완료 콜백(파일 마감)을 timeout 까지 대기. 데드락 방지용으로 무한대기 안 함.</summary>
    void StopAndWait(TimeSpan timeout);

    /// <summary>
    /// 녹화 중(Recording) 크롭 영역(SourceRect)을 실시간 이동한다. 출력 해상도가 시작 시 고정이라
    /// **위치 이동(같은 크기)만 왜곡 없이** 반영되고, 크기 변경은 왜곡되므로 호출측이 크기를 보존해야 한다.
    /// Recording 이 아니면 무시하고 false 반환.
    /// </summary>
    bool UpdateRegion(Rectangle region);

    bool IsRecording { get; }

    /// <summary>현재 녹화 상태(자체 enum).</summary>
    RecordingState State { get; }
    /// <summary>State 가 바뀔 때 발화 — 인코더 백그라운드 스레드에서 올 수 있어 구독자가 UI 마샬링 책임.</summary>
    event Action<RecordingState>? StateChanged;

    event Action<string>? RecordingCompleted;   // 성공 시 저장된 파일 경로
    event Action<string>? RecordingFailed;      // 실패 시 에러 메시지
}
