using Microsoft.Maui;

namespace PKForge.App.Views;

/// <summary>
/// TextPopup.Close asks the field whether a soft keyboard is up before putting it away. MAUI's
/// implementation of that question throws NotSupportedException off Android and iOS, which aborted
/// the popup's Close and left it open after the on-screen keyboard confirmed. There is no platform
/// keyboard on Linux (OskPage is a page, not an input method), so the honest answer is no.
/// This lives in the namespace of the views, so C# finds it before MAUI's extension method of the
/// same name, and the shared sources stay unedited (the same trick as LinuxHosting.UseSkiaSharp).
/// </summary>
internal static class LinuxSoftInput
{
    public static bool IsSoftInputShowing(this ITextInput input) => false;
}
