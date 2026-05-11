using DeployAssistant.CLI.Engine;
using System;
using Xunit;

namespace DeployAssistant.CLI.Tests.Engine;

public class TuiPromptTests
{
    // Helper to build a ConsoleKeyInfo from a ConsoleKey
    private static ConsoleKeyInfo Key(ConsoleKey key, char ch = '\0') =>
        new ConsoleKeyInfo(ch, key, shift: false, alt: false, control: false);

    [Fact]
    public void Confirm_YKey_ReturnsTrue()
    {
        var result = TuiPrompt.Confirm("Test", "message?", () => Key(ConsoleKey.Y, 'Y'));
        Assert.True(result);
    }

    [Fact]
    public void Confirm_LowercaseYKey_ReturnsTrue()
    {
        // Lower-case 'y' maps to ConsoleKey.Y as well; verify via the key enum branch
        var result = TuiPrompt.Confirm("Test", "message?", () => Key(ConsoleKey.Y, 'y'));
        Assert.True(result);
    }

    [Fact]
    public void Confirm_NKey_ReturnsFalse()
    {
        var result = TuiPrompt.Confirm("Test", "message?", () => Key(ConsoleKey.N, 'n'));
        Assert.False(result);
    }

    [Fact]
    public void Confirm_EscapeKey_ReturnsFalse()
    {
        var result = TuiPrompt.Confirm("Test", "message?", () => Key(ConsoleKey.Escape));
        Assert.False(result);
    }

    [Fact]
    public void Confirm_OtherKey_ReturnsFalse()
    {
        // 'q' is not y/n/Esc — should default to false
        var result = TuiPrompt.Confirm("Test", "message?", () => Key(ConsoleKey.Q, 'q'));
        Assert.False(result);
    }

    [Fact]
    public void Confirm_NullReadKey_Throws()
    {
        Assert.Throws<ArgumentNullException>(() =>
            TuiPrompt.Confirm("Test", "message?", null!));
    }
}
