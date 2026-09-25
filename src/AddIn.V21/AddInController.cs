using AddIn.Adapters;
using AddIn.Shared.Actions;
using AddIn.Shared.Adapters;
using Core;
using Core.Checks;
using Core.Config;
using Core.Logging;
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
        /// <summary>
        /// Which TIA version this Add-In was built for, which decides the executable a
        /// version-specific satellite is launched from.
        ///
        /// **A literal, because no Siemens API answers it** - neither `TiaPortal` nor the
        /// whole `Siemens.Engineering.AddIn` namespace exposes the running version. The one
        /// thing that knows is the project this file is compiled in.
        /// </summary>
        private const string TiaVersion = "V21";

        /// <summary>
        /// The log every click of this Add-In writes to - one per TIA version, because the two
        /// Add-Ins are two packages TIA loads apart, where the core updater's two executables
        /// are one application built twice and share a file.
        /// </summary>
        private const string LogName = LogPaths.AddInV21;

        /// <summary>How many selected objects a click's first line names before it counts the rest.</summary>
        private const int NamesShown = 10;

        private readonly TiaPortal _tiaPortal;
        private readonly ITiaNotifier _notifier;
        private readonly IProcessLauncher _launcher;
        private readonly ITiaBusy _busy;

        public AddInController(TiaPortal tiaPortal) : base(Product.Title)
        {
            _tiaPortal = tiaPortal;
            _notifier = new TiaNotifier(_tiaPortal);
            _launcher = new ProcessLauncher();
            _busy = new TiaBusy(_tiaPortal);
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
                (menuSelectionProvider, click) => ConfigEditorAction.Execute(
                    click.Notifier, click.Launcher, ProjectDirectory(), ProjectName()));

            // Beside the editor, and for the same reason: the folder belongs to the project
            // rather than to anything selected inside it.
            AddAction<Project>(
                menuAddInRoot,
                OpenProjectFolderAction.Title,
                OpenProjectFolderAction.IconPath,
                (menuSelectionProvider, click) => OpenProjectFolderAction.Execute(
                    click.Notifier, click.Launcher, ProjectDirectory()));

            // The core updater is also here, where no PLC is selected: the window lists the
            // project's PLCs and reads nothing until Load is pressed, so choosing one is a
            // drop-down rather than a right-click.
            AddAction<Project>(
                menuAddInRoot,
                CoreUpdaterAction.Title,
                CoreUpdaterAction.IconPath,
                (menuSelectionProvider, click) => CoreUpdaterAction.ExecuteForProject(
                    click.Notifier, click.Launcher, TiaVersion, ProjectFile()));

            // The coding-style check on the project root checks every PLC in it. The same
            // entry on narrower nodes is registered below, after the actions that belong
            // to those nodes.
            AddCheck<Project>(menuAddInRoot, "project", "projects", TiaCheckedObjects.FromProjects);

            // On a device rather than the project root: the hierarchy is created inside a PLC.
            AddAction<DeviceItem>(
                menuAddInRoot,
                CreateProjectHierarchyAction.Title,
                CreateProjectHierarchyAction.IconPath,
                (menuSelectionProvider, click) =>
                {
                    DeviceItem deviceItem = menuSelectionProvider?.GetSelection<DeviceItem>().FirstOrDefault();

                    ConfigLoadResult result = ConfigLoader.LoadFromProject(ProjectDirectory());
                    if (!result.Succeeded)
                    {
                        click.Notifier.Error(CreateProjectHierarchyAction.Title, result.Error);
                        return;
                    }

                    CreateProjectHierarchyAction.Execute(
                        click.Notifier,
                        result.Config.ProjectConfig?.Hierarchy,
                        TiaGroupNode.TargetsFor(deviceItem));
                });

            // And on the PLC, which is the entry that names one: the operator right-clicked
            // the controller, so the window opens on it rather than on whichever comes first.
            // This one hands nothing over - the window attaches to TIA Portal itself - so all
            // the Add-In supplies is which of the two executables its TIA version needs.
            AddAction<DeviceItem>(
                menuAddInRoot,
                CoreUpdaterAction.Title,
                CoreUpdaterAction.IconPath,
                (menuSelectionProvider, click) =>
                {
                    DeviceItem selected = menuSelectionProvider?.GetSelection<DeviceItem>().FirstOrDefault();

                    // The name, not the object: the satellite finds the PLC again on its own
                    // side, and an engineering object could not cross to another process
                    // anyway. TiaProjectPlaces answers null for a device item that is not a
                    // CPU, which the action turns into a sentence.
                    // The project file goes with it, and it is not decoration: with two TIA
                    // Portals open the window could not tell which one had launched it, and
                    // this is the one thing both ends name identically.
                    CoreUpdaterAction.Execute(
                        click.Notifier, click.Launcher, TiaVersion,
                        TiaProjectPlaces.SoftwareOf(selected)?.Name, ProjectFile());
                });

            // On data blocks, and it works on a multiple selection: GetSelection returns
            // the whole thing, so several blocks travel to the satellite in one run.
            AddAction<DataBlock>(
                menuAddInRoot,
                DataBlockSnapshotAction.Title,
                DataBlockSnapshotAction.IconPath,
                (menuSelectionProvider, click) =>
                {
                    List<DataBlock> blocks =
                        menuSelectionProvider?.GetSelection<DataBlock>().ToList() ?? new List<DataBlock>();

                    DataBlockSnapshotAction.Execute(
                        click.Notifier,
                        click.Launcher,
                        ProjectDirectory(),
                        TiaPlcSelection.From(blocks));
                });

            // The coding-style check everywhere else it makes sense: a PLC, a software unit,
            // any folder of the five families, and the objects themselves. One entry per
            // kind of node, each on a base type so a system folder and a user folder, or an
            // FC and a DB, share one registration. Technology objects are deliberately not
            // registered on their own: a TechnologicalInstanceDB is a PlcBlock, so it would
            // show the entry twice.
            AddCheck<DeviceItem>(menuAddInRoot, "PLC", "PLCs", TiaCheckedObjects.FromPlcs);
            AddCheck<PlcUnitBase>(menuAddInRoot, "software unit", "software units", TiaCheckedObjects.FromUnits);
            AddCheck<PlcBlockGroup>(menuAddInRoot, "block folder", "block folders", TiaCheckedObjects.FromBlockGroups);
            AddCheck<TechnologicalInstanceDBGroup>(menuAddInRoot, "technology object folder", "technology object folders", TiaCheckedObjects.FromTechnologyObjectGroups);
            AddCheck<PlcTagTableGroup>(menuAddInRoot, "tag table folder", "tag table folders", TiaCheckedObjects.FromTagTableGroups);
            AddCheck<PlcTypeGroup>(menuAddInRoot, "PLC data type folder", "PLC data type folders", TiaCheckedObjects.FromTypeGroups);
            AddCheck<PlcAlarmTextlistGroup>(menuAddInRoot, "PLC alarm text lists", "PLC alarm text list groups", TiaCheckedObjects.FromAlarmTextListGroups);
            AddCheck<PlcBlock>(menuAddInRoot, "block", "blocks", TiaCheckedObjects.FromBlocks);
            AddCheck<PlcTagTable>(menuAddInRoot, "tag table", "tag tables", TiaCheckedObjects.FromTagTables);
            AddCheck<PlcType>(menuAddInRoot, "PLC data type", "PLC data types", TiaCheckedObjects.FromTypes);
            AddCheck<PlcAlarmTextlist>(menuAddInRoot, "text list", "text lists", TiaCheckedObjects.FromAlarmTextLists);

            // The export, on the same nodes as the check: exporting one folder is the same
            // gesture as checking it. Alarm text lists are not here - Openness exports every
            // list of a PLC at once, so its entries hand back one workbook per PLC.
            AddExport<Project>(menuAddInRoot, "project", "projects", TiaExportObjects.FromProjects);
            AddExport<DeviceItem>(menuAddInRoot, "PLC", "PLCs", TiaExportObjects.FromPlcs);
            AddExport<PlcUnitBase>(menuAddInRoot, "software unit", "software units", TiaExportObjects.FromUnits);
            AddExport<PlcBlockGroup>(menuAddInRoot, "block folder", "block folders", TiaExportObjects.FromBlockGroups);
            AddExport<TechnologicalInstanceDBGroup>(menuAddInRoot, "technology object folder", "technology object folders", TiaExportObjects.FromTechnologyObjectGroups);
            AddExport<PlcTagTableGroup>(menuAddInRoot, "tag table folder", "tag table folders", TiaExportObjects.FromTagTableGroups);
            AddExport<PlcTypeGroup>(menuAddInRoot, "PLC data type folder", "PLC data type folders", TiaExportObjects.FromTypeGroups);
            AddExport<PlcBlock>(menuAddInRoot, "block", "blocks", TiaExportObjects.FromBlocks);
            AddExport<PlcTagTable>(menuAddInRoot, "tag table", "tag tables", TiaExportObjects.FromTagTables);
            AddExport<PlcType>(menuAddInRoot, "PLC data type", "PLC data types", TiaExportObjects.FromTypes);
            AddExport<PlcAlarmTextlistGroup>(menuAddInRoot, "PLC alarm text lists", "PLC alarm text list groups", TiaExportObjects.FromAlarmTextListGroups);
            AddExport<PlcAlarmTextlist>(menuAddInRoot, "text list", "text lists", TiaExportObjects.FromAlarmTextLists);

            // On the project root, and last: it is about the framework rather than about
            // anything selected. The window itself is a separate executable on disk, so
            // all this does is launch it.
            AddAction<Project>(
                menuAddInRoot,
                AboutAction.Title,
                AboutAction.IconPath,
                (menuSelectionProvider, click) => AboutAction.Execute(click.Notifier, click.Launcher));
        }

        /// <summary>
        /// One export entry for one kind of node, shaped exactly like the check above: the
        /// selection is taken once, so the words describing it and the walk over it cannot
        /// disagree.
        /// </summary>
        private void AddExport<T>(
            ContextMenuAddInRoot root,
            string one,
            string many,
            Func<IEnumerable<T>, List<ExportItem>> walk) where T : IEngineeringObject
        {
            AddAction<T>(
                root,
                ExportObjectsAction.Title,
                ExportObjectsAction.IconPath,
                (menuSelectionProvider, click) =>
                {
                    List<T> selected = menuSelectionProvider?.GetSelection<T>().ToList() ?? new List<T>();

                    ExportObjectsAction.Execute(
                        click.Notifier,
                        _busy,
                        ProjectDirectory(),
                        Selection.Scope(selected.Count, one, many),
                        () => walk(selected));
                });
        }

        /// <summary>
        /// One coding-style entry for one kind of node. The selection is taken once, so the
        /// words describing it and the walk over it cannot disagree; the walk itself is
        /// handed to the action, which runs it only once nothing else has refused.
        /// </summary>
        private void AddCheck<T>(
            ContextMenuAddInRoot root,
            string one,
            string many,
            Func<IEnumerable<T>, ExportScratch, List<CheckedObject>> walk) where T : IEngineeringObject
        {
            AddAction<T>(
                root,
                CheckCodingStyleAction.Title,
                CheckCodingStyleAction.IconPath,
                (menuSelectionProvider, click) =>
                {
                    List<T> selected = menuSelectionProvider?.GetSelection<T>().ToList() ?? new List<T>();

                    CheckCodingStyleAction.Execute(
                        click.Notifier,
                        click.Launcher,
                        _busy,
                        ProjectDirectory(),
                        ProjectName(),
                        Selection.Scope(selected.Count, one, many),
                        scratch => walk(selected, scratch));
                });
        }
        
        /// <summary>
        /// Adds a menu entry with its icon, falling back to the plain overload when the
        /// icon is not embedded. Keeps a missing asset from breaking the whole menu.
        ///
        /// **Every click is a run of the log**, opened here rather than in each entry so that no
        /// entry can forget it: the first line says which entry and on what, and the entry is
        /// handed a notifier and a launcher that write into the same run.
        /// </summary>
        private void AddAction<T>(
            ContextMenuAddInRoot root,
            string text,
            string iconPath,
            Action<MenuSelectionProvider<T>, MenuClick> onClick) where T : IEngineeringObject
        {
            ActionItem<T>.OnClickDelegate clicked = menuSelectionProvider =>
                MenuClick.Run(
                    LogName,
                    text,
                    ProjectName(),
                    ProjectDirectory(),
                    Described(menuSelectionProvider),
                    _notifier,
                    _launcher,
                    click => onClick(menuSelectionProvider, click));

            Icon icon = Icons.Get(iconPath);

            if (icon != null)
                root.Items.AddActionItemWithIcon<T>(text, icon, clicked);
            else
                root.Items.AddActionItem<T>(text, clicked);
        }

        /// <summary>
        /// What a click was on, for the first line of its run: how many, of what, and the first
        /// few by name.
        ///
        /// **Read through the untyped attribute**, because the selection is only known as an
        /// `IEngineeringObject` here and one method must serve every kind of node. An object with
        /// no name - an alarm text list group has none - is named by its type instead, and
        /// nothing here may fail a click: a selection that cannot be described is still worked on.
        /// </summary>
        private static string Described<T>(MenuSelectionProvider<T> menuSelectionProvider) where T : IEngineeringObject
        {
            List<T> selected;

            try
            {
                selected = menuSelectionProvider?.GetSelection<T>().ToList() ?? new List<T>();
            }
            catch (Exception exception)
            {
                return "a selection that could not be read: " + exception.Message;
            }

            if (selected.Count == 0) return "nothing selected";

            // A lambda rather than the method group: T is constrained to an interface, not to a
            // class, so the variance a method group needs does not apply.
            string named = string.Join(", ", selected.Take(NamesShown).Select(item => NameOf(item)));

            return "on " + selected.Count + " " + typeof(T).Name + " - " + named +
                   (selected.Count > NamesShown ? " and " + (selected.Count - NamesShown) + " more" : string.Empty);
        }

        private static string NameOf(IEngineeringObject item)
        {
            try
            {
                string name = item?.GetAttribute("Name") as string;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (Exception)
            {
                // No Name attribute on this kind of node; its type says what it is.
            }

            return item?.GetType().Name ?? "(null)";
        }

        /// <summary>
        /// The open project's own file, <c>…\LabSlave.ap21</c> - which is what a satellite
        /// attaching through Openness matches against `TiaPortalProcess.ProjectPath` to tell
        /// one running TIA Portal from another.
        /// </summary>
        private string ProjectFile() =>
            _tiaPortal?.Projects?.FirstOrDefault()?.Path?.FullName;

        /// <summary>Directory of the open TIA project, where .plc-framework lives.</summary>
        private string ProjectDirectory() =>
            _tiaPortal?.Projects?.FirstOrDefault()?.Path?.DirectoryName;

        /// <summary>Shown in a satellite's header, so two open windows can be told apart.</summary>
        private string ProjectName() =>
            _tiaPortal?.Projects?.FirstOrDefault()?.Name;
    }
}
