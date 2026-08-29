using Siemens.Engineering;
using Siemens.Engineering.AddIn;
using Core.Adapters;

namespace AddIn.Adapters
{
    /// <summary>
    /// INotifier implemented against the TIA Portal V21 message box.
    ///
    /// The V20 adapter is identical except for how the message box is obtained:
    /// V21 dropped TiaPortal.GetMessageBox() in favour of the generic service
    /// lookup. The two cannot be unified: V20's MessageBox does not implement
    /// IEngineeringService, so GetService&lt;MessageBox&gt;() does not compile.
    /// </summary>
    internal sealed class TiaNotifier : INotifier
    {
        private readonly TiaPortal _tiaPortal;

        public TiaNotifier(TiaPortal tiaPortal) => _tiaPortal = tiaPortal;

        public void Success(string caption, string message) => Show(NotificationIcon.Success, caption, message);
        public void Info(string caption, string message) => Show(NotificationIcon.Information, caption, message);
        public void Warning(string caption, string message) => Show(NotificationIcon.Warning, caption, message);
        public void Error(string caption, string message) => Show(NotificationIcon.Error, caption, message);

        /// <summary>
        /// GetService&lt;T&gt;() is documented by Siemens to return null when the service is
        /// unavailable (a TIA session started without a user interface, for instance),
        /// so the null-conditional here is required, not merely defensive.
        /// </summary>
        private void Show(NotificationIcon icon, string caption, string message) =>
            _tiaPortal.GetService<MessageBoxProvider>()?.ShowNotification(icon, caption, message);
    }
}
