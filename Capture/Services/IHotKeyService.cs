// author: eng-fe-desktop
// phase: engineering
// ADR-103: WH_KEYBOARD_LL 후크 — migration-mapping.md §6-1

namespace Capture.Services;

public interface IHotKeyService : IDisposable
{
    event EventHandler? PrintScreenPressed;
    event EventHandler? TabPressed;
    void Start();
    void Stop();
}
