
namespace Core
{
    public static class HelloWorldAction
    {
        public const string Title = "Hello World";

        /// <summary>
        /// Path into the assets\ folder, as "Feature/file.ico". Core names the icon;
        /// the Add-In adapter is the one that knows how to turn it into a real image.
        /// </summary>
        public const string IconPath = "Brand/favicon.ico";

        public static void Execute(INotifier notifier, string projectName)
        {
            notifier.Info(Title, $"\n\nHello world! {projectName ?? "No project"}");
        }
    }
}
