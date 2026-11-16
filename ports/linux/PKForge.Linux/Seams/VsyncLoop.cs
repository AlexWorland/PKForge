namespace PKForge.App.Services;

// Stand-in for src/PKForge.App/Services/VsyncLoop.cs, which is built on Android's Choreographer.
// Linux has no vsync callback here, so the loop steps from a 16 ms dispatcher timer.
public sealed class VsyncLoop
{
    private readonly Func<float, bool> _step;
    private IDispatcherTimer? _timer;
    private long _last;

    public VsyncLoop(Func<float, bool> step) => _step = step;

    public bool IsRunning { get; private set; }

    public void Start()
    {
        if (IsRunning) return;
        var dispatcher = Application.Current?.Dispatcher ?? Dispatcher.GetForCurrentThread();
        if (dispatcher is null) return;
        IsRunning = true;
        _last = System.Diagnostics.Stopwatch.GetTimestamp();
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += OnTick;
        _timer.Start();
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        Release();
    }

    private void Release()
    {
        if (_timer is null) return;
        _timer.Stop();
        _timer.Tick -= OnTick;
        _timer = null;
    }

    private void OnTick(object? sender, EventArgs e)
    {
        if (!IsRunning) return;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = (float)System.Diagnostics.Stopwatch.GetElapsedTime(_last, now).TotalMilliseconds;
        elapsed = elapsed <= 0 ? 1000f / 60f : Math.Min(elapsed, 50f);
        _last = now;
        bool more;
        try { more = _step(elapsed); }
        catch (Exception error)
        {
            IsRunning = false;
            Release();
            AppLog.Error("frame", "An animation step failed; it was stopped", error);
            return;
        }
        if (!IsRunning) return; // the step stopped it
        if (!more)
        {
            IsRunning = false;
            Release();
        }
    }
}
