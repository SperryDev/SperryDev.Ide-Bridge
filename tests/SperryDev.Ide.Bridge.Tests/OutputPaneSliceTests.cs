using SperryDev.Ide.Bridge;
using Xunit;

namespace SperryDev.Ide.Bridge.Tests;

public sealed class OutputPaneSliceTests
{
    [Fact]
    public void Slice_FromOffset_ReturnsTail()
    {
        const string text = "one\ntwo\nthree\n";

        var result = OutputPaneSlice.Slice(text, fromOffset: 4, maxChars: 1000, anchor: null);

        Assert.Equal("two\nthree\n", result.Text);
        Assert.Equal(text.Length, result.NextOffset);
        Assert.False(result.Reset);
    }

    [Fact]
    public void Slice_MaxChars_CutsAtLineBoundary()
    {
        const string text = "aaa\nbbb\nccc\n";

        var result = OutputPaneSlice.Slice(text, fromOffset: 0, maxChars: 6, anchor: null);

        Assert.Equal("aaa\n", result.Text);
        Assert.Equal(4, result.NextOffset);
    }

    [Fact]
    public void Slice_AnchorMismatch_ClearAndRefill_Resets()
    {
        const string before = "old line one\nold line two\n";
        var first = OutputPaneSlice.Slice(before, fromOffset: 0, maxChars: 1000, anchor: null);

        const string after = "a completely different and much longer refilled output here\n";
        var second = OutputPaneSlice.Slice(after, first.NextOffset, maxChars: 1000, anchor: first.Anchor);

        Assert.True(second.Reset);
        Assert.Equal(after, second.Text);
    }

    [Fact]
    public void Slice_OffsetBeyondEnd_Resets()
    {
        var result = OutputPaneSlice.Slice("short\n", fromOffset: 500, maxChars: 1000, anchor: "anything");

        Assert.True(result.Reset);
        Assert.Equal("short\n", result.Text);
    }

    [Fact]
    public void Slice_Empty_ReturnsEmpty()
    {
        var result = OutputPaneSlice.Slice(string.Empty, fromOffset: 0, maxChars: 100, anchor: null);

        Assert.Equal(string.Empty, result.Text);
        Assert.Equal(0, result.NextOffset);
        Assert.False(result.Reset);
    }
}
