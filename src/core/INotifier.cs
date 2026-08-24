
namespace core
{
    public interface INotifier
    {
        void Success(string caption, string message);
        void Info(string caption, string message);
        void Warning(string caption, string message);
        void Error(string caption, string message);
    }
}
