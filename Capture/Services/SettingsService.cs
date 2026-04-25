// author: eng-fe-desktop
// phase: engineering
// ADR-108: appsettings.json + ProtectedData (DPAPI) for SMTP password
// ADR-110: LegacySettingsImporter 별도 (여기서는 Load/Save 만)

using System.Drawing;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Capture.Services;

public class SettingsService : ISettingsService
{
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("CaptureIt-DPAPI-2024");

    private readonly string _settingsPath;

    // SMTP
    public string SmtpHost { get; set; } = string.Empty;
    public int SmtpPort { get; set; } = 25;
    public bool SmtpUseDefaultCredentials { get; set; } = true;
    public bool SmtpEnableSsl { get; set; } = false;
    public string EmailFrom { get; set; } = string.Empty;
    public string EmailTo { get; set; } = string.Empty;

    // Overlay
    public Color OverlayPenColor { get; set; } = Color.Black;

    // Auto save
    public string AutoSavePath { get; set; } = @"d:\";

    // 9차 추가: 히스토그램 토글 상태 persist (BL-006, ADR-301)
    public bool IsHistogramLogScale { get; set; } = false;
    // 11차: IsHistogramRgbMode (bool) → HistogramChannel (int 0~3) Gray/R/G/B
    public int HistogramChannel { get; set; } = 0;

    // DPAPI 암호화 저장용 (Base64)
    private string _encryptedPassword = string.Empty;

    public SettingsService()
    {
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string dir = Path.Combine(appData, "BitWiz", "Capture");
        Directory.CreateDirectory(dir);
        _settingsPath = Path.Combine(dir, "appsettings.json");
    }

    public string GetEmailPassword()
    {
        if (string.IsNullOrEmpty(_encryptedPassword)) return string.Empty;
        try
        {
            byte[] encrypted = Convert.FromBase64String(_encryptedPassword);
            byte[] plain = ProtectedData.Unprotect(encrypted, _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return string.Empty;
        }
    }

    public void SetEmailPassword(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            _encryptedPassword = string.Empty;
            return;
        }
        byte[] plain = Encoding.UTF8.GetBytes(plaintext);
        byte[] encrypted = ProtectedData.Protect(plain, _entropy, DataProtectionScope.CurrentUser);
        _encryptedPassword = Convert.ToBase64String(encrypted);
    }

    public void Load()
    {
        if (!File.Exists(_settingsPath)) return;
        try
        {
            string json = File.ReadAllText(_settingsPath);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("SmtpHost", out var smtpHost)) SmtpHost = smtpHost.GetString() ?? string.Empty;
            if (root.TryGetProperty("SmtpPort", out var smtpPort)) SmtpPort = smtpPort.GetInt32();
            if (root.TryGetProperty("SmtpUseDefaultCredentials", out var useDefault)) SmtpUseDefaultCredentials = useDefault.GetBoolean();
            if (root.TryGetProperty("SmtpEnableSsl", out var ssl)) SmtpEnableSsl = ssl.GetBoolean();
            if (root.TryGetProperty("EmailFrom", out var from)) EmailFrom = from.GetString() ?? string.Empty;
            if (root.TryGetProperty("EmailTo", out var to)) EmailTo = to.GetString() ?? string.Empty;
            if (root.TryGetProperty("EmailPasswordEncrypted", out var pw)) _encryptedPassword = pw.GetString() ?? string.Empty;
            if (root.TryGetProperty("AutoSavePath", out var savePath)) AutoSavePath = savePath.GetString() ?? @"d:\";
            if (root.TryGetProperty("OverlayPenColor", out var penColor))
            {
                int argb = penColor.GetInt32();
                OverlayPenColor = Color.FromArgb(argb);
            }
            if (root.TryGetProperty("IsHistogramLogScale", out var hLog)) IsHistogramLogScale = hLog.GetBoolean();
            // 11차: HistogramChannel (int) — 옛 IsHistogramRgbMode (bool) 키도 fallback (true=R/false=Gray)
            if (root.TryGetProperty("HistogramChannel", out var hCh))
            {
                int v = hCh.GetInt32();
                HistogramChannel = (v >= 0 && v <= 3) ? v : 0;
            }
            else if (root.TryGetProperty("IsHistogramRgbMode", out var hRgb))
            {
                HistogramChannel = hRgb.GetBoolean() ? 1 : 0; // R or Gray
            }
        }
        catch
        {
            // 로드 실패 시 기본값 유지
        }
    }

    public void Save()
    {
        var settings = new
        {
            SmtpHost,
            SmtpPort,
            SmtpUseDefaultCredentials,
            SmtpEnableSsl,
            EmailFrom,
            EmailTo,
            EmailPasswordEncrypted = _encryptedPassword,
            AutoSavePath,
            OverlayPenColor = OverlayPenColor.ToArgb(),
            IsHistogramLogScale,           // ← 9차 추가
            HistogramChannel               // ← 11차: int 0~3 (Gray/R/G/B)
        };
        string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_settingsPath, json);
    }
}
