// author: eng-fe-desktop
// phase: engineering
// preserve: 구조체 레이아웃·필드 원본과 동일 (migration-mapping.md §9-3)

using System.Drawing;
using System.Runtime.InteropServices;

namespace Capture.Interop;

/// <summary>
/// Win32 구조체 — preserve 라벨 항목만 이식
/// drop: MouseLLHookStruct (GlobalMouseHook 이식 제외), SCROLLBARINFO/SCROLLINFO (호출처 없음)
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public Rectangle ToRectangle() =>
        new Rectangle(Left, Top, Right - Left, Bottom - Top);
}

/// <summary>WH_KEYBOARD_LL 후크 콜백 구조체 (ADR-103)</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct KeyboardHookStruct
{
    public int vkCode;
    public int scanCode;
    public int flags;
    public int time;
    public int dwExtraInfo;
}

[Serializable]
[StructLayout(LayoutKind.Sequential)]
internal struct WINDOWPLACEMENT
{
    public int length;
    public int flags;
    public ShowWindowCommands showCmd;
    public POINT ptMinPosition;
    public POINT ptMaxPosition;
    public RECT rcNormalPosition;
}

internal enum ShowWindowCommands
{
    Hide = 0,
    Normal = 1,
    Minimized = 2,
    Maximized = 3
}

[StructLayout(LayoutKind.Sequential)]
internal struct POINT
{
    public int X;
    public int Y;

    public POINT(int x, int y) { X = x; Y = y; }
    public Point ToPoint() => new Point(X, Y);
}
