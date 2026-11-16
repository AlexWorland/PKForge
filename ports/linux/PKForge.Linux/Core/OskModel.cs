using System.Text;

namespace PKForge.Linux.Core;

/// <summary>
/// The state of the on-screen keyboard: the text being edited, the key under the cursor and the
/// one-shot shift. No drawing and no MAUI, so it is unit-tested; OskPage paints it.
/// </summary>
public sealed class OskModel(string initial, int maxLength, bool numericOnly)
{
    // The last key of the fourth row is the apostrophe; the space sits in front of it.
    private static readonly string[] Full = ["1234567890", "qwertyuiop", "asdfghjkl-", "zxcvbnm. '"];

    private readonly StringBuilder _text = new(initial);
    private int _row;
    private int _column;

    public IReadOnlyList<string> Rows { get; } = numericOnly ? [Full[0]] : Full;
    public string Text => _text.ToString();
    public (int Row, int Column) Cursor => (_row, _column);
    public bool Shifted { get; private set; }

    /// <summary>The character the cursor key would type right now.</summary>
    public char CurrentChar => Shifted ? char.ToUpperInvariant(Rows[_row][_column]) : Rows[_row][_column];

    /// <summary>Moves the cursor; stepping past an edge wraps to the opposite edge.</summary>
    public void Move(int dRow, int dColumn)
    {
        _row = Wrap(_row + dRow, Rows.Count);
        _column = Wrap(_column + dColumn, Rows[_row].Length);
    }

    public void PressKey()
    {
        if (maxLength > 0 && _text.Length >= maxLength) return;
        _text.Append(CurrentChar);
        Shifted = false;
    }

    public void Backspace()
    {
        if (_text.Length > 0) _text.Length--;
    }

    public void ToggleShift() => Shifted = !Shifted;

    public string Confirm() => Text;

    public string Cancel() => initial;

    /// <summary>Puts the cursor on a key (a touch), ignoring positions that are not on the keyboard.</summary>
    public bool Place(int row, int column)
    {
        if (row < 0 || row >= Rows.Count || column < 0 || column >= Rows[row].Length) return false;
        (_row, _column) = (row, column);
        return true;
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;
}
