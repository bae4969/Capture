// author: eng-fe-desktop
// phase: engineering
// preserve: [Description] 추출 로직 원본과 동일 (namespace 변경만) — migration-mapping.md §10-4

using System.ComponentModel;

namespace Capture.Imaging;

public static class EnumEx
{
    public static string Description(Enum value)
    {
        if (value.GetType().GetField(value.ToString())
                 ?.GetCustomAttributes(typeof(DescriptionAttribute), inherit: false)
                 .SingleOrDefault() is DescriptionAttribute attr)
        {
            return attr.Description;
        }
        return value.ToString();
    }
}
