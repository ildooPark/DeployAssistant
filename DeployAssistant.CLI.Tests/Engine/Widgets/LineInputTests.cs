using System;
using DeployAssistant.CLI.Engine.Widgets;
using Xunit;

namespace DeployAssistant.CLI.Tests.Engine.Widgets;

public class LineInputTests
{
    private static ConsoleKeyInfo Char(char c) => new(c, (ConsoleKey)c, false, false, false);
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);

    [Fact]
    public void TypingChars_AppendsAndAdvancesCursor()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Char('b'));
        input.Handle(Char('c'));
        Assert.Equal("abc", input.Text);
        Assert.Equal(3, input.CursorIndex);
    }

    [Fact]
    public void Backspace_DeletesCharBeforeCursor()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Char('b'));
        input.Handle(Key(ConsoleKey.Backspace));
        Assert.Equal("a", input.Text);
        Assert.Equal(1, input.CursorIndex);
    }

    [Fact]
    public void Backspace_AtStart_NoOp()
    {
        var input = new LineInput();
        input.Handle(Key(ConsoleKey.Backspace));
        Assert.Equal("", input.Text);
        Assert.Equal(0, input.CursorIndex);
    }

    [Fact]
    public void LeftArrow_MovesCursorLeft()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Char('b'));
        input.Handle(Key(ConsoleKey.LeftArrow));
        Assert.Equal(1, input.CursorIndex);
    }

    [Fact]
    public void LeftArrow_AtStart_NoOp()
    {
        var input = new LineInput();
        input.Handle(Key(ConsoleKey.LeftArrow));
        Assert.Equal(0, input.CursorIndex);
    }

    [Fact]
    public void RightArrow_MovesCursorRight()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Key(ConsoleKey.LeftArrow));
        input.Handle(Key(ConsoleKey.RightArrow));
        Assert.Equal(1, input.CursorIndex);
    }

    [Fact]
    public void RightArrow_AtEnd_NoOp()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Key(ConsoleKey.RightArrow));
        Assert.Equal(1, input.CursorIndex);
    }

    [Fact]
    public void TypingMidString_InsertsAtCursor()
    {
        var input = new LineInput();
        input.Handle(Char('a'));
        input.Handle(Char('c'));
        input.Handle(Key(ConsoleKey.LeftArrow));
        input.Handle(Char('b'));
        Assert.Equal("abc", input.Text);
        Assert.Equal(2, input.CursorIndex);
    }

    [Fact]
    public void SetText_ResetsCursorToEnd()
    {
        var input = new LineInput();
        input.SetText("hello");
        Assert.Equal("hello", input.Text);
        Assert.Equal(5, input.CursorIndex);
    }

    [Fact]
    public void Tab_ZeroCandidates_NoChange()
    {
        var input = new LineInput { CandidateProvider = _ => Array.Empty<string>() };
        input.SetText(@"C:\foo\zzz");
        input.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(@"C:\foo\zzz", input.Text);
        Assert.Empty(input.Candidates);
        Assert.False(input.CandidateMode);
    }

    [Fact]
    public void Tab_OneCandidate_AppendsCompletionWithSeparator()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "Workspace" } };
        input.SetText(@"C:\Wo");
        input.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(@"C:\Workspace\", input.Text);
        Assert.Equal(input.Text.Length, input.CursorIndex);
        Assert.False(input.CandidateMode);
    }

    [Fact]
    public void Tab_OneCandidate_EmptyPartial_AppendsName()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "child" } };
        input.SetText(@"C:\foo\");
        input.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(@"C:\foo\child\", input.Text);
    }

    [Fact]
    public void Tab_MultipleCandidates_EntersCandidateModeWithFirstSelected()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "alpha", "beta", "gamma" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        Assert.True(input.CandidateMode);
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, input.Candidates);
        Assert.Equal(0, input.CandidateIndex);
        Assert.Equal(@"C:\", input.Text); // unchanged until user picks
    }

    [Fact]
    public void Tab_NoProvider_NoOp()
    {
        var input = new LineInput(); // no CandidateProvider
        input.SetText(@"C:\foo");
        input.Handle(Key(ConsoleKey.Tab));
        Assert.Equal(@"C:\foo", input.Text);
        Assert.False(input.CandidateMode);
    }

    [Fact]
    public void CandidateMode_DownArrow_AdvancesIndex()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "a", "b", "c" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, input.CandidateIndex);
    }

    [Fact]
    public void CandidateMode_UpArrow_DecrementsIndex()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "a", "b", "c" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Key(ConsoleKey.DownArrow));
        input.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(0, input.CandidateIndex);
    }

    [Fact]
    public void CandidateMode_DownArrow_AtEnd_NoOp()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "a", "b" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Key(ConsoleKey.DownArrow));
        input.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, input.CandidateIndex);
    }

    [Fact]
    public void CandidateMode_Enter_AppliesSelectedAndExits()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "alpha", "beta" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Key(ConsoleKey.DownArrow));
        input.Handle(Key(ConsoleKey.Enter));
        Assert.Equal(@"C:\beta\", input.Text);
        Assert.False(input.CandidateMode);
    }

    [Fact]
    public void CandidateMode_Escape_DismissesUnchanged()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "alpha", "beta" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Key(ConsoleKey.Escape));
        Assert.False(input.CandidateMode);
        Assert.Equal(@"C:\", input.Text);
    }

    [Fact]
    public void CandidateMode_TypingChar_DismissesAndApplies()
    {
        var input = new LineInput { CandidateProvider = _ => new[] { "alpha", "beta" } };
        input.SetText(@"C:\");
        input.Handle(Key(ConsoleKey.Tab));
        input.Handle(Char('a'));
        Assert.False(input.CandidateMode);
        Assert.Equal(@"C:\a", input.Text);
    }
}
