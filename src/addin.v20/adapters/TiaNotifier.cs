using Siemens.Engineering;

using Core.Adapters;

namespace AddIn.Adapters
{
    /// <summary>
    /// INotifier implemented against the TIA Portal V17-V20 message box.
    ///
    /// The V21 adapter is identical except for how the message box is obtained:
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
        /// The null-conditional keeps a missing message box from taking down the host
        /// process: a notification that cannot be shown is not worth crashing TIA for.
        /// </summary>
        private void Show(NotificationIcon icon, string caption, string message) =>
            _tiaPortal.GetMessageBox()?.ShowNotification(icon, caption, message);
    }
}
