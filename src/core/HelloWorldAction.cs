
namespace core
{
    public static class HelloWorldAction
    {
        public const string Title = "Hello World";

        public static void Execute(INotifier notifier, string projectName)
        {
            notifier.Info(Title, $"\n\nHello world! {projectName ?? "No project"}");
        }
    }
}
