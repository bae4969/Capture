// author: eng-fe-desktop
// phase: engineering
// ADR-110: 원본 user.config 에서 1회 마이그레이션 — migration-mapping.md §13-1

using System.Drawing;
using System.IO;
using System.Xml.Linq;

namespace Capture.Services;

/// <summary>
/// ADR-110: .NET Framework ApplicationSettings(user.config) 에서
/// 신규 appsettings.json 으로 1회 마이그레이션.
/// 완료 후 마이그레이션 마커 파일을 생성해 재실행 방지.
/// </summary>
public class LegacySettingsImporter
{
    private readonly ISettingsService _settings;

    // 원본 ApplicationSettings 기반 user.config 경로 패턴
    // %LOCALAPPDATA%\BitWiz\CaptureIt\...\user.config
    private static readonly string _markerPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "BitWiz", "Capture", ".migrated_v1");

    public LegacySettingsImporter(ISettingsService settings)
    {
        _settings = settings;
    }

    public bool NeedsMigration => !File.Exists(_markerPath);

    public void TryMigrate()
    {
        if (!NeedsMigration) return;

        try
        {
            string? configPath = FindLegacyUserConfig();
            if (configPath != null)
            {
                ImportFromConfig(configPath);
                _settings.Save();
            }
        }
        catch
        {
            // 마이그레이션 실패 시 기본값으로 계속
        }
        finally
        {
            // 마커 생성 (성공 여부와 무관하게 1회만)
            File.WriteAllText(_markerPath, DateTime.UtcNow.ToString("O"));
        }
    }

    private static string? FindLegacyUserConfig()
    {
        // %LOCALAPPDATA%\BitWiz\CaptureIt_*..\user.config 패턴 탐색
        string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string legacyDir = Path.Combine(localApp, "BitWiz");
        if (!Directory.Exists(legacyDir)) return null;

        return Directory.GetFiles(legacyDir, "user.config", SearchOption.AllDirectories)
                         .FirstOrDefault();
    }

    private void ImportFromConfig(string configPath)
    {
        var doc = XDocument.Load(configPath);
        var settings = doc.Descendants("setting");

        foreach (var s in settings)
        {
            string name = s.Attribute("name")?.Value ?? string.Empty;
            string value = s.Element("value")?.Value ?? string.Empty;

            switch (name)
            {
                case "SMTP_HOST":
                    _settings.SmtpHost = value;
                    break;
                case "SMTP_PORT":
                    if (int.TryParse(value, out int port)) _settings.SmtpPort = port;
                    break;
                case "SMTP_USE_DEFAULT_CREDENTIALS":
                    if (bool.TryParse(value, out bool useDef)) _settings.SmtpUseDefaultCredentials = useDef;
                    break;
                case "SMTP_ENABLE_SSL":
                    if (bool.TryParse(value, out bool ssl)) _settings.SmtpEnableSsl = ssl;
                    break;
                case "EMAIL_FROM":
                    _settings.EmailFrom = value;
                    break;
                case "EMAIL_FROM_PW":
                    // natural-fix: 평문 비밀번호 → DPAPI 암호화 저장
                    if (!string.IsNullOrEmpty(value))
                        _settings.SetEmailPassword(value);
                    break;
                case "EMAIL_TO":
                    _settings.EmailTo = value;
                    break;
                case "PATH_AUTO_SAVE":
                    _settings.AutoSavePath = value;
                    break;
                case "OVERLAY_PEN_COLOR":
                    // 원본은 Color.Black — 직렬화된 경우 처리
                    if (int.TryParse(value, out int argb))
                        _settings.OverlayPenColor = Color.FromArgb(argb);
                    break;
            }
        }
    }
}
