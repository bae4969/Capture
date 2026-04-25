// author: eng-fe-desktop
// phase: engineering
// preserve: LoadLibrary — WH_KEYBOARD_LL 후크의 hInstance 획득에 사용 (ADR-103)
// ADR-401: LibraryImport 적용 (LoadLibrary, StringMarshalling.Utf16)

using System.Runtime.InteropServices;

namespace Capture.Interop;

internal static partial class Kernel32
{
    // ADR-107: 1차는 DllImport 유지, 후속 사이클에서 LibraryImport 전환 ADR 추가
    // ADR-401: LibraryImport 변환 — CharSet.Auto = LPWStr 동등, EntryPoint 명시로 W API 직접 지정
    // StringMarshalling.Utf16 + EntryPoint="LoadLibraryW" : CharSet.Auto(=LPWStr) 의 LibraryImport 동등 표현
    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, EntryPoint = "LoadLibraryW")]
    public static partial IntPtr LoadLibrary(string lpFileName);
}
