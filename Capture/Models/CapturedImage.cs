// author: eng-fe-desktop
// phase: engineering

using System.Drawing;
using System.Windows;

namespace Capture.Models;

/// <summary>
/// 캡처 결과 모델 — 비트맵·좌표·스크린 인덱스 포함
/// preserve: Bounds 1px 보정은 호출자(MainViewModel) 에서 적용 (migration-mapping.md §3-2)
/// </summary>
public class CapturedImage : IDisposable
{
    private bool _disposed;

    /// <summary>원본 System.Drawing.Bitmap (ADR-105: System.Drawing 유지)</summary>
    public System.Drawing.Bitmap? Bitmap { get; set; }

    /// <summary>결과창 Bounds (모니터 절대 좌표 + 1px 보정 포함)</summary>
    public System.Drawing.Rectangle Bounds { get; set; }

    /// <summary>캡처 영역 (모니터 내 상대 좌표)</summary>
    public System.Drawing.Rectangle BoundsCapture { get; set; }

    /// <summary>스크린 인덱스 (Screen.AllScreens 기준)</summary>
    public int IdxScreen { get; set; }

    /// <summary>파일에서 로드한 이미지 여부</summary>
    public bool LoadFromFile { get; set; }

    public void Dispose()
    {
        if (!_disposed)
        {
            Bitmap?.Dispose();
            Bitmap = null;
            _disposed = true;
        }
        GC.SuppressFinalize(this);
    }
}
