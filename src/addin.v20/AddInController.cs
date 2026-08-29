using System.Linq;

using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;

using Core;
using AddIn.Adapters;

namespace AddIn
{
    public class AddInController : ContextMenuAddIn
    {
        private readonly TiaPortal _tiaPortal;
        private readonly INotifier _notifier;

        public AddInController(TiaPortal tiaPortal) : base(Constants.Title)
        {
            _tiaPortal = tiaPortal;
            _notifier = new TiaNotifier(_tiaPortal);
        }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            addInRootSubmenu.Items.AddActionItem<Project> (
                HelloWorldAction.Title,
                (MenuSelectionProvider<Project> menuSelectionProvider) =>
                {
                    Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
                    HelloWorldAction.Execute(_notifier, project?.Name);
                });
        }
    }
}
