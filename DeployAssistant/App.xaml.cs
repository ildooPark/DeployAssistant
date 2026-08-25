namespace DeployAssistant
{
    public partial class App : System.Windows.Application
    {
        public AppServices? Services { get; private set; }

        static App()
        {
            // Must precede every file operation in the process, so the runtime never
            // caches the legacy path behavior.
            DeployAssistant.Utils.PathCompat.EnableNetFrameworkLongPaths();
        }

        public string CurrentLanguage { get; private set; } = "ko-KR";

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);
            Services = new AppServices();
            string? saved = Services.MetaDataManager.RequestSavedLanguage();
            ApplyLanguage(string.IsNullOrEmpty(saved) ? "ko-KR" : saved!);
        }

        /// <summary>Swaps the merged Strings.*.xaml dictionary. DynamicResource labels update
        /// live; grid column headers (resolved at parse) need a restart.</summary>
        public void ApplyLanguage(string languageCode)
        {
            var dicts = Resources.MergedDictionaries;
            for (int i = 0; i < dicts.Count; i++)
            {
                if (dicts[i].Source != null && dicts[i].Source.OriginalString.Contains("/Strings."))
                {
                    dicts[i] = new System.Windows.ResourceDictionary
                    {
                        Source = new System.Uri($"View/Strings.{languageCode}.xaml", System.UriKind.Relative)
                    };
                    CurrentLanguage = languageCode;
                    return;
                }
            }
        }
    }
}
