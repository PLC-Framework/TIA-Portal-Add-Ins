using System.Drawing;
using System.Linq;

using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.HW;

using Core;
using Core.Actions;
using Core.Adapters;
using Core.Config;

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

        protected override void BuildContextMenuItems(ContextMenuAddInRoot menuAddInRoot)
        {
            AddAction<Project>(
                menuAddInRoot,
                HelloWorldAction.Title,
                HelloWorldAction.IconPath,
                menuSelectionProvider =>
                {
                    Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
                    HelloWorldAction.Execute(_notifier, project?.Name);
                });

            // On a device rather than the project root: the hierarchy is created inside a PLC.
            AddAction<DeviceItem>(
                menuAddInRoot,
                CreateProjectHierarchyAction.Title,
                CreateProjectHierarchyAction.IconPath,
                menuSelectionProvider =>
                {
                    DeviceItem deviceItem = menuSelectionProvider?.GetSelection<DeviceItem>().FirstOrDefault();

                    ConfigLoadResult result = ConfigLoader.LoadFromProject(ProjectDirectory());
                    if (!result.Succeeded)
                    {
                        _notifier.Error(CreateProjectHierarchyAction.Title, result.Error);
                        return;
                    }

                    CreateProjectHierarchyAction.Execute(
                        _notifier,
                        result.Config.ProjectConfig?.Hierarchy,
                        TiaGroupNode.TargetsFor(deviceItem));
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

        /// <summary>Directory of the open TIA project, where .plc-framework lives.</summary>
        private string ProjectDirectory() =>
            _tiaPortal?.Projects?.FirstOrDefault()?.Path?.DirectoryName;
    }
}
