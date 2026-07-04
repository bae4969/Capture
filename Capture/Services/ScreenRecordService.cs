// author: eng-fe-desktop
// phase: engineering
// N차: 영역 화면 녹화 서비스 (무음 MP4/H264) — ScreenRecorderLib 래핑
// ADR 없음(신규 라이브러리, 사용자 승인 완료). ScreenRecorderLib 6.6.0 실물 API 기준.

using System.Drawing;
using ScreenRecorderLib;

namespace Capture.Services;

public class ScreenRecordService : IScreenRecordService
{
    private Recorder? _recorder;
    private bool _isRecording;

    public bool IsRecording => _isRecording;

    public event Action<string>? RecordingCompleted;
    public event Action<string>? RecordingFailed;

    public void Start(Rectangle region, string outputPath)
    {
        if (_isRecording) return;

        // 모든 디스플레이를 소스로 넣어 output canvas 가 virtual screen 전체를 덮게 한 뒤,
        // OutputOptions.SourceRect(= output 크롭)로 절대좌표 영역만 잘라낸다.
        // 단일 DisplayRecordingSource + source.SourceRect 는 그 모니터 로컬좌표라
        // cross-monitor 절대 rect 계약과 안 맞아 이 경로를 택함.
        var sources = new List<RecordingSourceBase>();
        foreach (var display in Recorder.GetDisplays())
            sources.Add(new DisplayRecordingSource(display.DeviceName));

        var options = new RecorderOptions
        {
            SourceOptions = new SourceOptions { RecordingSources = sources },
            OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                SourceRect = new ScreenRect(region.X, region.Y, region.Width, region.Height),
            },
            // 무음 스코프 — 오디오 캡처를 명시적으로 끈다(기본이 켜져 있음).
            AudioOptions = new AudioOptions { IsAudioEnabled = false },
            VideoEncoderOptions = new VideoEncoderOptions { Encoder = new H264VideoEncoder() },
        };

        _recorder = Recorder.CreateRecorder(options);
        _recorder.OnRecordingComplete += OnRecordingComplete;
        _recorder.OnRecordingFailed += OnRecordingFailed;

        _isRecording = true;
        _recorder.Record(outputPath);
    }

    public void Stop()
    {
        // 완료/실패 이벤트에서 실제 파일 마감·정리가 이뤄지므로 여기서는 정지 신호만 보낸다.
        _recorder?.Stop();
    }

    private void OnRecordingComplete(object? sender, RecordingCompleteEventArgs e)
    {
        _isRecording = false;
        CleanupRecorder();
        RecordingCompleted?.Invoke(e.FilePath);
    }

    private void OnRecordingFailed(object? sender, RecordingFailedEventArgs e)
    {
        _isRecording = false;
        CleanupRecorder();
        RecordingFailed?.Invoke(e.Error);
    }

    private void CleanupRecorder()
    {
        if (_recorder == null) return;
        _recorder.OnRecordingComplete -= OnRecordingComplete;
        _recorder.OnRecordingFailed -= OnRecordingFailed;
        _recorder.Dispose();
        _recorder = null;
    }

    public void Dispose()
    {
        // 앱 종료 시 녹화 중이면 파일이 손상되지 않도록 정지 시도.
        // Stop() 은 비동기 마감이라 완료 이벤트를 기다리지 않지만, 최소한 인코더에 정지를 알린다.
        if (_isRecording)
            _recorder?.Stop();
        CleanupRecorder();
    }
}
