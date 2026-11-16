using PKForge.App.Services;

namespace PKForge.Linux.Core;

/// <summary>
/// Hold-repeat for the pad, with the Android app's timings (MainActivity): directions repeat after
/// 320 ms then every 80 ms, L and R after 380 ms then every 140 ms while the top screen pages with
/// them. One button repeats at a time, like the Android key timer. Not thread-safe: call it from
/// the main thread only (the host posts presses there and ticks from a dispatcher timer).
/// </summary>
public sealed class PadRepeater(Action<PadButton> fire, Func<bool> shouldersRepeat)
{
    private const long DirectionDelayMs = 320, DirectionRepeatMs = 80, ShoulderDelayMs = 380, ShoulderRepeatMs = 140;

    private PadButton? _held;
    private long _due;

    /// <summary>Fires the button now, and starts repeating it if it is repeatable.</summary>
    public void Press(PadButton button, long nowMs)
    {
        // Asked before firing: the press itself may open another screen (as MainActivity does).
        var repeats = IsDirectional(button) || (IsShoulder(button) && shouldersRepeat());
        _held = null;
        fire(button);
        if (!repeats) return;
        _held = button;
        _due = nowMs + (IsDirectional(button) ? DirectionDelayMs : ShoulderDelayMs);
    }

    public void Release(PadButton button)
    {
        if (_held == button) _held = null;
    }

    /// <summary>Drops any held repeat, for a controller that vanished without sending its button-up.</summary>
    public void Cancel() => _held = null;

    public void Tick(long nowMs)
    {
        if (_held is not { } button || nowMs < _due) return;
        if (IsShoulder(button) && !shouldersRepeat()) { _held = null; return; }
        var interval = IsDirectional(button) ? DirectionRepeatMs : ShoulderRepeatMs;
        // Stay on the original cadence, but never queue a burst after a stalled tick.
        _due = _due + interval > nowMs ? _due + interval : nowMs + interval;
        fire(button);
    }

    private static bool IsDirectional(PadButton b) => b is PadButton.Up or PadButton.Down or PadButton.Left or PadButton.Right;
    private static bool IsShoulder(PadButton b) => b is PadButton.L or PadButton.R;
}
