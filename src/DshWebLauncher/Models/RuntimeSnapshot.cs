namespace DshWebLauncher.Models;

public enum DshRuntimeState
{
    Stopped,
    Starting,
    Running,
    External,
    Stopping,
    Faulted,
    StopFailed
}

public sealed record RuntimeSnapshot(
    DshRuntimeState State,
    bool IsReachable,
    int? ProcessId,
    long WorkingSetBytes,
    TimeSpan? Uptime,
    string Detail,
    Uri WebUri)
{
    public bool IsRunning => State is DshRuntimeState.Running or DshRuntimeState.External;
    public bool IsManaged => ProcessId is not null;
}
