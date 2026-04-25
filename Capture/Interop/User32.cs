// author: eng-fe-desktop
// phase: engineering
// preserve: 실제 호출되는 함수만 이식 (migration-mapping.md §9-1)
// drop: SetActiveWindow·WindowFromPoint·GetWindowDC·GetClassName·ReleaseDC·
//        GetScrollBarInfo·GetScrollInfo·mouse_event·SendMessage 일부 오버로드·
//        GDI 중첩 클래스 (gdi32, DEAD CaptureScreen 경유 의도) — migration-mapping.md §9-2
// ADR-107: 1차는 DllImport 유지. 후속 사이클에서 LibraryImport 전환.
// ADR-401: LibraryImport 적용 (변환 19 + fallback 3, partial method)

using System.Collections;
using System.Drawing;
using System.Runtime.InteropServices;

namespace Capture.Interop;

internal static partial class User32
{
    // ── 후크 (ADR-103: WH_KEYBOARD_LL 유지) ─────────────────────────────────

    public delegate int HookProc(int code, int wParam, IntPtr lParam);

    // ── #1 fallback (managed delegate marshalling — ADR-401 §Decision §2) ──
    // HookProc 는 캡처 보유 managed delegate. LibraryImport SourceGenerator 가
    // managed delegate 를 unmanaged function pointer 로 자동 변환하지 못함.
    // HotKeyService._hookProc GC pinning 패턴 보존 필수 (ADR-401 Consequences §델리게이트 GC pinning)
#pragma warning disable SYSLIB1054 // ADR-401 fallback: managed delegate marshalling
    [DllImport("user32.dll")]
    public static extern IntPtr SetWindowsHookEx(int idHook, HookProc callback, IntPtr hInstance, uint threadId);
#pragma warning restore SYSLIB1054

    // ── #2 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UnhookWindowsHookEx(IntPtr hInstance);

    // ── #3 변환 ──
    [LibraryImport("user32.dll")]
    public static partial int CallNextHookEx(IntPtr idHook, int nCode, int wParam, IntPtr lParam);

    // ── 창 열거 (WindowEnumService) ──────────────────────────────────────────

    public delegate bool EnumWindowsProc(IntPtr hWnd, int lParam);

    // ── #4 fallback (managed delegate marshalling — ADR-401 §Decision §2) ──
    // EnumWindowsProc 는 managed delegate. 동일 사유로 fallback 유지.
#pragma warning disable SYSLIB1054 // ADR-401 fallback: managed delegate marshalling
    [DllImport("user32.dll")]
    public static extern int EnumWindows(EnumWindowsProc ewp, int lParam);
#pragma warning restore SYSLIB1054

    // ── #5 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hWnd);

    // ── #6 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hWnd);

    // ── #7 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hWnd, ref RECT lpRect);

    // ── #8 변환 (SetLastError + 기존 [return:MarshalAs(Bool)] 보존) ──
    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowPlacement(IntPtr hWnd, ref WINDOWPLACEMENT lpwndpl);

    // ── 자식 창 탐색 (Window 모드) ───────────────────────────────────────────

    // ── #9 fallback (System.Drawing.Point non-blittable — SYSLIB1051) ──
    // LibraryImport SourceGenerator 가 System.Drawing.Point 를 blittable 로 처리하지 못함.
    // 시그니처 호환 유지(public Point 인자) 위해 DllImport fallback 보존.
    // (ADR-401 정정 항목 — handoff-return-to-eng-lead-tech.md 참조)
#pragma warning disable SYSLIB1054 // ADR-401 fallback: System.Drawing.Point non-blittable (SYSLIB1051)
    [DllImport("user32.dll")]
    public static extern bool ScreenToClient(IntPtr handle, ref Point point);
#pragma warning restore SYSLIB1054

    // ── #10 fallback (System.Drawing.Point non-blittable — SYSLIB1051) ──
#pragma warning disable SYSLIB1054 // ADR-401 fallback: System.Drawing.Point non-blittable (SYSLIB1051)
    [DllImport("user32.dll")]
    public static extern IntPtr ChildWindowFromPointEx(IntPtr hWndParent, Point pt, uint uFlags);
#pragma warning restore SYSLIB1054

    // ── #11 fallback (System.Drawing.Point non-blittable — SYSLIB1051) ──
#pragma warning disable SYSLIB1054 // ADR-401 fallback: System.Drawing.Point non-blittable (SYSLIB1051)
    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hwnd, ref Point lpPoint);
#pragma warning restore SYSLIB1054

    // ── #12 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsChild(IntPtr hWndParent, IntPtr hWnd);

    // ── #13 변환 ──
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetParent(IntPtr hWnd);

    // ── #14 변환 ──
    [LibraryImport("user32.dll")]
    public static partial IntPtr GetWindow(IntPtr hWnd, uint uCmd);

    // ── #15 변환 ──
    [LibraryImport("user32.dll")]
    public static partial int GetSystemMetrics(int smIndex);

    // ── 커서 제어 (RequestCaptureWindow 화살표 키) ──────────────────────────

    // ── #16 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetCursorPos(int X, int Y);

    // ── #17 fallback (System.Drawing.Point non-blittable — SYSLIB1051) ──
    // out Point: LibraryImport SourceGenerator 가 System.Drawing.Point 를 처리하지 못함.
    // 기존 [return:MarshalAs(Bool)] 보존. 시그니처 호환 유지.
#pragma warning disable SYSLIB1054 // ADR-401 fallback: System.Drawing.Point non-blittable (SYSLIB1051)
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out Point lpPoint);
#pragma warning restore SYSLIB1054

    // ── 창 드래그 (CapturedWindow MouseDown, HistogramWindow MouseDown) ──────

    // ── #18 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    // ── Layered Window 알파 직접 제어 (6차 fix — SetLayeredWindowAttributes) ─

    // ── #19 변환 (SetLastError 보존) ──
    // EntryPoint 명시: SendMessage 와 동일 사유 — W/A 자동 매핑 없음.
    [LibraryImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    public static partial int GetWindowLong(IntPtr hWnd, int nIndex);

    // ── #20 변환 ──
    [LibraryImport("user32.dll", EntryPoint = "SetWindowLongW")]
    public static partial int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    // ── #21 변환 ──
    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);

    // ── #22 변환 ──
    // EntryPoint 명시 필요: LibraryImport 는 DllImport CharSet.Auto 의 자동 W/A 매핑을 수행하지 않음.
    // SendMessage 는 OS 에 SendMessageW/SendMessageA 만 존재 → "SendMessage" 직 lookup 시 EntryPointNotFoundException.
    [LibraryImport("user32.dll", EntryPoint = "SendMessageW")]
    public static partial int SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

    // ── 고수준 헬퍼 (원본 User32Dll 의 static 메서드 이식) ──────────────────

    private static readonly List<IntPtr> _visibleWindowHandleList = new();
    private static readonly List<Rectangle> _visibleWindowRectList = new();

    public static void UpdateVisibleWindowList()
    {
        _visibleWindowHandleList.Clear();
        _visibleWindowRectList.Clear();
        EnumWindows(EvalWindow, 0);
    }

    public static Rectangle GetWindowRectFromPoint(Point point)
    {
        for (int i = 0; i < _visibleWindowHandleList.Count; i++)
        {
            if (_visibleWindowRectList[i].Contains(point))
            {
                IntPtr hWnd = _visibleWindowHandleList[i];
                IntPtr prev;
                do
                {
                    prev = hWnd;
                    hWnd = ChildWindow(prev, point);
                }
                while (prev != hWnd);
                return GetWindowRectSafe(hWnd);
            }
        }
        return default;
    }

    public static Rectangle GetWindowRectSafe(IntPtr hWnd)
    {
        if (hWnd == IntPtr.Zero) return default;
        RECT lpRect = default;
        if (!GetWindowRect(hWnd, ref lpRect))
            return default;

        WINDOWPLACEMENT placement = GetPlacement(hWnd);
        if (placement.showCmd == ShowWindowCommands.Maximized)
        {
            // preserve: 원본 최대화 창 8px 보정 동일
            lpRect.Top += 8;
            lpRect.Left += 8;
            lpRect.Right -= 8;
            lpRect.Bottom -= 8;
        }
        return lpRect.ToRectangle();
    }

    public static WINDOWPLACEMENT GetPlacement(IntPtr hwnd)
    {
        WINDOWPLACEMENT lpwndpl = default;
        lpwndpl.length = Marshal.SizeOf(lpwndpl);
        GetWindowPlacement(hwnd, ref lpwndpl);
        return lpwndpl;
    }

    private static IntPtr ChildWindow(IntPtr hWnd, Point point)
    {
        if (!ScreenToClient(hWnd, ref point)) return IntPtr.Zero;
        IntPtr child = ChildWindowFromPointEx(hWnd, point, 0u);
        if (child == IntPtr.Zero) return hWnd;
        if (!ClientToScreen(hWnd, ref point)) return IntPtr.Zero;
        if (!IsChild(GetParent(child), child)) return child;

        ArrayList list = new();
        IntPtr cur = child;
        while (cur != IntPtr.Zero)
        {
            if (GetWindowRectSafe(cur).Contains(point))
                list.Add(cur);
            cur = GetWindow(cur, NativeConstants.GW_HWNDNEXT);
        }

        int minArea = GetSystemMetrics(NativeConstants.SM_CXFULLSCREEN)
                    * GetSystemMetrics(NativeConstants.SM_CYFULLSCREEN);
        IntPtr result = child;
        foreach (IntPtr h in list)
        {
            Rectangle r = GetWindowRectSafe(h);
            int area = r.Width * r.Height;
            if (area < minArea)
            {
                minArea = area;
                result = h;
            }
        }
        return result;
    }

    private static bool EvalWindow(IntPtr hWnd, int lParam)
    {
        if (IsWindowVisible(hWnd) && !IsIconic(hWnd))
        {
            Rectangle rect = GetWindowRectSafe(hWnd);
            if (rect.Width > 0 && rect.Height > 0)
            {
                _visibleWindowHandleList.Add(hWnd);
                _visibleWindowRectList.Add(rect);
            }
        }
        return true;
    }
}
