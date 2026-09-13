using AddIn.Adapters;
using AddIn.Shared.Actions;
using AddIn.Shared.Adapters;
using Core;
using Core.Checks;
using Core.Config;
using Siemens.Engineering;
using Siemens.Engineering.AddIn.Menu;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW.Alarm.TextLists;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

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
                ConfigEditorAction.IconPath,
                menuSelectionProvider => ConfigEditorAction.Execute(
                    _notifier, _launcher, ProjectDirectory(), ProjectName()));

            // The coding-style check on the project root checks every PLC in it. The same
            // entry on narrower nodes is registered below, after the actions that belong
            // to those nodes.
            AddCheck<Project>(menuAddInRoot, TiaCheckedObjects.FromProjects);

            // On the project root, and last: it is about the framework rather than about
            // anything selected. The window itself is a separate executable on disk, so
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
                DataBlockSnapshotAction.IconPath,
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

            // The coding-style check everywhere else it makes sense: a PLC, a software unit,
            // any folder of the five families, and the objects themselves. One entry per
            // kind of node, each on a base type so a system folder and a user folder, or an
            // FC and a DB, share one registration. Technology objects are deliberately not
            // registered on their own: a TechnologicalInstanceDB is a PlcBlock, so it would
            // show the entry twice.
            AddCheck<DeviceItem>(menuAddInRoot, TiaCheckedObjects.FromPlcs);
            AddCheck<PlcUnitBase>(menuAddInRoot, TiaCheckedObjects.FromUnits);
            AddCheck<PlcBlockGroup>(menuAddInRoot, TiaCheckedObjects.FromBlockGroups);
            AddCheck<TechnologicalInstanceDBGroup>(menuAddInRoot, TiaCheckedObjects.FromTechnologyObjectGroups);
            AddCheck<PlcTagTableGroup>(menuAddInRoot, TiaCheckedObjects.FromTagTableGroups);
            AddCheck<PlcTypeGroup>(menuAddInRoot, TiaCheckedObjects.FromTypeGroups);
            AddCheck<PlcAlarmTextlistGroup>(menuAddInRoot, TiaCheckedObjects.FromAlarmTextListGroups);
            AddCheck<PlcBlock>(menuAddInRoot, TiaCheckedObjects.FromBlocks);
            AddCheck<PlcTagTable>(menuAddInRoot, TiaCheckedObjects.FromTagTables);
            AddCheck<PlcType>(menuAddInRoot, TiaCheckedObjects.FromTypes);
            AddCheck<PlcAlarmTextlist>(menuAddInRoot, TiaCheckedObjects.FromAlarmTextLists);
        }

        /// <summary>
        /// One coding-style entry for one kind of node. The whole selection is handed to the
        /// walk, and the walk itself is handed to the action, which runs it only once the
        /// configuration has been loaded and validated.
        /// </summary>
        private void AddCheck<T>(
            ContextMenuAddInRoot root,
            Func<IEnumerable<T>, List<CheckedObject>> walk) where T : IEngineeringObject
        {
            AddAction<T>(
                root,
                CheckCodingStyleAction.Title,
                CheckCodingStyleAction.IconPath,
                menuSelectionProvider => CheckCodingStyleAction.Execute(
                    _notifier,
                    ProjectDirectory(),
                    () => walk(menuSelectionProvider?.GetSelection<T>() ?? Enumerable.Empty<T>())));
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
