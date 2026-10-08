using DeployAssistant.CLI.Engine;
using DeployAssistant.DataComponent;
using Xunit;

namespace DeployAssistant.CLI.Tests.Engine;

public class TextStyleTests
{
    [Fact]
    public void Added_WrapsInGreenMarkup()
    {
        Assert.Equal("[green]hello[/]", TextStyle.Added("hello"));
    }

    [Fact]
    public void Removed_WrapsInRedMarkup()
    {
        Assert.Equal("[red]hello[/]", TextStyle.Removed("hello"));
    }

    [Fact]
    public void Modified_WrapsInYellowMarkup()
    {
        Assert.Equal("[yellow]hello[/]", TextStyle.Modified("hello"));
    }

    [Fact]
    public void Restored_WrapsInFuchsiaMarkup()
    {
        Assert.Equal("[fuchsia]hello[/]", TextStyle.Restored("hello"));
    }

    [Fact]
    public void Accent_WrapsInAquaBoldMarkup()
    {
        Assert.Equal("[aqua bold]hello[/]", TextStyle.Accent("hello"));
    }

    [Fact]
    public void Dim_UsesGreyNotTheFaintAttribute()
    {
        // Legacy conhost ignores SGR 2 (faint), so "dim" text would look normal there.
        Assert.Equal("[grey]hello[/]", TextStyle.Dim("hello"));
    }

    [Theory]
    [InlineData(DataState.Added,    "[green]ADD[/]  [green]src/Foo.cs[/]")]
    [InlineData(DataState.Deleted,  "[red]DEL[/]  [red]src/Foo.cs[/]")]
    [InlineData(DataState.Modified, "[yellow]MOD[/]  [yellow]src/Foo.cs[/]")]
    [InlineData(DataState.Restored, "[fuchsia]RST[/]  [fuchsia]src/Foo.cs[/]")]
    public void FormatFileState_RendersBadgeAndPath(DataState state, string expected)
    {
        Assert.Equal(expected, TextStyle.FormatFileState(state, "src/Foo.cs"));
    }

    [Fact]
    public void FormatFileState_EscapesMarkupInPath()
    {
        // Spectre's Markup.Escape turns "[" into "[[" so legitimate paths containing '[' don't break parsing.
        var result = TextStyle.FormatFileState(DataState.Added, "src/[bracket].cs");
        Assert.Contains("src/[[bracket]].cs", result);
    }
}
