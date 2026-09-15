using System;
using System.Collections.Generic;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

using AddIn.Shared.Adapters;

using Location = AddIn.Adapters.TiaProjectPlaces.Location;

namespace AddIn.Adapters
{
    /// <summary>
    /// Walks whatever was selected and hands back objects that can write themselves out as
    /// SimaticML.
    ///
    /// Identical in V20 and V21 and duplicated for the usual reason: it touches Siemens types,
    /// and the two Add-In assemblies have different identities. **Where an object lives comes
    /// from <see cref="TiaProjectPlaces"/>**, the same walk the coding-style report uses, so
    /// the exported folders and the report's columns cannot disagree.
    ///
    /// **It keeps more than the coding-style walk does, and that is the point.** There, an
    /// object TIA named itself is dropped, because a naming rule could only fail it. Here the
    /// question is what the project contains: **instance DBs, array DBs and technology objects
    /// are exported**, because they are part of the program somebody would want back. What
    /// stays out is only what TIA generates and nobody edits - the system block and type
    /// folders, and the default tag table.
    ///
    /// **Alarm text lists are not here**: Openness has no per-list export at all, only a
    /// service that writes every list of a PLC into one workbook. That is its own entry.
    /// </summary>
    internal static class TiaExportObjects
    {
        // ---- Containers ---------------------------------------------------------------------

        public static List<ExportItem> FromProjects(IEnumerable<Project> projects)
        {
            List<ExportItem> found = new List<ExportItem>();
            if (projects == null) return found;

            foreach (Project project in projects)
            {
                if (project == null) continue;

                foreach (PlcSoftware plc in TiaProjectPlaces.PlcsOf(project)) Plc(plc, found);
            }

            return found;
        }

        public static List<ExportItem> FromPlcs(IEnumerable<DeviceItem> deviceItems)
        {
            List<ExportItem> found = new List<ExportItem>();
            if (deviceItems == null) return found;

            foreach (DeviceItem item in deviceItems)
            {
                PlcSoftware plc = TiaProjectPlaces.SoftwareOf(item);
                if (plc != null) Plc(plc, found);
            }

            return found;
        }

        public static List<ExportItem> FromUnits(IEnumerable<PlcUnitBase> units)
        {
            List<ExportItem> found = new List<ExportItem>();
            if (units == null) return found;

            foreach (PlcUnitBase unit in units)
            {
                if (unit != null) Unit(unit, TiaProjectPlaces.LocationOf(unit.Parent), found);
            }

            return found;
        }

        // ---- Folders --------------------------------------------------------------------------

        public static List<ExportItem> FromBlockGroups(IEnumerable<PlcBlockGroup> groups) =>
            Each(groups, (group, found) => Blocks(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        public static List<ExportItem> FromTechnologyObjectGroups(IEnumerable<TechnologicalInstanceDBGroup> groups) =>
            Each(groups, (group, found) => TechnologyObjects(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        public static List<ExportItem> FromTagTableGroups(IEnumerable<PlcTagTableGroup> groups) =>
            Each(groups, (group, found) => TagTables(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        public static List<ExportItem> FromTypeGroups(IEnumerable<PlcTypeGroup> groups) =>
            Each(groups, (group, found) => Types(group, TiaProjectPlaces.LocationOf(group.Parent), found));

        // ---- Objects --------------------------------------------------------------------------

        public static List<ExportItem> FromBlocks(IEnumerable<PlcBlock> blocks) =>
            Each(blocks, (block, found) => Block(block, TiaProjectPlaces.LocationOf(block.Parent), found));

        public static List<ExportItem> FromTagTables(IEnumerable<PlcTagTable> tables) =>
            Each(tables, (table, found) => TagTable(table, TiaProjectPlaces.LocationOf(table.Parent), found));

        public static List<ExportItem> FromTypes(IEnumerable<PlcType> types) =>
            Each(types, (type, found) => DataType(type, TiaProjectPlaces.LocationOf(type.Parent), found));

        private static List<ExportItem> Each<T>(IEnumerable<T> selection, Action<T, List<ExportItem>> walk)
            where T : class
        {
            List<ExportItem> found = new List<ExportItem>();
            if (selection == null) return found;

            foreach (T item in selection)
            {
                if (item != null) walk(item, found);
            }

            return found;
        }

        // ---- The walk ---------------------------------------------------------------------------

        private static void Plc(PlcSoftware plc, List<ExportItem> found)
        {
            Location here = Location.In(plc);

            Blocks(plc.BlockGroup, here, found);
            TechnologyObjects(plc.TechnologicalObjectGroup, here, found);
            TagTables(plc.TagTableGroup, here, found);
            Types(plc.TypeGroup, here, found);

            foreach (PlcUnitBase unit in TiaProjectPlaces.UnitsOf(plc)) Unit(unit, here, found);
        }

        private static void Unit(PlcUnitBase unit, Location parent, List<ExportItem> found)
        {
            if (unit == null) return;

            Location here = parent.InUnit(unit.Name);

            Blocks(unit.BlockGroup, here, found);
            TagTables(unit.TagTableGroup, here, found);
            Types(unit.TypeGroup, here, found);
        }

        private static void Blocks(PlcBlockGroup group, Location parent, List<ExportItem> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcBlock block in group.Blocks) Block(block, here, found);
            foreach (PlcBlockUserGroup child in group.Groups) Blocks(child, here, found);

            // SystemBlockGroups is deliberately not walked: TIA fills and names it, and it
            // comes back with the firmware rather than out of a backup.
        }

        /// <summary>
        /// Every block, instance DBs and array DBs included: this is the project's program,
        /// not a list of names somebody chose.
        /// </summary>
        private static void Block(PlcBlock block, Location where, List<ExportItem> found)
        {
            if (block == null) return;

            found.Add(new ExportItem(
                where.Plc, where.Unit, where.Folders, block.Name,
                path => Write(block.Name, () => block.IsKnowHowProtected, path, file => block.Export(file, ExportOptions.WithDefaults))));
        }

        private static void TechnologyObjects(TechnologicalInstanceDBGroup group, Location parent, List<ExportItem> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (TechnologicalInstanceDB technologyObject in group.TechnologicalObjects)
                Block(technologyObject, here, found);

            foreach (TechnologicalInstanceDBUserGroup child in group.Groups) TechnologyObjects(child, here, found);
        }

        private static void TagTables(PlcTagTableGroup group, Location parent, List<ExportItem> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcTagTable table in group.TagTables) TagTable(table, here, found);
            foreach (PlcTagTableUserGroup child in group.Groups) TagTables(child, here, found);
        }

        private static void TagTable(PlcTagTable table, Location where, List<ExportItem> found)
        {
            // The default table holds what the hardware put there and TIA rebuilds it.
            if (table == null || table.IsDefault) return;

            found.Add(new ExportItem(
                where.Plc, where.Unit, where.Folders, table.Name,
                path => Write(table.Name, () => false, path, file => table.Export(file, ExportOptions.WithDefaults))));
        }

        private static void Types(PlcTypeGroup group, Location parent, List<ExportItem> found)
        {
            if (group == null) return;

            Location here = parent.InFolder(group.Name);

            foreach (PlcType type in group.Types) DataType(type, here, found);
            foreach (PlcTypeUserGroup child in group.Groups) Types(child, here, found);

            // SystemTypeGroups is deliberately not walked, for the same reason as system blocks.
        }

        private static void DataType(PlcType type, Location where, List<ExportItem> found)
        {
            if (type == null) return;

            found.Add(new ExportItem(
                where.Plc, where.Unit, where.Folders, type.Name,
                path => Write(type.Name, () => type.IsKnowHowProtected, path, file => type.Export(file, ExportOptions.WithDefaults))));
        }

        /// <summary>
        /// Exports one object, and turns everything that can go wrong into a sentence.
        ///
        /// **Know-how protection is checked first**, because TIA's own refusal for that names
        /// the mechanism rather than the block. **Nothing is compiled**: TIA will not export a
        /// block that is not consistent, and compiling it would change the project behind an
        /// operator who asked for a copy of it.
        ///
        /// **`WithDefaults` for all three families**, which is the difference between this and
        /// the export the coding-style check makes: that one reads names out of a file it
        /// deletes a second later and takes `None`, while this one is the copy somebody
        /// restores from, and a copy should not depend on what a future TIA decides a default
        /// value was.
        /// </summary>
        private static string Write(string name, Func<bool> knowHowProtected, string path, Action<System.IO.FileInfo> export)
        {
            try
            {
                if (knowHowProtected())
                    return "it is know-how protected, so TIA will not export it";
            }
            catch (Exception exception)
            {
                return "TIA would not say whether it is know-how protected: " + exception.Message;
            }

            try
            {
                export(new System.IO.FileInfo(path));
                return null;
            }
            catch (Exception exception)
            {
                return exception.Message;
            }
        }
    }
}
