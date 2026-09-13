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
    /// Walks a PLC and names what it holds in the terms the coding-style check reads.
    ///
    /// Identical in V20 and V21 and duplicated for the usual reason: it touches Siemens
    /// types, and the two Add-In assemblies have different identities. Nothing Siemens
    /// leaves this file; the checker in Core is handed names and strings.
    ///
    /// **Only names an engineer chose are collected.** System blocks, system types, system
    /// constants, system text lists and the default tag table are all named by TIA, so a
    /// rule could only ever fail them, and nobody reading the report could act on it.
    ///
    /// **Reading the tree is not guarded.** A folder that could not be read would otherwise
    /// drop out of the report and leave it looking complete, which is the worst result a
    /// check can give; failing the walk lets the action say what went wrong. What is guarded
    /// is what may legitimately be absent: software units on an S7-1200, and the members of
    /// a block that will not show them.
    /// </summary>
    internal static class TiaCheckedObjects
    {
        private const string Separator = "/";

        /// <summary>
        /// Everything in the PLC: its five families, and the same again inside every
        /// software unit. Null when the selected device is not a PLC.
        /// </summary>
        public static List<CheckedObject> FromPlc(DeviceItem deviceItem)
        {
            PlcSoftware plc = deviceItem?.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            if (plc == null) return null;

            List<CheckedObject> found = new List<CheckedObject>();

            Blocks(plc.BlockGroup, string.Empty, found);
            TechnologyObjects(plc.TechnologicalObjectGroup, string.Empty, found);
            TagTables(plc.TagTableGroup, string.Empty, found);
            Types(plc.TypeGroup, string.Empty, found);
            AlarmTextLists(plc.PlcAlarmTextlistGroup, string.Empty, found);

            // A unit repeats four of the PLC's group roots with the same types; technology
            // objects stay at PLC level. The unit's name heads the path, so a folder called
            // 03-ALL in two units is still two places.
            foreach (PlcUnitBase unit in UnitsOf(plc))
            {
                if (unit == null) continue;

                Blocks(unit.BlockGroup, unit.Name, found);
                TagTables(unit.TagTableGroup, unit.Name, found);
                Types(unit.TypeGroup, unit.Name, found);
                AlarmTextLists(unit.PlcAlarmTextlistGroup, unit.Name, found);
            }

            return found;
        }

        /// <summary>
        /// The blocks in one folder and everything below it. Serves the system group and a
        /// user folder alike, since both are a <see cref="PlcBlockGroup"/>.
        /// </summary>
        public static List<CheckedObject> FromBlockGroup(PlcBlockGroup group)
        {
            if (group == null) return null;

            List<CheckedObject> found = new List<CheckedObject>();
            Blocks(group, PathOf(group.Parent), found);

            return found;
        }

        /// <summary>A selection of blocks, each with the path it sits at.</summary>
        public static List<CheckedObject> FromBlocks(IEnumerable<PlcBlock> blocks)
        {
            List<CheckedObject> found = new List<CheckedObject>();
            if (blocks == null) return found;

            foreach (PlcBlock block in blocks)
            {
                if (block == null) continue;
                Block(block, PathOf(block.Parent), found);
            }

            return found;
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
        /// The configuration's name for a block's kind. Most specific first: a technology
        /// object is an instance DB to the type system, and would otherwise be reported as one.
        /// Anything the contract does not name keeps its runtime type name, which the checker
        /// then reports as not configured - the honest answer for it.
        /// </summary>
        private static string BlockType(PlcBlock block)
        {
            if (block is OB) return CodingStyleNames.OB;
            if (block is FC) return CodingStyleNames.FC;
            if (block is FB) return CodingStyleNames.FB;
            if (block is GlobalDB) return CodingStyleNames.GlobalDB;
            if (block is ArrayDB) return CodingStyleNames.ArrayDB;
            if (block is TechnologicalInstanceDB) return CodingStyleNames.TechnologicalInstanceDB;
            if (block is InstanceDB) return CodingStyleNames.InstanceDB;

            return block.GetType().Name;
        }

        private static void TechnologyObjects(TechnologicalInstanceDBGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
            {
                if (technologyObject == null) continue;

                found.Add(new CheckedObject(
                    ObjectFamily.TechnologyObjects, CodingStyleNames.TechnologicalInstanceDB,
                    technologyObject.Name, here));
            }

            foreach (TechnologicalInstanceDBUserGroup child in group.Groups) TechnologyObjects(child, here, found);
        }

        private static void TagTables(PlcTagTableGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (PlcTagTable table in group.TagTables)
            {
                // The default tag table cannot be renamed, so its name is not the engineer's.
                if (table == null || table.IsDefault) continue;

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
                    ObjectFamily.TagTables, CodingStyleNames.PlcTagTable, table.Name, here, members));
            }

            foreach (PlcTagTableUserGroup child in group.Groups) TagTables(child, here, found);
        }

        private static void Types(PlcTypeGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            string here = Join(parent, group.Name);

            foreach (PlcType type in group.Types)
            {
                if (type == null) continue;

                // No members: a PLC data type exposes no interface in the object model.
                found.Add(new CheckedObject(
                    ObjectFamily.Types,
                    type is PlcStruct ? CodingStyleNames.PlcStruct : type.GetType().Name,
                    type.Name, here));
            }

            foreach (PlcTypeUserGroup child in group.Groups) Types(child, here, found);

            // SystemTypeGroups is deliberately not walked, for the same reason as system blocks.
        }

        /// <summary>
        /// User text lists only. The group has no folders and no name of its own in the
        /// object model, so a list's path is the unit it belongs to, or nothing.
        /// </summary>
        private static void AlarmTextLists(PlcAlarmTextlistGroup group, string parent, List<CheckedObject> found)
        {
            if (group == null) return;

            foreach (PlcAlarmUserTextlist list in group.PlcAlarmUserTextlists)
            {
                if (list == null) continue;

                found.Add(new CheckedObject(
                    ObjectFamily.AlarmTextLists, CodingStyleNames.AlarmTexts, list.Name, parent));
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

        /// <summary>
        /// The path of whatever sits under <paramref name="start"/>, built upwards: the
        /// folder names, then the software unit when there is one. The same shape
        /// <see cref="FromPlc"/> builds downwards, so a block reports the same path whichever
        /// menu entry reached it.
        /// </summary>
        private static string PathOf(IEngineeringObject start)
        {
            List<string> names = new List<string>();

            for (IEngineeringObject current = start; current != null; current = current.Parent)
            {
                PlcBlockGroup group = current as PlcBlockGroup;
                if (group != null)
                {
                    names.Add(group.Name);
                    continue;
                }

                PlcUnitBase unit = current as PlcUnitBase;
                if (unit != null) names.Add(unit.Name);

                break;
            }

            names.Reverse();
            return string.Join(Separator, names);
        }

        private static string Join(string parent, string name)
        {
            if (string.IsNullOrEmpty(parent)) return name ?? string.Empty;
            if (string.IsNullOrEmpty(name)) return parent;

            return parent + Separator + name;
        }
    }
}
