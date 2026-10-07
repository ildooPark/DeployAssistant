using System;
using System.Collections.Generic;

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

        /// <summary>
        /// Emit the screen body as Spectre.Console markup. The frame (header, breadcrumb,
        /// footer) is drawn around it; size content from <see cref="Ui.Width"/> and
        /// <see cref="Ui.BodyHeight"/>.
        /// </summary>
        public abstract void Render();

        /// <summary>Breadcrumb segment shown in the header; empty to stay out of the trail.</summary>
        public virtual string Title => "";

        /// <summary>Key bindings for the footer, in priority order (the tail drops first on narrow windows).</summary>
        public virtual IReadOnlyList<KeyHint> Hints => Array.Empty<KeyHint>();

        /// <summary>Translate one keystroke into a transition.</summary>
        public abstract ScreenAction Handle(ConsoleKeyInfo key);

        /// <summary>
        /// Called by the engine whenever this screen is popped off the stack,
        /// replaced by another screen, or the application exits. Use for cleanup
        /// (e.g. unsubscribing event handlers). Default: no-op.
        /// </summary>
        public virtual void OnExit() { }

        /// <summary>
        /// Optional: return a non-null ScreenAction to transition without waiting for a key.
        /// Engine calls this once after OnEnter and Render. Default: return null.
        /// </summary>
        public virtual ScreenAction? AutoAdvance() => null;
    }
}
