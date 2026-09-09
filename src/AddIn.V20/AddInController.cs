using System.Collections.Generic;
using System.Drawing;
using System.Linq;

using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Blocks;

using AddIn.Shared.Actions;
using AddIn.Shared.Adapters;

using Core;
using Core.Config;

using AddIn.Adapters;

namespace AddIn
{
    public class AddInController : ContextMenuAddIn
    {
        private readonly TiaPortal _tiaPortal;
        private readonly ITiaNotifier _notifier;
        private readonly IProcessLauncher _launcher;

        public AddInController(TiaPortal tiaPortal) : base(Product.Title)
        {
            _tiaPortal = tiaPortal;
            _notifier = new TiaNotifier(_tiaPortal);
            _launcher = new ProcessLauncher();
        }

        protected override void BuildContextMenuItems(ContextMenuAddInRoot menuAddInRoot)
        {
            // On the project root: config.json belongs to the project rather than to any
            // one PLC. No check that the file exists - its absence is a normal state the
            // editor handles by offering to create one.
            AddAction<Project>(
                menuAddInRoot,
                ConfigEditorAction.Title,
                null,
                menuSelectionProvider => ConfigEditorAction.Execute(
                    _notifier, _launcher, ProjectDirectory(), ProjectName()));

            // On the project root, and last: it is about the framework rather than about
            // anything selected. The window itself is a separate executable in tools\, so
            // all this does is launch it.
            AddAction<Project>(
                menuAddInRoot,
                AboutAction.Title,
                AboutAction.IconPath,
                menuSelectionProvider => AboutAction.Execute(_notifier, _launcher));

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

            // On data blocks, and it works on a multiple selection: GetSelection returns
            // the whole thing, so several blocks travel to the satellite in one run.
            AddAction<DataBlock>(
                menuAddInRoot,
                DataBlockSnapshotAction.Title,
                null,
                menuSelectionProvider =>
                {
                    List<DataBlock> blocks =
                        menuSelectionProvider?.GetSelection<DataBlock>().ToList() ?? new List<DataBlock>();

                    DataBlockSnapshotAction.Execute(
                        _notifier,
                        _launcher,
                        ProjectDirectory(),
                        TiaPlcSelection.From(blocks));
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

        /// <summary>Shown in a satellite's header, so two open windows can be told apart.</summary>
        private string ProjectName() =>
            _tiaPortal?.Projects?.FirstOrDefault()?.Name;
    }
}
