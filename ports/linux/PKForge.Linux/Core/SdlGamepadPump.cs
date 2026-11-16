using System.Runtime.InteropServices;

namespace PKForge.Linux.Core;

/// <summary>
/// Reads game controllers through SDL2. The handheld's built-in pad is a joystick device that the
/// window system never delivers to the app, so a background thread polls SDL and hands button and
/// stick changes to the main thread through <paramref name="postToMainThread"/>.
/// </summary>
public sealed class SdlGamepadPump(
    Action<Action> postToMainThread,
    Action<SdlButton, bool> onButton,
    Action<float, float, float, float> onSticks,
    string libraryName = "libSDL2-2.0.so.0",
    Action<string>? log = null)
{
    private const uint InitGameController = 0x00002000;
    private const uint EventQuit = 0x100, ControllerAxisMotion = 0x650, ControllerButtonDown = 0x651,
        ControllerButtonUp = 0x652, ControllerDeviceAdded = 0x653, ControllerDeviceRemoved = 0x654;
    private const float DeadZone = 0.2f;

    private delegate int InitDelegate(uint flags);
    private delegate void QuitDelegate();
    private delegate int SetHintDelegate(string name, string value);
    private delegate IntPtr GetErrorDelegate();
    private delegate int WaitEventTimeoutDelegate(byte[] evt, int timeoutMs);
    private delegate IntPtr ControllerOpenDelegate(int index);
    private delegate void ControllerCloseDelegate(IntPtr controller);
    private delegate int NumJoysticksDelegate();
    private delegate int IsControllerDelegate(int index);
    private delegate IntPtr ControllerGetJoystickDelegate(IntPtr controller);
    private delegate int JoystickInstanceIdDelegate(IntPtr joystick);

    private IntPtr _lib;
    private QuitDelegate? _quit;
    private WaitEventTimeoutDelegate? _wait;
    private ControllerOpenDelegate? _open;
    private ControllerCloseDelegate? _close;
    private ControllerGetJoystickDelegate? _getJoystick;
    private JoystickInstanceIdDelegate? _instanceId;
    private Thread? _thread;
    private volatile bool _stopping;

    // Touched only on the pump thread.
    private readonly Dictionary<int, IntPtr> _controllers = [];
    private readonly Dictionary<int, HashSet<SdlButton>> _held = [];
    private readonly float[] _axes = new float[4];

    /// <summary>Why <see cref="Start"/> returned false, or null.</summary>
    public string? UnavailableReason { get; private set; }

    public bool Start()
    {
        if (_thread is not null) return true;
        try
        {
            if (!NativeLibrary.TryLoad(libraryName, out _lib))
                return Unavailable($"{libraryName} could not be loaded");
            var init = Bind<InitDelegate>("SDL_Init");
            var setHint = Bind<SetHintDelegate>("SDL_SetHint");
            var getError = Bind<GetErrorDelegate>("SDL_GetError");
            _quit = Bind<QuitDelegate>("SDL_Quit");
            _wait = Bind<WaitEventTimeoutDelegate>("SDL_WaitEventTimeout");
            _open = Bind<ControllerOpenDelegate>("SDL_GameControllerOpen");
            _close = Bind<ControllerCloseDelegate>("SDL_GameControllerClose");
            _getJoystick = Bind<ControllerGetJoystickDelegate>("SDL_GameControllerGetJoystick");
            _instanceId = Bind<JoystickInstanceIdDelegate>("SDL_JoystickInstanceID");
            var numJoysticks = Bind<NumJoysticksDelegate>("SDL_NumJoysticks");
            var isController = Bind<IsControllerDelegate>("SDL_IsGameController");

            // Without this SDL drops joystick events while the window is not focused (it never is on a bare handheld session).
            setHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (init(InitGameController) != 0)
            {
                var error = Marshal.PtrToStringUTF8(getError());
                return Unavailable($"SDL_Init failed: {error}");
            }
            // Pads present before the first event loop turn arrive as device-added events too, but
            // opening here makes the count in the log meaningful.
            var present = 0;
            for (var i = 0; i < numJoysticks(); i++)
                if (isController(i) != 0 && Open(i)) present++;
            log?.Invoke($"SDL2 ready, {present} controller(s) open");
        }
        catch (Exception ex)
        {
            return Unavailable($"{ex.GetType().Name}: {ex.Message}");
        }
        _stopping = false;
        _thread = new Thread(Run) { IsBackground = true, Name = "sdl-gamepad" };
        _thread.Start();
        return true;
    }

    /// <summary>Ends the poll thread and shuts SDL down.</summary>
    public void Stop()
    {
        var thread = _thread;
        if (thread is null) return;
        _stopping = true;
        thread.Join();
        _thread = null;
        _quit?.Invoke();
    }

    private bool Unavailable(string reason)
    {
        UnavailableReason = reason;
        log?.Invoke($"SDL gamepad unavailable: {reason}");
        return false;
    }

    private T Bind<T>(string name) where T : Delegate =>
        Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(_lib, name));

    private bool Open(int deviceIndex)
    {
        var controller = _open!(deviceIndex);
        if (controller == IntPtr.Zero) return false;
        var id = _instanceId!(_getJoystick!(controller));
        if (_controllers.TryGetValue(id, out var existing) && existing != IntPtr.Zero)
        {
            _close!(controller);
            return true;
        }
        _controllers[id] = controller;
        return true;
    }

    // SDL_Event is a 56-byte union; every layout starts with the uint type.
    private void Run()
    {
        var evt = new byte[56];
        while (!_stopping)
        {
            if (_wait!(evt, 100) == 0) continue;
            switch (BitConverter.ToUInt32(evt, 0))
            {
                case ControllerDeviceAdded:
                    Open(BitConverter.ToInt32(evt, 8));
                    break;
                case ControllerDeviceRemoved:
                    Remove(BitConverter.ToInt32(evt, 8));
                    break;
                case ControllerButtonDown:
                case ControllerButtonUp:
                    Button(BitConverter.ToInt32(evt, 8), (SdlButton)evt[12], BitConverter.ToUInt32(evt, 0) == ControllerButtonDown);
                    break;
                case ControllerAxisMotion:
                    Axis(evt[12], BitConverter.ToInt16(evt, 16));
                    break;
            }
        }
        foreach (var controller in _controllers.Values) _close!(controller);
        _controllers.Clear();
    }

    private void Button(int instance, SdlButton button, bool down)
    {
        if (!_held.TryGetValue(instance, out var set)) _held[instance] = set = [];
        if (down ? !set.Add(button) : !set.Remove(button)) return;
        postToMainThread(() => onButton(button, down));
    }

    private void Axis(byte axis, short raw)
    {
        if (axis > 3) return;   // triggers are not used
        var value = Math.Clamp(raw / 32767f, -1f, 1f);
        _axes[axis] = Math.Abs(value) < DeadZone ? 0f : value;
        PostSticks();
    }

    private void PostSticks()
    {
        float lx = _axes[0], ly = _axes[1], rx = _axes[2], ry = _axes[3];
        postToMainThread(() => onSticks(lx, ly, rx, ry));
    }

    // A removed pad never sends its button-ups: release what it held, and centre the sticks.
    private void Remove(int instance)
    {
        if (_controllers.Remove(instance, out var controller)) _close!(controller);
        if (_held.Remove(instance, out var set))
            foreach (var button in set)
            {
                var b = button;
                postToMainThread(() => onButton(b, false));
            }
        if (_controllers.Count == 0)
        {
            Array.Clear(_axes);
            PostSticks();
        }
    }
}
