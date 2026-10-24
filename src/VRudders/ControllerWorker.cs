using System.Runtime.ExceptionServices;

namespace VRudders;

// A separate STA/message loop preserves DirectInput notifications without depending
// on the main form's timer, visibility, focus, resizing, or a modal window.
internal sealed class ControllerWorker : IDisposable
{
    readonly Thread thread;
    readonly ForwardingSession session;
    Control? dispatcher;
    bool disposed;

    public ControllerWorker(ControllerBackend controllers)
    {
        session = new(controllers);
        using var ready = new ManualResetEventSlim();
        Exception? startupError = null;
        bool initialized = false;
        thread = new Thread(() =>
        {
            try
            {
                using var control = new Control();
                _ = control.Handle; dispatcher = control;
                using var timer = new System.Windows.Forms.Timer { Interval = 16 };
                timer.Tick += (_, _) => session.Tick();
                timer.Start(); initialized = true; ready.Set();
                Application.Run();
            }
            catch (Exception ex)
            {
                // Only initialization owns the ready signal. It is disposed once
                // the constructor returns, while this thread keeps running.
                if (!initialized) { startupError = ex; ready.Set(); }
                else session.Fail("Forwarding thread stopped: " + ex.Message);
            }
            finally { session.Dispose(); }
        }) { IsBackground = true, Name = "VRudders input and yaw" };
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); ready.Wait();
        if (startupError != null) { thread.Join(); ExceptionDispatchInfo.Capture(startupError).Throw(); }
    }

    public ForwardingSnapshot Snapshot => session.Snapshot;
    public bool IsAlive => thread.IsAlive && !disposed;
    public void Invoke(Action<ForwardingSession> action)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (!thread.IsAlive) throw new IOException("The forwarding thread stopped. Exit and restart VRudders.");
        dispatcher!.Invoke(() => action(session));
    }
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (thread.IsAlive)
        {
            dispatcher!.Invoke(() => { session.Dispose(); Application.ExitThread(); });
            thread.Join();
        }
    }
}
