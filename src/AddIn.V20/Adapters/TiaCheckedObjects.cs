using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using Siemens.Engineering;
using Siemens.Engineering.HW;
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

using Location = AddIn.Adapters.TiaProjectPlaces.Location;

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
    /// rule could only ever fail them, and nobody reading the report could act on it. The
    /// export walk beside this one keeps more, because a backup of the project's code is a
    /// different question from a naming report.
    ///
    /// **Reading the tree is not guarded.** A folder that could not be read would otherwise
    /// drop out of the report and leave it looking complete, which is the worst result a
    /// check can give; failing the walk lets the action say what went wrong. What is guarded
    /// is what may legitimately be absent: software on a device item that has none, software
    /// units on an S7-1200, and the interface of a block that will not show one.
    ///
    /// **Where an object lives comes from <see cref="TiaProjectPlaces"/>**, which the export
    /// walk uses too: the report's columns and the exported folders must agree.
    ///
    /// **An interface is read by exporting the object, and never during the walk.** What an
    /// object carries here is a delegate; the checker calls it only for the objects whose
    /// name matched a rule that expects something inside, so a project-wide check exports
    /// what the configuration asks about rather than everything it finds.
    /// </summary>
    internal static class TiaCheckedObjects
    {
        /// <summary>Numbers the exported files, so no two share a name inside one run's folder.</summary>
        private static int _exports;

        // ---- Containers ---------------------------------------------------------------------

        /// <summary>Every PLC in the project, wherever its device sits among the device groups.</summary>
        public static List<CheckedObject> FromProjects(IEnumerable<Project> projects, ExportScratch scratch)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (projects == null) return found;

            foreach (Project project in projects)
            {
                if (project == null) continue;

                foreach (PlcSoftware plc in TiaProjectPlaces.PlcsOf(project)) Plc(plc, scratch, found);
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
                PlcSoftware plc = TiaProjectPlaces.SoftwareOf(item);
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
                if (unit != null) Unit(unit, TiaProjectPlaces.LocationOf(unit.Parent), scratch, found);
            }

            return found;
        }

        // ---- Folders: the system root and a user folder alike --------------------------------

        public static List<CheckedObject> FromBlockGroups(IEnumerable<PlcBlockGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => Blocks(group, TiaProjectPlaces.LocationOf(group.Parent), scratch, found));

        public static List<CheckedObject> FromTechnologyObjectGroups(IEnumerable<TechnologicalInstanceDBGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => TechnologyObjects(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        public static List<CheckedObject> FromTagTableGroups(IEnumerable<PlcTagTableGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => TagTables(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        public static List<CheckedObject> FromTypeGroups(IEnumerable<PlcTypeGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => Types(group, TiaProjectPlaces.LocationOf(group.Parent), scratch, found));

        public static List<CheckedObject> FromAlarmTextListGroups(IEnumerable<PlcAlarmTextlistGroup> groups, ExportScratch scratch) =>
            Each(groups, (group, found) => AlarmTextLists(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        // ---- Objects ------------------------------------------------------------------------

        /// <summary>
        /// Blocks of any kind, and technology objects too: a technology object is a block to
        /// the type system, so the menu entry on blocks is the one TIA offers on it.
        /// </summary>
        public static List<CheckedObject> FromBlocks(IEnumerable<PlcBlock> blocks, ExportScratch scratch) =>
            Each(blocks, (block, found) => Block(block, TiaProjectPlaces.LocationOf(block.Parent), scratch, found));

        public static List<CheckedObject> FromTagTables(IEnumerable<PlcTagTable> tables, ExportScratch scratch) =>
            Each(tables, (table, found) => TagTable(table, TiaProjectPlaces.LocationOf(table.Parent), found));

        public static List<CheckedObject> FromTypes(IEnumerable<PlcType> types, ExportScratch scratch) =>
            Each(types, (type, found) => DataType(type, TiaProjectPlaces.LocationOf(type.Parent), scratch, found));

        public static List<CheckedObject> FromAlarmTextLists(IEnumerable<PlcAlarmTextlist> lists, ExportScratch scratch) =>
            Each(lists, (list, found) => AlarmTextList(list, TiaProjectPlaces.LocationOf(list.Parent), found));

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

            foreach (PlcUnitBase unit in TiaProjectPlaces.UnitsOf(plc)) Unit(unit, here, scratch, found);
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
    }
}
