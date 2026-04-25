// author: eng-fe-desktop
// phase: engineering
// ADR-103: WH_KEYBOARD_LL 후크. hookProc GC 핀 유지 필수.
// natural-fix: bitmask (flags & LLKHF_ALTDOWN) != 0 → flags == LLKHF_ALTDOWN (단순화 아닌 정확화)
//              실제로 Alt 키만 체크하므로 & 연산 후 != 0 로 유지 (원본 의도 보존)
// natural-fix: Dispatcher.InvokeAsync 로 UI 스레드 마샬링
// ADR-107: DllImport 유지 (LibraryImport 1차 대기)

using System.Runtime.InteropServices;
using System.Windows.Threading;
using Capture.Interop;

namespace Capture.Services;

public class HotKeyService : IHotKeyService
{
    private readonly Dispatcher _uiDispatcher;
    private IntPtr _hookHandle = IntPtr.Zero;
    private User32.HookProc? _hookProc; // GC 방지용 필드

    public event EventHandler? PrintScreenPressed;
    public event EventHandler? TabPressed;

    public HotKeyService(Dispatcher uiDispatcher)
    {
        _uiDispatcher = uiDispatcher;
    }

    public void Start()
    {
        if (_hookHandle != IntPtr.Zero) return;
        _hookProc = HookCallback;
        IntPtr hModule = Kernel32.LoadLibrary("user32");
        _hookHandle = User32.SetWindowsHookEx(
            NativeConstants.WH_KEYBOARD_LL,
            _hookProc,
            hModule,
            0);
    }

    public void Stop()
    {
        if (_hookHandle == IntPtr.Zero) return;
        User32.UnhookWindowsHookEx(_hookHandle);
        _hookHandle = IntPtr.Zero;
        _hookProc = null;
    }

    private int HookCallback(int code, int wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam == NativeConstants.WM_KEYDOWN || wParam == NativeConstants.WM_SYSKEYDOWN))
        {
            var khs = Marshal.PtrToStructure<KeyboardHookStruct>(lParam);
            // natural-fix: 원본 (e.KeyCode & Keys.Snapshot) == Keys.Snapshot → vkCode == VK_SNAPSHOT
            if (khs.vkCode == NativeConstants.VK_SNAPSHOT)
            {
                // natural-fix: Dispatcher.InvokeAsync 로 UI 스레드 마샬링
                _uiDispatcher.InvokeAsync(() => PrintScreenPressed?.Invoke(this, EventArgs.Empty));
            }
            else if (khs.vkCode == NativeConstants.VK_TAB
                     && (khs.flags & NativeConstants.LLKHF_ALTDOWN) != 0)
            {
                _uiDispatcher.InvokeAsync(() => TabPressed?.Invoke(this, EventArgs.Empty));
            }
        }
        return User32.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    public void Dispose()
    {
        Stop();
        GC.SuppressFinalize(this);
    }
}
