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
    public void Restored_WrapsInMagentaMarkup()
    {
        Assert.Equal("[magenta]hello[/]", TextStyle.Restored("hello"));
    }

    [Fact]
    public void Accent_WrapsInCyanBoldMarkup()
    {
        Assert.Equal("[cyan bold]hello[/]", TextStyle.Accent("hello"));
    }

    [Fact]
    public void Dim_WrapsInDimMarkup()
    {
        Assert.Equal("[dim]hello[/]", TextStyle.Dim("hello"));
    }

    [Theory]
    [InlineData(DataState.Added,    "  [green]+[/] [green]src/Foo.cs[/]  [dim](added)[/]")]
    [InlineData(DataState.Deleted,  "  [red]-[/] [red]src/Foo.cs[/]  [dim](deleted)[/]")]
    [InlineData(DataState.Modified, "  [yellow]~[/] [yellow]src/Foo.cs[/]  [dim](modified)[/]")]
    [InlineData(DataState.Restored, "  [magenta]*[/] [magenta]src/Foo.cs[/]  [dim](restored)[/]")]
    public void FormatFileState_MatchesLegacyOutput(DataState state, string expected)
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
