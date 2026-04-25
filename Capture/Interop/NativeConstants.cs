// author: eng-fe-desktop
// phase: engineering
// preserve: 상수값 원본과 동일 (migration-mapping.md §9)

namespace Capture.Interop;

/// <summary>
/// Win32 상수 — User32Dll.cs 에서 preserve 라벨 항목만 이식
/// </summary>
internal static class NativeConstants
{
    // Hook types
    public const int WH_KEYBOARD_LL = 13;   // ADR-103: WH_KEYBOARD_LL 후크 유지
    public const int WH_MOUSE_LL = 14;

    // Window messages
    public const int WM_NCLBUTTONDOWN = 161; // HT_CAPTION 과 함께 드래그 이동에 사용
    public const int WM_KEYDOWN = 256;
    public const int WM_KEYUP = 257;
    public const int WM_SYSKEYDOWN = 260;
    public const int WM_SYSKEYUP = 261;

    // Hit test values
    public const int HT_CAPTION = 2;

    // Keyboard flags
    public const int LLKHF_ALTDOWN = 32;   // flags & 0x20 — Alt 모디파이어

    // Virtual keys
    public const int VK_SNAPSHOT = 0x2C;    // Print Screen
    public const int VK_TAB = 0x09;

    // GetWindow commands
    public const uint GW_HWNDNEXT = 2;

    // GetSystemMetrics indices
    public const int SM_CXFULLSCREEN = 16;
    public const int SM_CYFULLSCREEN = 17;

    // ChildWindowFromPointEx flags
    public const uint CWP_ALL = 0;

    // Layered Window (6차 fix — SetLayeredWindowAttributes 알파 직접 제어)
    public const int  GWL_EXSTYLE   = -20;
    public const int  WS_EX_LAYERED = 0x80000;
    public const uint LWA_ALPHA     = 0x2;
}
