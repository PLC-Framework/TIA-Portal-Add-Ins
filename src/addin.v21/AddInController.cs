using System.Drawing;
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

        public AddInController(TiaPortal tiaPortal) : base(Product.Title)
        {
            _tiaPortal = tiaPortal;
            _notifier = new TiaNotifier(_tiaPortal);
        }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot addInRootSubmenu)
        {
            AddAction<Project>(
                addInRootSubmenu,
                HelloWorldAction.Title,
                HelloWorldAction.IconPath,
                menuSelectionProvider =>
                {
                    Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
                    HelloWorldAction.Execute(_notifier, project?.Name);
                });
        }

        /// <summary>
        /// Adds a menu entry with its icon, falling back to the plain overload when the
        /// icon is not embedded. Keeps a missing asset from breaking the whole menu.
        /// </summary>
        private static void AddAction<T>(
            ContextMenuAddInRoot root,
            string text,
            string iconPath,
            ActionItem<T>.OnClickDelegate onClick) where T : IEngineeringObject
        {
            Icon icon = Icons.Get(iconPath);

            if (icon != null)
                root.Items.AddActionItemWithIcon<T>(text, icon, onClick);
            else
                root.Items.AddActionItem<T>(text, onClick);
        }
    }
}
