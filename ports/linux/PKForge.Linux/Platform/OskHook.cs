using System.Runtime.CompilerServices;
using Microsoft.Maui.Handlers;
using PKForge.App.Services;
using PKForge.App.Views;

namespace PKForge.Linux;

/// <summary>
/// Gives every Entry and Editor in the app the on-screen keyboard: when a field takes focus it is
/// put down again, OskPage edits a copy of its text, and the result is written back the way a
/// real keyboard would leave it (Text set, then Completed for an Entry). The app is not edited:
/// the handler mappers are the only seam both frameworks offer.
/// </summary>
public static class OskHook
{
    private static readonly ConditionalWeakTable<VisualElement, object> Subscribed = [];
    private static bool _logged;
    private static bool _busy;

    public static void Install()
    {
        if (Environment.GetEnvironmentVariable("PKFORGE_OSK") == "0") return;
        // OpenMaui renders fields with its own handlers, which keep their own mappers: the ones in
        // Microsoft.Maui.Handlers are never consulted on Linux. Both are appended to; the second
        // is only a cover for a future OpenMaui that goes back to the shared handlers.
        Microsoft.Maui.Platform.Linux.Handlers.EntryHandler.Mapper.AppendToMapping("PKForgeLinuxOsk", (handler, view) => Attach(view as VisualElement, "Entry", handler));
        Microsoft.Maui.Platform.Linux.Handlers.EditorHandler.Mapper.AppendToMapping("PKForgeLinuxOsk", (handler, view) => Attach(view as VisualElement, "Editor", handler));
        EntryHandler.Mapper.AppendToMapping("PKForgeLinuxOsk", (handler, view) => Attach(view as VisualElement, "Entry", handler));
        EditorHandler.Mapper.AppendToMapping("PKForgeLinuxOsk", (handler, view) => Attach(view as VisualElement, "Editor", handler));
    }

    private static void Attach(VisualElement? view, string kind, object handler)
    {
        if (view is null) return;
        if (!_logged)
        {
            _logged = true;
            AppLog.Info("osk", $"Handler mapping runs: first {kind} created, handler {handler.GetType().FullName}");
        }
        lock (Subscribed)
        {
            if (Subscribed.TryGetValue(view, out _)) return;
            Subscribed.Add(view, handler);
        }
        view.Focused += OnFocused;
    }

    private static async void OnFocused(object? sender, FocusEventArgs args)
    {
        if (sender is not VisualElement view) return;
        // The field can take focus again when the keyboard leaves; that must not reopen it.
        if (_busy) return;
        var (text, maxLength, numeric, readOnly) = sender switch
        {
            Entry e => (e.Text, e.MaxLength, e.Keyboard == Keyboard.Numeric, e.IsReadOnly),
            Editor ed => (ed.Text, ed.MaxLength, ed.Keyboard == Keyboard.Numeric, ed.IsReadOnly),
            _ => (null, 0, false, true),
        };
        if (readOnly || !view.IsEnabled) return;
        var nav = FileBrowserPage.CurrentNavigation;
        if (nav is null) return;
        _busy = true;
        try
        {
            AppLog.Info("osk", $"{sender.GetType().Name} focused: opening the keyboard");
            view.Unfocus();
            var title = (sender as Entry)?.Placeholder ?? (sender as Editor)?.Placeholder;
            var (result, confirmed) = await OskPage.EditCoreAsync(nav, string.IsNullOrWhiteSpace(title) ? "Enter text" : title,
                text ?? "", maxLength is > 0 and < int.MaxValue ? maxLength : 0, numeric);
            switch (sender)
            {
                case Entry entry:
                    if (entry.Text != result) entry.Text = result;
                    if (confirmed) entry.SendCompleted();
                    break;
                case Editor editor:
                    if (editor.Text != result) editor.Text = result;
                    break;
            }
            // Whatever gave the field focus back in the meantime goes too.
            view.Unfocus();
            AppLog.Info("osk", $"Keyboard closed, confirmed={confirmed}, text length {result.Length}");
        }
        catch (Exception ex) { AppLog.Error("osk", "On-screen keyboard failed", ex); }
        finally
        {
            // Let focus events queued by the pop pass before the next focus may open the keyboard.
            _ = MainThread.InvokeOnMainThreadAsync(async () => { await Task.Delay(300); _busy = false; });
        }
    }
}
