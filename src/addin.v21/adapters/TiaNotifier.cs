using Siemens.Engineering;
using Siemens.Engineering.AddIn;

using Core;

namespace AddIn.Adapters
{
    internal sealed class TiaNotifier : INotifier
    {
        private readonly TiaPortal _tiaPortal;

        public TiaNotifier(TiaPortal tiaPortal) => _tiaPortal = tiaPortal;

        private void Show(NotificationIcon icon, string caption, string message) =>
            _tiaPortal.GetService<MessageBoxProvider>().ShowNotification(icon, caption, message);

        public void Success(string caption, string message) => Show(NotificationIcon.Success, caption, message);
        public void Info(string caption, string message) => Show(NotificationIcon.Information, caption, message);
        public void Warning(string caption, string message) => Show(NotificationIcon.Warning, caption, message);
        public void Error(string caption, string message) => Show(NotificationIcon.Error, caption, message);
    }
}
