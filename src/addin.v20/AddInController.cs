using System.Linq;

using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;

using Core;
using Addin.Adapters;

namespace Addin
{
    public class AddInController : ContextMenuAddIn
    {
        private const string Title = "PLC-Framework";
        private readonly TiaPortal _tiaPortal;
        private TiaNotifier _notifier;

        public AddInController(TiaPortal tiaPortal) : base(Title)
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
