// author: eng-fe-desktop
// phase: engineering
// ADR-108: appsettings.json + DPAPI (ProtectedData) — migration-mapping.md §8-1

using System.Drawing;

namespace Capture.Services;

public interface ISettingsService
{
    // SMTP
    string SmtpHost { get; set; }
    int SmtpPort { get; set; }
    bool SmtpUseDefaultCredentials { get; set; }
    bool SmtpEnableSsl { get; set; }
    string EmailFrom { get; set; }
    string EmailTo { get; set; }

    // natural-fix: 평문 → DPAPI 암호화 저장/로드
    string GetEmailPassword();
    void SetEmailPassword(string plaintext);

    // Overlay
    Color OverlayPenColor { get; set; }

    // Auto save
    string AutoSavePath { get; set; }

    // 9차 추가: 히스토그램 토글 상태 persist (BL-006, ADR-301)
    bool IsHistogramLogScale { get; set; }
    // 11차: IsHistogramRgbMode (bool) → HistogramChannel (int 0~3) Gray/R/G/B 순환
    int HistogramChannel { get; set; }

    void Load();
    void Save();
}
