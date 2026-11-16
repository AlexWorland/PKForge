using PKForge.App.Services;
using PKForge.App.Theme;
using PKForge.Chrome;
using PKForge.Linux.Core;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace PKForge.App.Views;

/// <summary>
/// The on-screen keyboard. The handheld has no keyboard and its firmware has no soft one, so a
/// text field opens this page instead (see OskHook). D-pad moves over the keys, A types the key
/// under the cursor, B deletes, X toggles shift for one letter, Start confirms and Select cancels.
/// Touch types the key touched. The typing itself lives in OskModel.
/// </summary>
public sealed class OskPage : ContentPage, IPadHandler
{
    private readonly string _title;
    private readonly OskModel _model;
    private readonly TaskCompletionSource<(string Text, bool Confirmed)> _result = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly SKCanvasView _canvas = new() { EnableTouchEvents = true };
    private readonly List<(int Row, int Column, SKRect Rect)> _keyHits = [];
    private readonly List<(string Action, SKRect Rect)> _buttonHits = [];
    private int _closed;    // 0 open, 1 closing: guards every exit path so the result is set exactly once

    private OskPage(string title, string initial, int maxLength, bool numericOnly)
    {
        _title = title;
        _model = new OskModel(initial, maxLength, numericOnly);
        Title = title;
        BackgroundColor = UiTokens.Housing;
        NavigationPage.SetHasNavigationBar(this, false);
        _canvas.PaintSurface += PaintGuard.Wrap("OskPage.OnPaint", OnPaint);
        _canvas.Touch += OnTouch;
        Content = new Grid { Children = { DsChrome.GridBackground(), _canvas } };
    }

    /// <summary>Opens the keyboard over the current page and returns the text when it closes.
    /// Cancelling returns <paramref name="initial"/>.</summary>
    public static async Task<string> EditAsync(INavigation nav, string title, string initial, int maxLength, bool numericOnly) =>
        (await EditCoreAsync(nav, title, initial, maxLength, numericOnly)).Text;

    /// <summary>As <see cref="EditAsync"/>, and says whether the player confirmed (a field is only
    /// "completed" then, even when the text did not change).</summary>
    internal static async Task<(string Text, bool Confirmed)> EditCoreAsync(INavigation nav, string title, string initial, int maxLength, bool numericOnly)
    {
        var page = new OskPage(title, initial, maxLength, numericOnly);
        await MainThread.InvokeOnMainThreadAsync(() => nav.PushAsync(page, animated: false));
        return await page._result.Task;
    }

    // ---------- Lifecycle ----------

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (Volatile.Read(ref _closed) == 0)
            IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Push(this);
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Release();
        // Finish pops this page itself and answers after the pop; only a page taken away by
        // something else (not Start or Select) is a cancel that nobody has answered yet.
        if (Interlocked.Exchange(ref _closed, 1) == 0) _result.TrySetResult((_model.Cancel(), false));
    }

    private void Release() => IPlatformApplication.Current?.Services.GetService<GamepadRouter>()?.Remove(this);

    private void Finish(bool confirmed)
    {
        if (Interlocked.Exchange(ref _closed, 1) != 0) return;
        Release();
        var text = confirmed ? _model.Confirm() : _model.Cancel();
        // The page leaves first and the caller hears the answer after: the caller sets the field
        // and may open its next screen at once, and a late pop would remove that one instead.
        _ = MainThread.InvokeOnMainThreadAsync(async () =>
        {
            try
            {
                if (ReferenceEquals(Navigation.NavigationStack.LastOrDefault(), this)) await Navigation.PopAsync(animated: false);
                else Navigation.RemovePage(this);
            }
            catch (Exception ex) { AppLog.Warn("osk", $"Closing the keyboard failed: {ex.Message}"); }
            finally { _result.TrySetResult((text, confirmed)); }
        });
    }

    // ---------- Painting ----------

    private float _unit = 1f;

    private void OnPaint(object? sender, SKPaintSurfaceEventArgs args)
    {
        var c = args.Surface.Canvas;
        c.Clear(SKColors.Transparent);
        var face = BoxBrowserPage.PixelTypeface();
        var info = args.Info;
        var width = PickerPaint.DesignWidth;
        _unit = info.Width / width;
        var height = info.Height / _unit;
        _keyHits.Clear();
        _buttonHits.Clear();
        c.Save();
        c.Scale(_unit);

        using var titleFont = new SKFont(face, 44f);
        using var textFont = new SKFont(face, 52f);
        using var keyFont = new SKFont(face, 42f);
        using var hintFont = new SKFont(face, 28f);

        var panelWidth = MathF.Min(1500f, width - 48f);
        var panel = new SKRect((width - panelWidth) / 2f, 20f, (width + panelWidth) / 2f, height - 20f);
        using (var fill = new SKPaint { Color = Pksm.Paper, IsAntialias = true }) c.DrawRoundRect(panel, 26f, 26f, fill);
        using (var rim = new SKPaint { Color = Pksm.SelectBorder, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 4f })
            c.DrawRoundRect(SKRect.Inflate(panel, -2f, -2f), 26f, 26f, rim);

        using var ink = new SKPaint { Color = Pksm.Ink, IsAntialias = true };
        using var soft = new SKPaint { Color = Pksm.InkSoft, IsAntialias = true };
        c.DrawText(_title, panel.Left + 32f, panel.Top + 62f, SKTextAlign.Left, titleFont, ink);

        // The text so far, with a caret.
        var plate = new SKRect(panel.Left + 28f, panel.Top + 90f, panel.Right - 28f, panel.Top + 190f);
        using (var shade = new SKPaint { Color = Pksm.PaperShade, IsAntialias = true }) c.DrawRoundRect(plate, 14f, 14f, shade);
        using (var edge = new SKPaint { Color = Pksm.PaperEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f })
            c.DrawRoundRect(plate, 14f, 14f, edge);
        var shown = _model.Text;
        // Long text keeps its end in view: the caret is where typing goes.
        while (shown.Length > 1 && textFont.MeasureText(shown + "_") > plate.Width - 48f) shown = shown[1..];
        c.DrawText(shown + "_", plate.Left + 24f, plate.MidY + 18f, SKTextAlign.Left, textFont, ink);

        // The buttons at the foot, then the keys in what is left between.
        var foot = new SKRect(panel.Left + 28f, panel.Bottom - 100f, panel.Right - 28f, panel.Bottom - 24f);
        var buttons = new (string Action, string Label)[]
        {
            ("shift", _model.Shifted ? "X  SHIFT ON" : "X  Shift"), ("back", "B  Delete"),
            ("done", "Start  Done"), ("cancel", "Select  Cancel"),
        };
        var gap = 16f;
        var buttonWidth = (foot.Width - gap * (buttons.Length - 1)) / buttons.Length;
        for (var i = 0; i < buttons.Length; i++)
        {
            var rect = new SKRect(foot.Left + i * (buttonWidth + gap), foot.Top, foot.Left + i * (buttonWidth + gap) + buttonWidth, foot.Bottom);
            using (var fill = new SKPaint { Color = buttons[i].Action == "shift" && _model.Shifted ? Pksm.SelectFill : Pksm.PaperShade, IsAntialias = true })
                c.DrawRoundRect(rect, 14f, 14f, fill);
            using (var edge = new SKPaint { Color = Pksm.PaperEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3f })
                c.DrawRoundRect(rect, 14f, 14f, edge);
            c.DrawText(buttons[i].Label, rect.MidX, rect.MidY + 10f, SKTextAlign.Center, hintFont, ink);
            _buttonHits.Add((buttons[i].Action, Scaled(rect)));
        }

        var rows = _model.Rows;
        var gridTop = plate.Bottom + 20f;
        var gridBottom = foot.Top - 20f;
        var cell = MathF.Min(MathF.Min(140f, (gridBottom - gridTop) / rows.Count), (panel.Width - 56f) / 10f);
        var gridLeft = panel.MidX - cell * 10f / 2f;
        for (var r = 0; r < rows.Count; r++)
        {
            var line = rows[r];
            for (var k = 0; k < line.Length; k++)
            {
                var rect = new SKRect(gridLeft + k * cell + 5f, gridTop + r * cell + 5f, gridLeft + (k + 1) * cell - 5f, gridTop + (r + 1) * cell - 5f);
                var here = _model.Cursor == (r, k);
                using (var fill = new SKPaint { Color = here ? Pksm.SelectFill : Pksm.PaperShade, IsAntialias = true })
                    c.DrawRoundRect(rect, 12f, 12f, fill);
                using (var edge = new SKPaint { Color = here ? Pksm.SelectBorder : Pksm.PaperEdge, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = here ? 6f : 3f })
                    c.DrawRoundRect(rect, 12f, 12f, edge);
                var ch = _model.Shifted ? char.ToUpperInvariant(line[k]) : line[k];
                var label = ch == ' ' ? "space" : ch.ToString();
                var font = ch == ' ' ? hintFont : keyFont;
                c.DrawText(label, rect.MidX, rect.MidY + 15f, SKTextAlign.Center, font, ink);
                _keyHits.Add((r, k, Scaled(rect)));
            }
        }
        c.Restore();
    }

    private SKRect Scaled(SKRect r) => new(r.Left * _unit, r.Top * _unit, r.Right * _unit, r.Bottom * _unit);

    private void Invalidate() => _canvas.InvalidateSurface();

    // ---------- Touch ----------

    private void OnTouch(object? sender, SKTouchEventArgs args)
    {
        args.Handled = true;
        if (args.ActionType != SKTouchAction.Released || Volatile.Read(ref _closed) != 0) return;
        var at = args.Location;
        foreach (var (action, rect) in _buttonHits)
        {
            if (!rect.Contains(at)) continue;
            switch (action)
            {
                case "shift": _model.ToggleShift(); Invalidate(); break;
                case "back": _model.Backspace(); Invalidate(); break;
                case "done": Finish(confirmed: true); break;
                default: Finish(confirmed: false); break;
            }
            return;
        }
        foreach (var (row, column, rect) in _keyHits)
        {
            if (!rect.Contains(at)) continue;
            _model.Place(row, column);
            _model.PressKey();
            Invalidate();
            return;
        }
    }

    // ---------- Pad ----------

    public bool OnPadButton(PadButton button)
    {
        if (Volatile.Read(ref _closed) != 0) return true;
        switch (button)
        {
            case PadButton.Up: _model.Move(-1, 0); break;
            case PadButton.Down: _model.Move(1, 0); break;
            case PadButton.Left: case PadButton.L: _model.Move(0, -1); break;
            case PadButton.Right: case PadButton.R: _model.Move(0, 1); break;
            case PadButton.A: _model.PressKey(); break;
            case PadButton.B: _model.Backspace(); break;
            case PadButton.X: _model.ToggleShift(); break;
            case PadButton.Start: Finish(confirmed: true); return true;
            case PadButton.Select: Finish(confirmed: false); return true;
            default: return true;
        }
        Invalidate();
        return true; // the keyboard owns the pad while open
    }
}
