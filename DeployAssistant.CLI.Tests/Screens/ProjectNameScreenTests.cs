using System;
using DeployAssistant.CLI.Engine;
using DeployAssistant.CLI.Screens;
using Xunit;

namespace DeployAssistant.CLI.Tests.Screens
{
    public class ProjectNameScreenTests
    {
        [Fact]
        public void Handle_EnterKey_WithInput_CallsOnCompleteWithInputName()
        {
            bool callbackInvoked = false;
            string actualPath = "";
            string actualName = "";

            var screen = new ProjectNameScreen("C:\\path", "DefaultName", (p, n) =>
            {
                callbackInvoked = true;
                actualPath = p;
                actualName = n;
            });

            // Simulate appending "MyP" then pressing Enter
            screen.Handle(new ConsoleKeyInfo('M', ConsoleKey.M, false, false, false));
            screen.Handle(new ConsoleKeyInfo('y', ConsoleKey.Y, false, false, false));
            screen.Handle(new ConsoleKeyInfo('P', ConsoleKey.P, false, false, false));
            var result = screen.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            Assert.Equal(ScreenAction.PopAction, result);
            Assert.True(callbackInvoked);
            Assert.Equal("C:\\path", actualPath);
            Assert.Equal("DefaultNameMyP", actualName); // Because the input initially has "DefaultName" and we typed "MyP"
        }

        [Fact]
        public void Handle_EscapeKey_CallsOnCompleteWithDefaultName()
        {
            bool callbackInvoked = false;
            string actualPath = "";
            string actualName = "";

            var screen = new ProjectNameScreen("C:\\path", "DefaultName", (p, n) =>
            {
                callbackInvoked = true;
                actualPath = p;
                actualName = n;
            });

            // Simulate typing something and then Escape
            screen.Handle(new ConsoleKeyInfo('A', ConsoleKey.A, false, false, false));
            var result = screen.Handle(new ConsoleKeyInfo('\x1B', ConsoleKey.Escape, false, false, false));

            Assert.Equal(ScreenAction.PopAction, result);
            Assert.True(callbackInvoked);
            Assert.Equal("C:\\path", actualPath);
            Assert.Equal("DefaultName", actualName);
        }

        [Fact]
        public void Handle_EnterKey_WithEmptyInput_CallsOnCompleteWithDefaultName()
        {
            bool callbackInvoked = false;
            string actualPath = "";
            string actualName = "";

            var screen = new ProjectNameScreen("C:\\path", "DefaultName", (p, n) =>
            {
                callbackInvoked = true;
                actualPath = p;
                actualName = n;
            });

            // Backspace out the default name
            for (int i = 0; i < "DefaultName".Length; i++)
            {
                screen.Handle(new ConsoleKeyInfo('\b', ConsoleKey.Backspace, false, false, false));
            }

            var result = screen.Handle(new ConsoleKeyInfo('\r', ConsoleKey.Enter, false, false, false));

            Assert.Equal(ScreenAction.PopAction, result);
            Assert.True(callbackInvoked);
            Assert.Equal("C:\\path", actualPath);
            Assert.Equal("DefaultName", actualName);
        }
    }
}
