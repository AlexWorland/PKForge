#pragma warning disable CA1822, IDE0060, CS8981
// Compile-only stand-ins for the Android and Java types the shared app sources name. They have no
// behaviour: the code paths that use them run only when a native Android view or preference store
// exists, which never happens on Linux. They exist so the shared files compile unchanged.

namespace Java.Lang
{
    public class Exception : System.Exception
    {
        public Exception() { }
        public Exception(string? message) : base(message) { }
    }

    public sealed class String
    {
        public override string ToString() => string.Empty;
    }

    public sealed class Boolean
    {
        public bool BooleanValue() => false;
    }

    public sealed class Integer
    {
        public int IntValue() => 0;
    }

    public sealed class Long
    {
        public long LongValue() => 0;
    }

    public sealed class Float
    {
        public float FloatValue() => 0;
    }
}

namespace Android.Util
{
    public static class Log
    {
        public static int Warn(string? tag, string? message) { PKForge.App.Services.AppLog.Warn(tag ?? "", message ?? ""); return 0; }
        public static int Error(string? tag, string? message) { PKForge.App.Services.AppLog.Error(tag ?? "", message ?? ""); return 0; }
        public static int Info(string? tag, string? message) { PKForge.App.Services.AppLog.Info(tag ?? "", message ?? ""); return 0; }
    }
}

namespace Android.Content
{
    public enum FileCreationMode
    {
        Private = 0,
    }

    public class Context
    {
        public virtual string? PackageName => null;

        public virtual ISharedPreferences? GetSharedPreferences(string? name, FileCreationMode mode) => null;
    }

    public interface ISharedPreferencesEditor : System.IDisposable
    {
        ISharedPreferencesEditor? PutBoolean(string? key, bool value);
        ISharedPreferencesEditor? PutInt(string? key, int value);
        ISharedPreferencesEditor? PutLong(string? key, long value);
        ISharedPreferencesEditor? PutFloat(string? key, float value);
        ISharedPreferencesEditor? PutString(string? key, string? value);

        bool Commit();
    }

    public interface ISharedPreferences
    {
        IDictionary<string, object> All { get; }

        ISharedPreferencesEditor? Edit();
    }
}

namespace Android.App
{
    public static class Application
    {
        public static Android.Content.Context Context { get; } = new();
    }
}

namespace Android.Views
{
    public enum MotionEventActions
    {
        Down,
        Move,
        Up,
        Cancel,
    }

    public enum FeedbackConstants
    {
        LongPress,
    }

    public class MotionEvent
    {
        public MotionEventActions ActionMasked => MotionEventActions.Cancel;

        public float GetX() => 0;

        public float GetY() => 0;
    }

    public class TouchEventArgs : System.EventArgs
    {
        public MotionEvent? Event { get; set; }

        public bool Handled { get; set; }
    }

    public class ViewConfiguration
    {
        public int ScaledTouchSlop => 0;

        public static int LongPressTimeout => 500;

        public static ViewConfiguration? Get(Android.Content.Context context) => null;
    }

    public class View
    {
        public Android.Content.Context? Context => null;

        public bool Focusable { get; set; }

        public bool FocusableInTouchMode { get; set; }

        public event System.EventHandler<TouchEventArgs>? Touch;

        public bool PerformHapticFeedback(FeedbackConstants feedback) => false;
    }
}
