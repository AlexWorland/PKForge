using PKForge.Linux.Core;
using Xunit;

namespace PKForge.Linux.Tests;

public sealed class OskModelTests
{
    private static OskModel OnQ(string initial = "", int maxLength = 0)
    {
        var model = new OskModel(initial, maxLength, numericOnly: false);
        model.Move(1, 0); // row 1, column 0: 'q'
        return model;
    }

    [Fact]
    public void TypingAppendsAndShiftUppercasesOneLetter()
    {
        var model = OnQ();
        model.PressKey();
        Assert.Equal("q", model.Text);
        model.ToggleShift();
        Assert.True(model.Shifted);
        model.PressKey();
        Assert.Equal("qQ", model.Text);
        Assert.False(model.Shifted);
        model.PressKey();
        Assert.Equal("qQq", model.Text);
    }

    [Fact]
    public void MoveWrapsAtTheRowAndColumnEdges()
    {
        var model = new OskModel("", 0, numericOnly: false);
        Assert.Equal((0, 0), model.Cursor);
        model.Move(-1, 0);
        Assert.Equal((3, 0), model.Cursor);   // up from the top row lands on the bottom row
        model.Move(1, 0);
        Assert.Equal((0, 0), model.Cursor);   // and down from the bottom row returns to the top
        model.Move(0, -1);
        Assert.Equal((0, 9), model.Cursor);   // left from the first key lands on the last
        model.Move(0, 1);
        Assert.Equal((0, 0), model.Cursor);
    }

    [Fact]
    public void BackspaceOnEmptyTextDoesNothing()
    {
        var model = new OskModel("", 0, numericOnly: false);
        model.Backspace();
        Assert.Equal("", model.Text);
        model.PressKey();
        model.Backspace();
        Assert.Equal("", model.Text);
    }

    [Fact]
    public void TypingStopsAtMaxLength()
    {
        var model = OnQ(maxLength: 3);
        for (var i = 0; i < 4; i++) model.PressKey();
        Assert.Equal("qqq", model.Text);
        Assert.Equal(3, model.Text.Length);
    }

    [Fact]
    public void NumericOnlyOffersOnlyDigits()
    {
        var model = new OskModel("", 0, numericOnly: true);
        Assert.Equal(1, model.Rows.Count);
        Assert.Equal("1234567890", model.Rows[0]);
        model.Move(1, 0);                     // there is no second row to go to
        Assert.Equal((0, 0), model.Cursor);
        model.Move(0, 1);
        model.PressKey();
        Assert.Equal("2", model.Text);
    }

    [Fact]
    public void CancelReturnsOriginalText()
    {
        var model = new OskModel("Pika", 0, numericOnly: false);
        model.PressKey();
        model.PressKey();
        Assert.Equal("Pika11", model.Text);
        Assert.Equal("Pika", model.Cancel());
        Assert.Equal("Pika11", model.Confirm());
    }
}
