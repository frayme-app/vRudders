using System.Diagnostics;

namespace VRudders;

internal sealed class InstanceGuard : IDisposable
{
    internal const string Name = @"Global\VRudders.YawForwarder";
    readonly Mutex mutex;
    InstanceGuard(Mutex mutex) => this.mutex = mutex;

    internal static InstanceGuard? TryAcquire(out string reason)
    {
        reason = "VRudders is already running. Use the existing window, or close it before starting this copy.";
        try
        {
            var mutex = new Mutex(true, Name, out bool created);
            if (!created) { mutex.Dispose(); return null; }
            // The original POC predates the mutex. Also detect that executable.
            foreach (var process in Process.GetProcessesByName("VRudders"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId) continue;
                    mutex.ReleaseMutex(); mutex.Dispose(); return null;
                }
            }
            return new InstanceGuard(mutex);
        }
        catch (UnauthorizedAccessException)
        {
            reason = "VRudders is already running in another Windows session. Close that copy first.";
            return null;
        }
    }

    public void Dispose() { mutex.ReleaseMutex(); mutex.Dispose(); }
}
