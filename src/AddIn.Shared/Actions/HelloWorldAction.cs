using AddIn.Shared.Adapters;

namespace AddIn.Shared.Actions
{
    public static class HelloWorldAction
    {
        public const string Title = "Hello World";

        /// <summary>
        /// Path into the assets\ folder, as "Feature/file.ico". The action names the icon;
        /// the version-specific adapter is what turns it into a real image.
        /// </summary>
        public const string IconPath = "Brand/favicon.ico";

        public static void Execute(ITiaNotifier notifier, string projectName)
        {
            notifier.Info(Title, $"\n\nHello world! {projectName ?? "No project"}");
        }
    }
}
