namespace DeployAssistant.CLI.Engine
{
    internal abstract record ScreenAction
    {
        public sealed record Stay : ScreenAction;
        public sealed record Pop : ScreenAction;
        public sealed record Push(Screen Next) : ScreenAction;
        public sealed record Replace(Screen Next) : ScreenAction;
        public sealed record Exit : ScreenAction;

        public static readonly Stay StayAction = new();
        public static readonly Pop PopAction = new();
        public static readonly Exit ExitAction = new();
    }
}
