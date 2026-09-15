using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Alarm.TextLists;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

using AddIn.Shared.Adapters;

using Core.Checks;
using Core.Config;

namespace AddIn.Adapters
{
    /// <summary>
    /// Walks whatever was selected in the project tree and names what it holds in the terms
    /// the coding-style check reads.
    ///
    /// Identical in V20 and V21 and duplicated for the usual reason: it touches Siemens
    /// types, and the two Add-In assemblies have different identities. Nothing Siemens
    /// leaves this file; the checker in Core is handed names and strings.
    ///
    /// One public entry per kind of node the menu offers the check on - the project, a PLC,
    /// a software unit, each family's folders, each family's objects - and every one of them
    /// takes the whole selection, so a multiple selection is checked in one run.
    ///
    /// **Only names an engineer chose are collected.** System blocks, system types, system
    /// constants, system text lists and the default tag table are all named by TIA, so a
    /// rule could only ever fail them, and nobody reading the report could act on it.
    ///
    /// **Reading the tree is not guarded.** A folder that could not be read would otherwise
    /// drop out of the report and leave it looking complete, which is the worst result a
    /// check can give; failing the walk lets the action say what went wrong. What is guarded
    /// is what may legitimately be absent: software on a device item that has none, software
    /// units on an S7-1200, and the interface of a block that will not show one.
    ///
    /// **Where an object lives is three answers, not one**: its PLC, its software unit and
    /// the folders in between. They are found together, whether the walk starts high and
    /// goes down or starts at an object and climbs through Parent, so an object reports the
    /// same place whichever menu entry reached it.
    ///
    /// **An interface is read by exporting the object, and never during the walk.** What an
    /// object carries here is a delegate; the checker calls it only for the objects whose
    /// name matched a rule that expects something inside, so a project-wide check exports
    /// what the configuration asks about rather than everything it finds.
    /// </summary>
    internal static class TiaCheckedObjects
    {
        private const string Separator = "/";

        // Deep enough for any tree TIA lets an engineer build, and a guarantee that a Parent
        // chain which somehow loops cannot hold TIA's thread forever.
        private const int MaxParentSteps = 64;

        /// <summary>Numbers the exported files, so no two share a name inside one run's folder.</summary>
        private static int _exports;

        /// <summary>
        /// Where something sits: its PLC, its software unit - empty for the general program -
        /// and the folders under one of those two.
        ///
        /// The three are kept apart rather than joined into a path because the report shows
        /// them as their own columns: a project holds several PLCs and a PLC several units,
        /// each repeating the same folder names, and a single string could be filtered on as
        /// a whole or not at all.
        /// </summary>
        private struct Location
        {
            private Location(string plc, string unit, string folders)
            {
                Plc = plc;
                Unit = unit;
                Folders = folders;
            }

            public readonly string Plc;
            public readonly string Unit;
            public readonly string Folders;

            public static Location In(PlcSoftware plc) =>
                new Location(plc?.Name ?? string.Empty, string.Empty, string.Empty);

            /// <summary>The same PLC, inside one of its units, back at the root of its folders.</summary>
            public Location InUnit(string unit) => new Location(Plc, unit ?? string.Empty, string.Empty);

            public Location InFolder(string name) =>
                string.IsNullOrEmpty(name) ? this : new Location(Plc, Unit, Join(Folders, name));

            public static Location Of(string plc, string unit, IEnumerable<string> folders) =>
                new Location(plc ?? string.Empty, unit ?? string.Empty, string.Join(Separator, folders));
        }

        // ---- Containers ---------------------------------------------------------------------

        /// <summary>Every PLC in the project, wherever its device sits among the device groups.</summary>
        public static List<CheckedObject> FromProjects(IEnumerable<Project> projects, ExportScratch scratch)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (projects == null) return found;

            foreach (Project project in projects)
            {
                if (project == null) continue;

                foreach (PlcSoftware plc in PlcsOf(project)) Plc(plc, scratch, found);
            }

            return found;
        }

        /// <summary>The PLC each selected device item carries. An item without one contributes nothing.</summary>
        public static List<CheckedObject> FromPlcs(IEnumerable<DeviceItem> deviceItems, ExportScratch scratch)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (deviceItems == null) return found;

            foreach (DeviceItem item in deviceItems)
            {
                PlcSoftware plc = SoftwareOf(item);
                if (plc != null) Plc(plc, scratch, found);
            }

            return found;
        }

        public static List<CheckedObject> FromUnits(IEnumerable<PlcUnitBase> units, ExportScratch scratch)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (units == null) return found;

            foreach (PlcUnitBase unit in units)
            {
                if (unit != null) Unit(unit, LocationOf(unit.Parent), scratch, found);
            }

            return found;
        }

        // ---- Folders: the system root and a user folder alike --------------------------------

        public static List<CheckedObject> FromBlockGroups(IEnumerable<PlcBlockGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => Blocks(group, LocationOf(group.Parent), scratch, found));

        public static List<CheckedObject> FromTechnologyObjectGroups(IEnumerable<TechnologicalInstanceDBGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => TechnologyObjects(group, LocationOf(group.Parent), found));

        public static List<CheckedObject> FromTagTableGroups(IEnumerable<PlcTagTableGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => TagTables(group, LocationOf(group.Parent), found));

        public static List<CheckedObject> FromTypeGroups(IEnumerable<PlcTypeGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => Types(group, LocationOf(group.Parent), scratch, found));

        public static List<CheckedObject> FromAlarmTextListGroups(IEnumerable<PlcAlarmTextlistGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => AlarmTextLists(group, LocationOf(group.Parent), found));

        // ---- Objects ------------------------------------------------------------------------

        /// <summary>
        /// Blocks of any kind, and technology objects too: a technology object is a block to
        /// the type system, so the menu entry on blocks is the one TIA offers on it.
        /// </summary>
        public static List<CheckedObject> FromBlocks(IEnumerable<PlcBlock> blocks, ExportScratch scratch) =>
            Each(blocks, (block, found) => Block(block, LocationOf(block.Parent), scratch, found));

        public static List<CheckedObject> FromTagTables(IEnumerable<PlcTagTable> tables, ExportScratch scratch) =>
            Each(tables, (table, found) => TagTable(table, LocationOf(table.Parent), found));

        public static List<CheckedObject> FromTypes(IEnumerable<PlcType> types, ExportScratch scratch) =>
            Each(types, (type, found) => DataType(type, LocationOf(type.Parent), scratch, found));

        public static List<CheckedObject> FromAlarmTextLists(IEnumerable<PlcAlarmTextlist> lists, ExportScratch scratch) =>
            Each(lists, (list, found) => AlarmTextList(list, LocationOf(list.Parent), found));

        private static List<CheckedObject> Each<T>(IEnumerable<T> selection, Action<T, List<CheckedObject>> walk)
            where T : class
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (selection == null) return found;

            foreach (T item in selection)
            {
                if (item != null) walk(item, found);
            }

            return found;
        }

        // ---- The walk -----------------------------------------------------------------------

        private static void Plc(PlcSoftware plc, ExportScratch scratch, List<CheckedObject> found)
        {
            Location here = Location.In(plc);

            Blocks(plc.BlockGroup, here, scratch, found);
            TechnologyObjects(plc.TechnologicalObjectGroup, here, found);
            TagTables(plc.TagTableGroup, here, found);
            Types(plc.TypeGroup, here, scratch, found);
            AlarmTextLists(plc.PlcAlarmTextlistGroup, here, found);

            foreach (PlcUnitBase unit in UnitsOf(plc)) Unit(unit, here, scratch, found);
        }

        /// <summary>
        /// A unit repeats four of the PLC's group roots with the same types; technology
        /// objects stay at PLC level. Its name is the row's unit rather than part of its
        /// path, so a folder called 03-ALL in two units is still two places.
        /// </summary>
        private static void Unit(PlcUnitBase unit, Location parent, ExportScratch scratch, List<CheckedObject> found)
        {
            if (unit == null) return;

            Location here = parent.InUnit(unit.Name);

            Blocks(unit.BlockGroup, here, scratch, found);
            TagTables(unit.TagTableGroup, here, found);
            Types(unit.TypeGroup, here, scratch, found);
            AlarmTextLists(unit.PlcAlarmTextlistGroup, here, found);
        }

        private static void Blocks(PlcBlockGroup group, Location parent, ExportScratch scratch, List<CheckedObject> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcBlock block in group.Blocks) Block(block, here, scratch, found);
            foreach (PlcBlockUserGroup child in group.Groups) Blocks(child, here, scratch, found);

            // SystemBlockGroups is deliberately not walked: TIA fills and names it.
        }

        private static void Block(PlcBlock block, Location where, ExportScratch scratch, List<CheckedObject> found)
        {
            if (block == null) return;

            TechnologicalInstanceDB technologyObject = block as TechnologicalInstanceDB;
            if (technologyObject != null)
            {
                TechnologyObject(technologyObject, where, found);
                return;
            }

            found.Add(new CheckedObject(
                ObjectFamily.Blocks, BlockType(block), block.Name,
                where.Plc, where.Unit, where.Folders,
                memberSource: InterfaceOf(block, scratch)));
        }

        /// <summary>
        /// How this block's own interface is read, or null when it has none of its own.
        ///
        /// **An instance DB's members are its FB's**, and are checked on the FB; naming them
        /// again here would report one engineer's decision once per instance. **An array DB
        /// holds elements of one type** rather than named members, and the one name in its
        /// export is the DB's own. Everything else - an OB, an FC, an FB, a global DB - owns
        /// what is inside it.
        /// </summary>
        private static Func<CheckedMembers> InterfaceOf(PlcBlock block, ExportScratch scratch)
        {
            if (block is InstanceDB || block is ArrayDB) return null;

            return () => Exported(
                "block",
                () => block.IsKnowHowProtected,
                file => block.Export(file, ExportOptions.None),
                scratch);
        }

        /// <summary>
        /// The configuration's name for a block's kind. Anything the contract does not name
        /// keeps its runtime type name, which the checker then reports as not configured -
        /// the honest answer for it.
        /// </summary>
        private static string BlockType(PlcBlock block)
        {
            if (block is OB) return CodingStyleNames.OB;
            if (block is FC) return CodingStyleNames.FC;
            if (block is FB) return CodingStyleNames.FB;
            if (block is GlobalDB) return CodingStyleNames.GlobalDB;
            if (block is ArrayDB) return CodingStyleNames.ArrayDB;
            if (block is InstanceDB) return CodingStyleNames.InstanceDB;

            return block.GetType().Name;
        }

        private static void TechnologyObjects(TechnologicalInstanceDBGroup group, Location parent, List<CheckedObject> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
                TechnologyObject(technologyObject, here, found);

            foreach (TechnologicalInstanceDBUserGroup child in group.Groups) TechnologyObjects(child, here, found);
        }

        /// <summary>
        /// No interface: what a technology object holds is defined by Siemens, down to the
        /// name of every member, so there is nothing an engineer could be asked to rename.
        /// </summary>
        private static void TechnologyObject(TechnologicalInstanceDB technologyObject, Location where, List<CheckedObject> found)
        {
            if (technologyObject == null) return;

            found.Add(new CheckedObject(
                ObjectFamily.TechnologyObjects, CodingStyleNames.TechnologicalInstanceDB,
                technologyObject.Name, where.Plc, where.Unit, where.Folders));
        }

        private static void TagTables(PlcTagTableGroup group, Location parent, List<CheckedObject> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcTagTable table in group.TagTables) TagTable(table, here, found);
            foreach (PlcTagTableUserGroup child in group.Groups) TagTables(child, here, found);
        }

        /// <summary>
        /// The one family read straight out of the object model: a tag table's tags and
        /// constants are names with nothing nested inside them, so there is nothing an export
        /// would add and no reason to write a file.
        /// </summary>
        private static void TagTable(PlcTagTable table, Location where, List<CheckedObject> found)
        {
            // The default tag table cannot be renamed, so its name is not the engineer's.
            if (table == null || table.IsDefault) return;

            List<CheckedMember> members = new List<CheckedMember>();

            foreach (PlcTag tag in table.Tags)
            {
                if (tag != null) members.Add(new CheckedMember(CodingStyleNames.Tag, tag.Name));
            }

            // User constants only: system constants are named by TIA after the hardware.
            foreach (PlcUserConstant constant in table.UserConstants)
            {
                if (constant != null) members.Add(new CheckedMember(CodingStyleNames.UserConstant, constant.Name));
            }

            found.Add(new CheckedObject(
                ObjectFamily.TagTables, CodingStyleNames.PlcTagTable, table.Name,
                where.Plc, where.Unit, where.Folders, members));
        }

        private static void Types(PlcTypeGroup group, Location parent, ExportScratch scratch, List<CheckedObject> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcType type in group.Types) DataType(type, here, scratch, found);
            foreach (PlcTypeUserGroup child in group.Groups) Types(child, here, scratch, found);

            // SystemTypeGroups is deliberately not walked, for the same reason as system blocks.
        }

        private static void DataType(PlcType type, Location where, ExportScratch scratch, List<CheckedObject> found)
        {
            if (type == null) return;

            found.Add(new CheckedObject(
                ObjectFamily.Types,
                type is PlcStruct ? CodingStyleNames.PlcStruct : type.GetType().Name,
                type.Name, where.Plc, where.Unit, where.Folders,
                memberSource: () => Exported(
                    "PLC data type",
                    () => type.IsKnowHowProtected,
                    file => type.Export(file, ExportOptions.None),
                    scratch)));
        }

        /// <summary>
        /// The group has no folders and no name of its own in the object model, so a list's
        /// path is the PLC or unit it belongs to.
        /// </summary>
        private static void AlarmTextLists(PlcAlarmTextlistGroup group, Location parent, List<CheckedObject> found)
        {
            if (group == null) return;

            foreach (PlcAlarmUserTextlist list in group.PlcAlarmUserTextlists) AlarmTextList(list, parent, found);
        }

        /// <summary>User text lists only: a system text list is named by TIA.</summary>
        private static void AlarmTextList(PlcAlarmTextlist list, Location where, List<CheckedObject> found)
        {
            if (!(list is PlcAlarmUserTextlist)) return;

            found.Add(new CheckedObject(
                ObjectFamily.AlarmTextLists, CodingStyleNames.AlarmTexts, list.Name,
                where.Plc, where.Unit, where.Folders));
        }

        // ---- Reading an interface -------------------------------------------------------------

        /// <summary>
        /// Exports one object, reads the interface out of it and deletes the file again.
        ///
        /// **The object model cannot answer this question**, which is the whole reason for
        /// writing a file at all: its Member carries a name and nothing else, so a member
        /// declared inside a Struct arrives as its parent's name and its own joined by a dot,
        /// and every one of them fails a rule that reads a single name.
        ///
        /// **Nothing that goes wrong here stops the check.** A know-how protected object, a
        /// block TIA refuses to export because it is not consistent, a folder that could not
        /// be created: each comes back as a reason, and the checker turns it into one skipped
        /// row naming the object. **Nothing is compiled to make an export succeed** - that
        /// would change the project behind the operator's back, on a menu entry that says it
        /// checks names.
        ///
        /// **The file goes as soon as it has been read**, whatever happened, and the run's
        /// folder goes with it: this is somebody's source code.
        /// </summary>
        private static CheckedMembers Exported(
            string what, Func<bool> knowHowProtected, Action<FileInfo> export, ExportScratch scratch)
        {
            try
            {
                if (knowHowProtected())
                    return CheckedMembers.Unreadable(
                        "The " + what + " is know-how protected, so its interface cannot be read.");
            }
            catch (Exception exception)
            {
                return CheckedMembers.Unreadable("TIA would not say whether the " + what +
                                                 " is know-how protected: " + exception.Message);
            }

            if (!scratch.Ready) return CheckedMembers.Unreadable(scratch.Problem);

            string path = scratch.FileFor(Interlocked.Increment(ref _exports));

            try
            {
                export(new FileInfo(path));

                using (FileStream stream = File.OpenRead(path))
                {
                    string problem;
                    IReadOnlyList<CheckedMember> members = SimaticMlInterface.Read(stream, out problem);

                    return members == null
                        ? CheckedMembers.Unreadable(problem)
                        : CheckedMembers.Found(members);
                }
            }
            catch (Exception exception)
            {
                return CheckedMembers.Unreadable("TIA would not export the " + what + ": " + exception.Message);
            }
            finally
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception)
                {
                    // The run's folder is deleted whole when the check ends, so a file that
                    // is momentarily locked is not worth failing a report over.
                }
            }
        }

        // ---- Finding the software -----------------------------------------------------------

        /// <summary>
        /// A project's PLCs, from the top-level devices and from every device folder. A set,
        /// because a device reachable two ways must still be checked once.
        /// </summary>
        private static IEnumerable<PlcSoftware> PlcsOf(Project project)
        {
            List<PlcSoftware> plcs = new List<PlcSoftware>();
            HashSet<PlcSoftware> seen = new HashSet<PlcSoftware>();

            Devices(project.Devices, plcs, seen);
            Devices(project.UngroupedDevicesGroup?.Devices, plcs, seen);

            foreach (DeviceUserGroup group in project.DeviceGroups) DeviceGroup(group, plcs, seen);

            return plcs;
        }

        private static void DeviceGroup(DeviceUserGroup group, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (group == null) return;

            Devices(group.Devices, plcs, seen);
            foreach (DeviceUserGroup child in group.Groups) DeviceGroup(child, plcs, seen);
        }

        private static void Devices(DeviceComposition devices, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (devices == null) return;

            foreach (Device device in devices)
            {
                if (device == null) continue;
                foreach (DeviceItem item in device.DeviceItems) DeviceItems(item, plcs, seen);
            }
        }

        private static void DeviceItems(DeviceItem item, List<PlcSoftware> plcs, HashSet<PlcSoftware> seen)
        {
            if (item == null) return;

            PlcSoftware plc = SoftwareOf(item);
            if (plc != null && seen.Add(plc)) plcs.Add(plc);

            foreach (DeviceItem child in item.DeviceItems) DeviceItems(child, plcs, seen);
        }

        /// <summary>
        /// The PLC a device item carries, or null. Asked of every module in a device, and
        /// most of them are not a CPU - a refusal there is an answer, not a failure.
        /// </summary>
        private static PlcSoftware SoftwareOf(DeviceItem item)
        {
            if (item == null) return null;

            try
            {
                return item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Units and safety units alike: both hold names an engineer chose.
        ///
        /// Only the S7-1500 family has software units. On an S7-1200 the provider service is
        /// not there, which is a normal answer and not a failure - the one place a missing
        /// piece of the tree is expected rather than suspicious.
        /// </summary>
        private static IEnumerable<PlcUnitBase> UnitsOf(PlcSoftware plc)
        {
            PlcUnitSystemGroup unitGroup;
            try
            {
                unitGroup = plc.GetService<PlcUnitProvider>()?.UnitGroup;
            }
            catch (Exception)
            {
                unitGroup = null;
            }

            List<PlcUnitBase> units = new List<PlcUnitBase>();
            if (unitGroup == null) return units;

            foreach (PlcUnit unit in unitGroup.Units) units.Add(unit);
            foreach (PlcSafetyUnit unit in unitGroup.SafetyUnits) units.Add(unit);

            return units;
        }

        // ---- Where something sits -----------------------------------------------------------

        /// <summary>
        /// Where whatever sits under <paramref name="start"/> lives, built upwards: folder
        /// names, then the software unit if there is one, then the PLC. Links it does not
        /// know - the unit folder between a unit and its PLC, say - are stepped over rather
        /// than ending the walk, since which ones TIA puts in the chain is not something to
        /// bet on.
        /// </summary>
        private static Location LocationOf(IEngineeringObject start)
        {
            List<string> folders = new List<string>();
            string unit = null;
            string plc = null;

            IEngineeringObject current = start;
            for (int step = 0; current != null && step < MaxParentSteps; step++)
            {
                PlcSoftware software = current as PlcSoftware;
                if (software != null)
                {
                    plc = software.Name;
                    break;
                }

                // Hardware or the project: the software was left behind, so stop here.
                if (current is HardwareObject || current is Project) break;

                // A unit ends the folders and starts the climb towards its PLC: everything
                // above it belongs to the PLC, not to the folders of this object.
                PlcUnitBase unitLink = current as PlcUnitBase;
                if (unitLink != null) unit = unitLink.Name;
                else
                {
                    string name = FolderName(current);
                    if (name != null) folders.Add(name);
                }

                current = current.Parent;
            }

            folders.Reverse();
            return Location.Of(plc, unit, folders);
        }

        /// <summary>The name a link contributes to a path, or null for one that contributes none.</summary>
        private static string FolderName(IEngineeringObject link)
        {
            if (link is PlcBlockGroup) return ((PlcBlockGroup)link).Name;
            if (link is PlcTagTableGroup) return ((PlcTagTableGroup)link).Name;
            if (link is PlcTypeGroup) return ((PlcTypeGroup)link).Name;
            if (link is TechnologicalInstanceDBGroup) return ((TechnologicalInstanceDBGroup)link).Name;

            return null;
        }

        private static string Join(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent)) return name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return parent;

            return parent + Separator + name;
        }
    }
}
