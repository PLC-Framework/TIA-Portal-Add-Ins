using System;
using System.Collections.Generic;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Alarm.TextLists;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Blocks.Interface;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

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
    /// units on an S7-1200, and the members of a block that will not show them.
    ///
    /// **Every path starts with the PLC's name**, because a project holds several and the
    /// same folder exists in each. It is built downwards when the walk starts high and
    /// upwards through Parent when it starts at a folder or an object, to the same shape,
    /// so a block reports one path whichever menu entry reached it.
    /// </summary>
    internal static class TiaCheckedObjects
    {
        private const string Separator = "/";

        // Deep enough for any tree TIA lets an engineer build, and a guarantee that a Parent
        // chain which somehow loops cannot hold TIA's thread forever.
        private const int MaxParentSteps = 64;

        // ---- Containers ---------------------------------------------------------------------

        /// <summary>Every PLC in the project, wherever its device sits among the device groups.</summary>
        public static List<CheckedObject> FromProjects(IEnumerable<Project> projects)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (projects == null) return found;

            foreach (Project project in projects)
            {
                if (project == null) continue;

                foreach (PlcSoftware plc in PlcsOf(project)) Plc(plc, found);
            }

            return found;
        }

        /// <summary>The PLC each selected device item carries. An item without one contributes nothing.</summary>
        public static List<CheckedObject> FromPlcs(IEnumerable<DeviceItem> deviceItems)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (deviceItems == null) return found;

            foreach (DeviceItem item in deviceItems)
            {
                PlcSoftware plc = SoftwareOf(item);
                if (plc != null) Plc(plc, found);
            }

            return found;
        }

        public static List<CheckedObject> FromUnits(IEnumerable<PlcUnitBase> units)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (units == null) return found;

            foreach (PlcUnitBase unit in units)
            {
                if (unit != null) Unit(unit, PathOf(unit.Parent), found);
            }

            return found;
        }

        // ---- Folders: the system root and a user folder alike --------------------------------

        public static List<CheckedObject> FromBlockGroups(IEnumerable<PlcBlockGroup> groups) =>
            Each(groups, (group, found) => Blocks(group, PathOf(group.Parent), found));

        public static List<CheckedObject> FromTechnologyObjectGroups(IEnumerable<TechnologicalInstanceDBGroup> groups) =>
            Each(groups, (group, found) => TechnologyObjects(group, PathOf(group.Parent), found));

        public static List<CheckedObject> FromTagTableGroups(IEnumerable<PlcTagTableGroup> groups) =>
            Each(groups, (group, found) => TagTables(group, PathOf(group.Parent), found));

        public static List<CheckedObject> FromTypeGroups(IEnumerable<PlcTypeGroup> groups) =>
            Each(groups, (group, found) => Types(group, PathOf(group.Parent), found));

        public static List<CheckedObject> FromAlarmTextListGroups(IEnumerable<PlcAlarmTextlistGroup> groups) =>
            Each(groups, (group, found) => AlarmTextLists(group, PathOf(group.Parent), found));

        // ---- Objects ------------------------------------------------------------------------

        /// <summary>
        /// Blocks of any kind, and technology objects too: a technology object is a block to
        /// the type system, so the menu entry on blocks is the one TIA offers on it.
        /// </summary>
        public static List<CheckedObject> FromBlocks(IEnumerable<PlcBlock> blocks) =>
            Each(blocks, (block, found) => Block(block, PathOf(block.Parent), found));

        public static List<CheckedObject> FromTagTables(IEnumerable<PlcTagTable> tables) =>
            Each(tables, (table, found) => TagTable(table, PathOf(table.Parent), found));

        public static List<CheckedObject> FromTypes(IEnumerable<PlcType> types) =>
            Each(types, (type, found) => DataType(type, PathOf(type.Parent), found));

        public static List<CheckedObject> FromAlarmTextLists(IEnumerable<PlcAlarmTextlist> lists) =>
            Each(lists, (list, found) => AlarmTextList(list, PathOf(list.Parent), found));

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

        private static void Plc(PlcSoftware plc, List<CheckedObject> found)
        {
            string here = plc.Name ?? string.Empty;

            Blocks(plc.BlockGroup, here, found);
            TechnologyObjects(plc.TechnologicalObjectGroup, here, found);
            TagTables(plc.TagTableGroup, here, found);
            Types(plc.TypeGroup, here, found);
            AlarmTextLists(plc.PlcAlarmTextlistGroup, here, found);

            foreach (PlcUnitBase unit in UnitsOf(plc)) Unit(unit, here, found);
        }

        /// <summary>
        /// A unit repeats four of the PLC's group roots with the same types; technology
        /// objects stay at PLC level. Its name joins the path, so a folder called 03-ALL in
        /// two units is still two places.
        /// </summary>
        private static void Unit(PlcUnitBase unit, string parent, List<CheckedObject> found)
        {
            if (unit == null) return;

            string here = Join(parent, unit.Name);

            Blocks(unit.BlockGroup, here, found);
            TagTables(unit.TagTableGroup, here, found);
            Types(unit.TypeGroup, here, found);
            AlarmTextLists(unit.PlcAlarmTextlistGroup, here, found);
        }

        private static void Blocks(PlcBlockGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (PlcBlock block in group.Blocks) Block(block, here, found);
            foreach (PlcBlockUserGroup child in group.Groups) Blocks(child, here, found);

            // SystemBlockGroups is deliberately not walked: TIA fills and names it.
        }

        private static void Block(PlcBlock block, string path, List<CheckedObject> found)
        {
            if (block == null) return;

            TechnologicalInstanceDB technologyObject = block as TechnologicalInstanceDB;
            if (technologyObject != null)
            {
                TechnologyObject(technologyObject, path, found);
                return;
            }

            // Only a global DB's members are its own. An instance DB's are its FB's
            // interface, checked on the FB; an array DB holds elements of one type rather
            // than named members; and a technology object's are defined by Siemens.
            GlobalDB db = block as GlobalDB;
            if (db == null)
            {
                found.Add(new CheckedObject(ObjectFamily.Blocks, BlockType(block), block.Name, path));
                return;
            }

            string unreadable;
            List<CheckedMember> members = StaticsOf(db, out unreadable);

            found.Add(new CheckedObject(
                ObjectFamily.Blocks, CodingStyleNames.GlobalDB, db.Name, path, members, unreadable));
        }

        /// <summary>
        /// A global DB's top-level members. Every one of them is Static - a data block has
        /// no other section - and nested members wait for the export phase two brings.
        /// </summary>
        private static List<CheckedMember> StaticsOf(GlobalDB db, out string unreadable)
        {
            List<CheckedMember> members = new List<CheckedMember>();
            unreadable = null;

            try
            {
                if (db.IsKnowHowProtected)
                {
                    unreadable = "The block is know-how protected, so its members cannot be read.";
                    return members;
                }

                MemberComposition composition = db.Interface?.Members;
                if (composition == null)
                {
                    unreadable = "TIA returned no interface for the block.";
                    return members;
                }

                foreach (Member member in composition)
                {
                    if (member != null) members.Add(new CheckedMember(CodingStyleNames.Static, member.Name));
                }
            }
            catch (Exception e)
            {
                // Half a list would read as the whole of it.
                members.Clear();
                unreadable = "TIA refused to show the block's members: " + e.Message;
            }

            return members;
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

        private static void TechnologyObjects(TechnologicalInstanceDBGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
                TechnologyObject(technologyObject, here, found);

            foreach (TechnologicalInstanceDBUserGroup child in group.Groups) TechnologyObjects(child, here, found);
        }

        private static void TechnologyObject(TechnologicalInstanceDB technologyObject, string path, List<CheckedObject> found)
        {
            if (technologyObject == null) return;

            found.Add(new CheckedObject(
                ObjectFamily.TechnologyObjects, CodingStyleNames.TechnologicalInstanceDB,
                technologyObject.Name, path));
        }

        private static void TagTables(PlcTagTableGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (PlcTagTable table in group.TagTables) TagTable(table, here, found);
            foreach (PlcTagTableUserGroup child in group.Groups) TagTables(child, here, found);
        }

        private static void TagTable(PlcTagTable table, string path, List<CheckedObject> found)
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
                ObjectFamily.TagTables, CodingStyleNames.PlcTagTable, table.Name, path, members));
        }

        private static void Types(PlcTypeGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (PlcType type in group.Types) DataType(type, here, found);
            foreach (PlcTypeUserGroup child in group.Groups) Types(child, here, found);

            // SystemTypeGroups is deliberately not walked, for the same reason as system blocks.
        }

        private static void DataType(PlcType type, string path, List<CheckedObject> found)
        {
            if (type == null) return;

            // No members: a PLC data type exposes no interface in the object model.
            found.Add(new CheckedObject(
                ObjectFamily.Types,
                type is PlcStruct ? CodingStyleNames.PlcStruct : type.GetType().Name,
                type.Name, path));
        }

        /// <summary>
        /// The group has no folders and no name of its own in the object model, so a list's
        /// path is the PLC or unit it belongs to.
        /// </summary>
        private static void AlarmTextLists(PlcAlarmTextlistGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            foreach (PlcAlarmUserTextlist list in group.PlcAlarmUserTextlists) AlarmTextList(list, parent, found);
        }

        /// <summary>User text lists only: a system text list is named by TIA.</summary>
        private static void AlarmTextList(PlcAlarmTextlist list, string path, List<CheckedObject> found)
        {
            if (!(list is PlcAlarmUserTextlist)) return;

            found.Add(new CheckedObject(
                ObjectFamily.AlarmTextLists, CodingStyleNames.AlarmTexts, list.Name, path));
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

        // ---- Paths --------------------------------------------------------------------------

        /// <summary>
        /// The path of whatever sits under <paramref name="start"/>, built upwards: folder
        /// names, the software unit when there is one, and the PLC. Links it does not know -
        /// the unit folder between a unit and its PLC, say - are stepped over rather than
        /// ending the walk, since which ones TIA puts in the chain is not something to bet on.
        /// </summary>
        private static string PathOf(IEngineeringObject start)
        {
            List<string> names = new List<string>();

            IEngineeringObject current = start;
            for (int step = 0; current != null && step < MaxParentSteps; step++)
            {
                PlcSoftware plc = current as PlcSoftware;
                if (plc != null)
                {
                    names.Add(plc.Name);
                    break;
                }

                // Hardware or the project: the software was left behind, so stop here.
                if (current is HardwareObject || current is Project) break;

                string name = NameOf(current);
                if (name != null) names.Add(name);

                current = current.Parent;
            }

            names.Reverse();
            return string.Join(Separator, names);
        }

        /// <summary>The name a link contributes to a path, or null for one that contributes none.</summary>
        private static string NameOf(IEngineeringObject link)
        {
            if (link is PlcBlockGroup) return ((PlcBlockGroup)link).Name;
            if (link is PlcTagTableGroup) return ((PlcTagTableGroup)link).Name;
            if (link is PlcTypeGroup) return ((PlcTypeGroup)link).Name;
            if (link is TechnologicalInstanceDBGroup) return ((TechnologicalInstanceDBGroup)link).Name;
            if (link is PlcUnitBase) return ((PlcUnitBase)link).Name;

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
