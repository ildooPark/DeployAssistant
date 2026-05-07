using System;
using DeployAssistant.CLI.Engine.Widgets;
using Xunit;

namespace DeployAssistant.CLI.Tests.Engine.Widgets;

public class SelectableListTests
{
    private static ConsoleKeyInfo Key(ConsoleKey k) => new('\0', k, false, false, false);
    private static ConsoleKeyInfo Char(char c) => new(c, (ConsoleKey)c, false, false, false);

    [Fact]
    public void Empty_RendersWithoutSelection()
    {
        var list = new SelectableList(itemCount: 0, viewportHeight: 5);
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, list.ViewportTop);
    }

    [Fact]
    public void DownArrow_MovesSelection()
    {
        var list = new SelectableList(itemCount: 10, viewportHeight: 5);
        list.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void UpArrow_AtTop_NoOp()
    {
        var list = new SelectableList(itemCount: 10, viewportHeight: 5);
        list.Handle(Key(ConsoleKey.UpArrow));
        Assert.Equal(0, list.SelectedIndex);
    }

    [Fact]
    public void DownArrow_AtBottom_NoOp()
    {
        var list = new SelectableList(itemCount: 3, viewportHeight: 5);
        list.Handle(Key(ConsoleKey.DownArrow));
        list.Handle(Key(ConsoleKey.DownArrow));
        list.Handle(Key(ConsoleKey.DownArrow)); // would land on 3
        Assert.Equal(2, list.SelectedIndex);
    }

    [Fact]
    public void DownArrow_BeyondViewport_ScrollsViewport()
    {
        var list = new SelectableList(itemCount: 10, viewportHeight: 3);
        // Move cursor to index 3 (one past the visible window 0..2)
        for (int i = 0; i < 3; i++) list.Handle(Key(ConsoleKey.DownArrow));
        Assert.Equal(3, list.SelectedIndex);
        Assert.Equal(1, list.ViewportTop); // scrolled by 1 to keep cursor visible
    }

    [Fact]
    public void UpArrow_AboveViewport_ScrollsViewport()
    {
        var list = new SelectableList(itemCount: 10, viewportHeight: 3);
        for (int i = 0; i < 5; i++) list.Handle(Key(ConsoleKey.DownArrow)); // SelectedIndex=5, ViewportTop=3
        Assert.Equal(5, list.SelectedIndex);
        Assert.Equal(3, list.ViewportTop);
        for (int i = 0; i < 4; i++) list.Handle(Key(ConsoleKey.UpArrow)); // SelectedIndex=1, ViewportTop=1
        Assert.Equal(1, list.SelectedIndex);
        Assert.Equal(1, list.ViewportTop);
    }

    [Fact]
    public void HalfPageDown_JumpsHalfViewport()
    {
        var list = new SelectableList(itemCount: 100, viewportHeight: 10);
        list.Handle(Char('d'));
        Assert.Equal(5, list.SelectedIndex);
    }

    [Fact]
    public void HalfPageUp_JumpsHalfViewport()
    {
        var list = new SelectableList(itemCount: 100, viewportHeight: 10);
        list.Handle(Char('d'));
        list.Handle(Char('d')); // 10
        list.Handle(Char('u')); // 5
        Assert.Equal(5, list.SelectedIndex);
    }

    [Fact]
    public void HalfPageDown_ClampsAtEnd()
    {
        var list = new SelectableList(itemCount: 7, viewportHeight: 10);
        list.Handle(Char('d')); // would land on 5 — but that's <=6 so stays 5
        list.Handle(Char('d')); // would land on 10 — clamps to 6
        Assert.Equal(6, list.SelectedIndex);
    }

    [Fact]
    public void Home_GoesToFirst()
    {
        var list = new SelectableList(itemCount: 100, viewportHeight: 10);
        list.Handle(Char('d'));
        list.Handle(Key(ConsoleKey.Home));
        Assert.Equal(0, list.SelectedIndex);
        Assert.Equal(0, list.ViewportTop);
    }

    [Fact]
    public void End_GoesToLast()
    {
        var list = new SelectableList(itemCount: 100, viewportHeight: 10);
        list.Handle(Key(ConsoleKey.End));
        Assert.Equal(99, list.SelectedIndex);
        Assert.Equal(90, list.ViewportTop);
    }
}
