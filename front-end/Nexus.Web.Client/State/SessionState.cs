using Nexus.Web.Client.Models.Auth;

namespace Nexus.Web.Client.State;

/// <summary>
/// Holds the current session in-memory for the lifetime of the WASM app.
/// Notifications are raised by NexusAuthStateProvider; UI components subscribe here.
/// </summary>
public sealed class SessionState
{
    public SessionInfo? Current    { get; private set; }
    public bool         IsLoggedIn => Current is not null;

    public event Action? OnChange;

    public void Set(SessionInfo info)
    {
        Current = info;
        OnChange?.Invoke();
    }

    public void Clear()
    {
        Current = null;
        OnChange?.Invoke();
    }
}
