namespace PKForge.App.Services;

/// <summary>Linux stand-in for the Android Choreographer loop: a 16 ms dispatcher timer.</summary>
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
        IsRunning = true;
        _last = System.Diagnostics.Stopwatch.GetTimestamp();
        _timer = Application.Current!.Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, _) =>
        {
            if (!IsRunning) return;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            var ms = Math.Min((now - _last) * 1000f / System.Diagnostics.Stopwatch.Frequency, 50f);
            _last = now;
            bool more;
            try { more = _step(ms); } catch { more = false; }
            if (!more) Stop();
        };
        _timer.Start();
    }
    public void Stop() { IsRunning = false; _timer?.Stop(); _timer = null; }
}
