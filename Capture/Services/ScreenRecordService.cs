// author: eng-fe-desktop
// phase: engineering
// N차: 영역 화면 녹화 서비스 (무음 MP4/H264) — ScreenRecorderLib 래핑
// ADR 없음(신규 라이브러리, 사용자 승인 완료). ScreenRecorderLib 6.6.0 실물 API 기준.
// frame-based: _isRecording bool → _state 상태머신. OnStatusChanged 로 RecorderStatus→RecordingState 매핑.
//   Pause/Resume/StopAndWait 추가. Stop 시 완료 콜백을 로컬 MRE 로 timeout 대기해 종료 데드락 회피(UI 무관).
// multi-monitor fix: SourceRect 를 virtual screen 절대좌표 그대로 넣던 것을 '캔버스 좌표'로 변환.
//   라이브러리는 소스 합집합의 좌상단을 (0,0) 으로 정규화한 캔버스를 만든다 — 주 모니터 왼쪽/위에
//   모니터가 있으면 가상 원점이 음수라 절대좌표가 그만큼 밀려 다른 모니터가 녹화됐다.
//   (실측: 3 FHD 에서 2번=주 모니터 영역이 왼쪽 1번 모니터로 크롭됨)

using System.Drawing;
using Capture.Interop;
using ScreenRecorderLib;

namespace Capture.Services;

public class ScreenRecordService : IScreenRecordService
{
    private Recorder? _recorder;
    private RecordingState _state = RecordingState.Idle;

    // 중단 완료(파일 마감)를 기다리기 위한 로컬 신호 — UI 스레드와 무관하게 서비스 내부에서만 사용.
    // StopAndWait 가 이 이벤트를 리셋→Stop→Wait 하고, 완료/실패 콜백이 Set 한다.
    private readonly System.Threading.ManualResetEventSlim _finished = new(false);

    // 캔버스 원점 = Start 시점 모니터 합집합의 좌상단(virtual screen 절대좌표).
    // 캔버스는 recorder 생성 시점에 고정되므로 녹화 중 재계산하지 않고 이 값을 계속 쓴다.
    private Point _canvasOrigin = Point.Empty;

    public bool IsRecording => _state == RecordingState.Recording || _state == RecordingState.Paused;
    public RecordingState State => _state;

    public event Action<RecordingState>? StateChanged;
    public event Action<string>? RecordingCompleted;
    public event Action<string>? RecordingFailed;

    public void Start(Rectangle region, string outputPath)
    {
        if (_state != RecordingState.Idle) return;

        // 모든 디스플레이를 소스로 넣어 output canvas 가 virtual screen 전체를 덮게 한 뒤,
        // OutputOptions.SourceRect(= output 크롭)로 그 영역만 잘라낸다.
        // 단일 DisplayRecordingSource + source.SourceRect 는 그 모니터 로컬좌표라
        // cross-monitor 절대 rect 계약과 안 맞아 이 경로를 택함.
        var monitors = DpiHelper.EnumMonitors();
        _canvasOrigin = new Point(
            monitors.Min(m => m.PhysicalBounds.Left),
            monitors.Min(m => m.PhysicalBounds.Top));

        // 각 소스의 캔버스 내 위치·크기를 명시 고정 — 캔버스 배치를 라이브러리 자동 배치에
        // 맡기지 않고 "캔버스 = virtual screen 을 원점만큼 평행이동한 것"을 계약으로 만든다.
        // 이 계약이 있어야 ToCanvasRect 의 좌표 변환이 성립한다.
        var sources = new List<RecordingSourceBase>();
        foreach (var display in Recorder.GetDisplays())
        {
            var source = new DisplayRecordingSource(display.DeviceName);
            var mon = monitors.FirstOrDefault(m =>
                string.Equals(m.DeviceName, display.DeviceName, StringComparison.OrdinalIgnoreCase));
            // 매칭 실패(장치명 불일치)면 위치를 지정하지 않고 라이브러리 자동 배치에 맡긴다.
            if (mon.PhysicalBounds.Width > 0)
            {
                source.Position = new ScreenPoint(
                    mon.PhysicalBounds.Left - _canvasOrigin.X,
                    mon.PhysicalBounds.Top - _canvasOrigin.Y);
                source.OutputSize = new ScreenSize(
                    mon.PhysicalBounds.Width, mon.PhysicalBounds.Height);
            }
            sources.Add(source);
        }

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = sources },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                SourceRect = ToCanvasRect(region),
            },
            // 무음 스코프 — 오디오 캡처를 명시적으로 끈다(기본이 켜져 있음).
            AudioOptions = new AudioOptions { IsAudioEnabled = false },
            VideoEncoderOptions = new VideoEncoderOptions { Encoder = new H264VideoEncoder() },
        };

        _recorder = Recorder.CreateRecorder(options);
        _recorder.OnRecordingComplete += OnRecordingComplete;
        _recorder.OnRecordingFailed += OnRecordingFailed;
        // 라이브러리가 보고하는 실제 상태(Recording/Paused/Finishing)를 자체 enum 으로 반영.
        _recorder.OnStatusChanged += OnStatusChanged;

        _finished.Reset();
        // 낙관적으로 Recording 으로 전환 — OnStatusChanged 가 곧 같은 상태를 확정한다.
        SetState(RecordingState.Recording);
        _recorder.Record(outputPath);
    }

    public void Stop()
    {
        // 완료/실패 이벤트에서 실제 파일 마감·정리가 이뤄지므로 여기서는 정지 신호만 보낸다.
        _recorder?.Stop();
    }

    public void Pause()
    {
        // Recording 상태에서만 유효. 방어적으로 recorder null·상태 가드.
        if (_state != RecordingState.Recording) return;
        _recorder?.Pause();
    }

    public void Resume()
    {
        if (_state != RecordingState.Paused) return;
        _recorder?.Resume();
    }

    public void StopAndWait(TimeSpan timeout)
    {
        // 이미 Idle 이면 대기할 것이 없다.
        if (_state == RecordingState.Idle) return;

        _finished.Reset();
        _recorder?.Stop();
        // 완료/실패 콜백이 _finished 를 Set 할 때까지 timeout 까지만 대기 — 무한대기 금지.
        // 이 메서드는 UI 스레드를 만지지 않으므로(백그라운드 Task 에서 호출), 완료 콜백의
        // UI 마샬링과 충돌하지 않는다(종료 시 ViewModel 이 UI 마샬링을 스킵).
        _finished.Wait(timeout);
    }

    public bool UpdateRegion(Rectangle region)
    {
        // 녹화 중(Recording)에만 유효. GetDynamicOptionsBuilder 로 SourceRect(output 크롭)를 즉시 갱신한다.
        // SetOptions 는 !IsRecording 가드가 있어 녹화 중엔 못 쓰므로 DynamicOptionsBuilder 경로가 유일.
        // Start 와 동일한 캔버스 좌표 변환 — 크기는 시작 시 고정 출력 해상도라 위치만 이동해야 왜곡이 없다.
        if (_state != RecordingState.Recording || _recorder == null) return false;

        return _recorder
            .GetDynamicOptionsBuilder()
            .SetDynamicOutputOptions(new DynamicOutputOptions
            {
                SourceRect = ToCanvasRect(region),
            })
            .Apply();
    }

    // virtual screen 절대 rect → 캔버스 rect. 캔버스 원점(합집합 좌상단)만큼 평행이동한다.
    // 원점이 (0,0) 인 배치(주 모니터가 가장 왼쪽·위)에서는 항등 변환이라 기존 동작과 같다.
    private ScreenRect ToCanvasRect(Rectangle region) => new(
        region.X - _canvasOrigin.X,
        region.Y - _canvasOrigin.Y,
        region.Width,
        region.Height);

    // RecorderStatus(라이브러리) → RecordingState(자체) 매핑. 인코더 백그라운드 스레드에서 올 수 있어
    // 여기서는 UI 를 절대 만지지 않고 상태 전이·이벤트 발화만 한다(구독자가 UI 마샬링 책임).
    private void OnStatusChanged(object? sender, RecordingStatusEventArgs e)
    {
        var mapped = e.Status switch
        {
            RecorderStatus.Idle => RecordingState.Idle,
            RecorderStatus.Recording => RecordingState.Recording,
            RecorderStatus.Paused => RecordingState.Paused,
            RecorderStatus.Finishing => RecordingState.Finishing,
            _ => _state,
        };
        SetState(mapped);
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        SetState(RecordingState.Idle);
        DetachAndDeferDispose();
        _finished.Set();
        RecordingCompleted?.Invoke(e.FilePath);
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        SetState(RecordingState.Idle);
        DetachAndDeferDispose();
        _finished.Set();
        RecordingFailed?.Invoke(e.Error);
    }

    private void SetState(RecordingState next)
    {
        if (_state == next) return;
        _state = next;
        StateChanged?.Invoke(next);
    }

    // 완료/실패 콜백은 네이티브 m_RecordTask continuation 위에서 '같은 스레드'로 동기 실행된다
    // (ScreenRecorderLib #367). 그 콜스택에서 Recorder.Dispose() 를 부르면 ~RecordingManager 가
    // 아직 끝나지 않은 자기 태스크를 m_RecordTask.wait() 로 기다리는 self-join 교착에 빠져 완료 처리가
    // 멈춘다(→ Finishing 고착·앱 freeze). 구독 해제·참조 null 은 콜백 안에서 안전하지만, Dispose 만
    // 콜스택 밖(풀 스레드)으로 지연해 교착을 피한다.
    private void DetachAndDeferDispose()
    {
        var rec = _recorder;
        if (rec == null) return;
        rec.OnRecordingComplete -= OnRecordingComplete;
        rec.OnRecordingFailed -= OnRecordingFailed;
        rec.OnStatusChanged -= OnStatusChanged;
        _recorder = null;
        System.Threading.Tasks.Task.Run(() => rec.Dispose());
    }

    private void CleanupRecorder()
    {
        if (_recorder == null) return;
        _recorder.OnRecordingComplete -= OnRecordingComplete;
        _recorder.OnRecordingFailed -= OnRecordingFailed;
        _recorder.OnStatusChanged -= OnStatusChanged;
        _recorder.Dispose();
        _recorder = null;
    }

    public void Dispose()
    {
        // 앱 종료 시 녹화 중이면 파일이 손상되지 않도록 완료를 timeout 대기 후 정리.
        // StopAndWait 가 완료 콜백에서 CleanupRecorder 를 이미 수행하지만, 타임아웃/실패 경로를 위해
        // 아래에서 한 번 더 멱등 정리한다.
        if (_state != RecordingState.Idle)
            StopAndWait(TimeSpan.FromSeconds(5));
        CleanupRecorder();
        _finished.Dispose();
    }
}
