using System;

namespace DeployAssistant.CLI.Engine
{
    /// <summary>
    /// Base class for all TUI screens. Abstract class (not interface) so that
    /// <see cref="AutoAdvance"/> can be a virtual default — default interface
    /// methods are not supported on the .NET Framework 4.7.2 runtime that the
    /// CLI targets.
    /// </summary>
    internal abstract class Screen
    {
        /// <summary>
        /// Called by the engine the first time this screen becomes top-of-stack,
        /// and again whenever it returns to top-of-stack via Pop.
        /// Long-running synchronous work (manager calls, Status spinners) goes here.
        /// </summary>
        public virtual void OnEnter() { }

        /// <summary>Emit Spectre.Console markup to the console.</summary>
        public abstract void Render();

        /// <summary>Translate one keystroke into a transition.</summary>
        public abstract ScreenAction Handle(ConsoleKeyInfo key);

        /// <summary>
        /// Optional: return a non-null ScreenAction to transition without waiting for a key.
        /// Engine calls this once after OnEnter and Render. Default: return null.
        /// </summary>
        public virtual ScreenAction? AutoAdvance() => null;
    }
}
