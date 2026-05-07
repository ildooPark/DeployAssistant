namespace DeployAssistant
{
    public partial class App : System.Windows.Application
    {
        public AppServices? Services { get; private set; }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            Services = new AppServices();
        }
    }
}
