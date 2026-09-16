using System;
using System.Collections.Generic;
using System.IO;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Alarm;
using Siemens.Engineering.SW.Alarm.TextLists;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

using AddIn.Shared.Adapters;

using Core.Exports;

using Location = AddIn.Adapters.TiaProjectPlaces.Location;

namespace AddIn.Adapters
{
    /// <summary>
    /// Walks whatever was selected and hands back objects that can write themselves out in
    /// every format TIA offers for them.
    ///
    /// **An object is not one file.** SimaticML is what everything has and what an import
    /// restores from; a SIMATIC SD pair and a language source are what TIA's own export dialog
    /// adds, and which of them exist is decided by the block's programming language. See
    /// <see cref="Formats"/> for the table.
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
    /// **The alarm text lists are one item per PLC, not one per list**, because that is all
    /// Openness offers: `PlcAlarmTextlist` has no `Export`, and the only way out is a service
    /// that writes a PLC's lists into a workbook. Selecting one list therefore exports the
    /// PLC's lists - the file always means the same thing, which a file whose contents
    /// depended on the selection would not.
    /// </summary>
    internal static class TiaExportObjects
    {
        /// <summary>
        /// What a PLC's alarm text lists are written as. Ours and fixed: the group they live
        /// in has no name of its own in the object model, so there is nothing to mirror.
        /// </summary>
        private const string AlarmTextsFile = "Alarm texts";

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

        /// <summary>
        /// The alarm text lists of whatever PLC each selection belongs to, once per PLC: two
        /// lists of one PLC are one workbook, not two.
        /// </summary>
        public static List<ExportItem> FromAlarmTextListGroups(IEnumerable<PlcAlarmTextlistGroup> groups) =>
            AlarmTextsOf(groups);

        public static List<ExportItem> FromAlarmTextLists(IEnumerable<PlcAlarmTextlist> lists) =>
            AlarmTextsOf(lists);

        private static List<ExportItem> AlarmTextsOf<T>(IEnumerable<T> selection) where T : IEngineeringObject
        {
            List<ExportItem> found = new List<ExportItem>();
            if (selection == null) return found;

            HashSet<PlcSoftware> seen = new HashSet<PlcSoftware>();

            foreach (T item in selection)
            {
                if (item == null) continue;

                PlcSoftware plc = TiaProjectPlaces.SoftwareFor(item);
                if (plc != null && seen.Add(plc)) AlarmTexts(plc, found);
            }

            return found;
        }

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
            AlarmTexts(plc, found);

            // A unit's alarm text lists are the PLC's as far as Openness is concerned, so the
            // workbook above already holds them.
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
                (folder, name) =>
                {
                    ProgrammingLanguage language = Language(block);
                    string extension = SourceFor(language);

                    return Formats(
                        folder, name,
                        () => block.IsKnowHowProtected,
                        file => block.Export(file, ExportOptions.WithDefaults),
                        HasDocuments(language)
                            ? (Func<DirectoryInfo, string, DocumentExportResult>)block.ExportAsDocuments
                            : null,
                        extension,
                        extension == null ? null : (Action<FileInfo>)(file => Source(block, file)));
                }));
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

            // SimaticML and nothing else: a tag table has no source and no SIMATIC SD.
            found.Add(new ExportItem(
                where.Plc, where.Unit, where.Folders, table.Name,
                (folder, name) => Formats(
                    folder, name,
                    () => false,
                    file => table.Export(file, ExportOptions.WithDefaults),
                    null, null, null)));
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

            // A PLC data type has all three. **A safety one has no `.udt`**, and nothing in the
            // typed API says which it is - `PlcType` carries no language and the safety
            // assembly declares no type of its own for one - so the source is attempted and
            // TIA's refusal is reported as the one format that did not come out.
            found.Add(new ExportItem(
                where.Plc, where.Unit, where.Folders, type.Name,
                (folder, name) => Formats(
                    folder, name,
                    () => type.IsKnowHowProtected,
                    file => type.Export(file, ExportOptions.WithDefaults),
                    type.ExportAsDocuments,
                    UdtSource,
                    file => Source(type, file))));
        }

        /// <summary>
        /// One workbook holding every alarm text list of this PLC.
        ///
        /// **Not one file per list, because Openness has no per-list export**: the only way
        /// out is `PlcAlarmTextListProvider`, whose filtered overload narrows by *name* - and
        /// a unit's list and the PLC's list can share one. Writing the PLC's lists whole means
        /// the file says the same thing however the export was started.
        ///
        /// It sits beside the PLC's folders rather than inside one: the group these live in
        /// has no name of its own in the object model, so there is no folder to mirror.
        /// </summary>
        private static void AlarmTexts(PlcSoftware plc, List<ExportItem> found)
        {
            if (plc == null) return;

            found.Add(new ExportItem(
                plc.Name, null, null, AlarmTextsFile,
                (folder, name) =>
                {
                    PlcAlarmTextListProvider provider;
                    try
                    {
                        provider = plc.GetService<PlcAlarmTextListProvider>();
                    }
                    catch (Exception exception)
                    {
                        return ExportOutcome.Refused(exception.Message);
                    }

                    // Answers null when the service is not there, as MessageBoxProvider does:
                    // a PLC without alarm texts is not a failure.
                    if (provider == null)
                        return ExportOutcome.Refused("this PLC does not offer alarm text lists");

                    string failed = Try(folder, name, ExportTree.Workbook,
                        path => provider.ExportToXlsx(new FileInfo(path)));

                    return failed == null ? ExportOutcome.Written(1) : ExportOutcome.Refused(failed);
                }));
        }

        // ---- Formats ----------------------------------------------------------------------------

        /// <summary>
        /// Which formats an object of this language has, as TIA's own export dialog offers
        /// them. **A language decides, not a guess**: `PlcBlock.ProgrammingLanguage` says
        /// whether a source can be generated and what its extension is.
        ///
        /// <list type="bullet">
        /// <item><description>LAD, FBD - SimaticML and SIMATIC SD</description></item>
        /// <item><description>SCL - a <c>.scl</c> source as well</description></item>
        /// <item><description>STL - a <c>.awl</c> source, and no SD</description></item>
        /// <item><description>GRAPH - SimaticML alone</description></item>
        /// <item><description>DB - a <c>.db</c> source as well; a safety DB has none</description></item>
        /// <item><description>a PLC data type - a <c>.udt</c> source as well</description></item>
        /// </list>
        ///
        /// **Anything else gets SimaticML and SIMATIC SD**, which every object has: the
        /// languages the table does not name - the safety code languages, ProDiag, a
        /// technology object's Motion_DB - are exported rather than skipped, and it is TIA
        /// that says no if one of the two turns out not to apply.
        /// </summary>
        private static string SourceFor(ProgrammingLanguage language)
        {
            switch (language)
            {
                case ProgrammingLanguage.SCL: return ".scl";
                case ProgrammingLanguage.STL: return ".awl";
                case ProgrammingLanguage.DB: return ".db";
                default: return null;
            }
        }

        /// <summary>The source extension of a PLC data type. Not a language: `PlcType` has none.</summary>
        private const string UdtSource = ".udt";

        /// <summary>
        /// The language, or <c>Undef</c> when TIA will not say - which is treated as "try
        /// everything and let TIA refuse", the same answer a safety data type gets.
        /// </summary>
        private static ProgrammingLanguage Language(PlcBlock block)
        {
            try
            {
                return block.ProgrammingLanguage;
            }
            catch (Exception)
            {
                return ProgrammingLanguage.Undef;
            }
        }

        /// <summary>
        /// Whether this language has a SIMATIC SD pair. STL and GRAPH do not, which is what
        /// TIA's own dialog offers - the rest do.
        /// </summary>
        private static bool HasDocuments(ProgrammingLanguage language) =>
            language != ProgrammingLanguage.STL && language != ProgrammingLanguage.GRAPH;

        /// <summary>
        /// Writes every format one object has, and says which of them did not come out.
        ///
        /// **SimaticML first and always**: it is the format everything has, the one the check
        /// reads back, and the one an import restores from. A source or a SIMATIC SD pair that
        /// TIA refuses leaves the object exported but incomplete, which is a different answer
        /// from one that produced nothing - and the notification keeps the two apart.
        /// </summary>
        private static ExportOutcome Formats(
            string folder,
            string name,
            Func<bool> knowHowProtected,
            Action<FileInfo> simaticMl,
            Func<DirectoryInfo, string, DocumentExportResult> documents,
            string sourceExtension,
            Action<FileInfo> source)
        {
            // **Asked first**, because TIA's own refusal for a protected block names the
            // mechanism rather than the object, and it would be repeated once per format.
            try
            {
                if (knowHowProtected())
                    return ExportOutcome.Refused("it is know-how protected, so TIA will not export it");
            }
            catch (Exception exception)
            {
                return ExportOutcome.Refused("TIA would not say whether it is know-how protected: " + exception.Message);
            }

            int files = 0;
            List<string> problems = new List<string>();

            // **`WithDefaults`, unlike the coding-style check's export.** That one reads names
            // out of a file it deletes a second later and takes `None`; this is the copy
            // somebody restores from, and it should not depend on what a future TIA decides a
            // default value was.
            Keep(Try(folder, name, ExportTree.SimaticMl, path => simaticMl(new FileInfo(path))), ref files, problems, 1);

            if (documents != null) Documents(folder, name, documents, ref files, problems);

            if (source != null && sourceExtension != null)
                Keep(Try(folder, name, sourceExtension, path => source(new FileInfo(path))), ref files, problems, 1);

            return problems.Count == 0
                ? ExportOutcome.Written(files)
                : ExportOutcome.Partly(files, string.Join("; ", problems.ToArray()));
        }

        /// <summary>
        /// The SIMATIC SD pair: one call writes the declaration and the resources, and TIA
        /// names both itself.
        ///
        /// **It reports its outcome in the result rather than by throwing**, which is the one
        /// place in this file where a successful-looking call can mean nothing was written:
        /// `DocumentExportResult.State` is `Success`, `PartialSuccess` or `Failure`, and
        /// `ExportedDocuments` lists what really came out. Counting the two files without
        /// reading it would report a failed SD export as two files on disk.
        /// </summary>
        private static void Documents(
            string folder,
            string name,
            Func<DirectoryInfo, string, DocumentExportResult> documents,
            ref int files,
            List<string> problems)
        {
            DocumentExportResult result;

            try
            {
                Fresh(Path.Combine(folder, name + ExportTree.SimaticSdDeclaration));
                Fresh(Path.Combine(folder, name + ExportTree.SimaticSdResources));

                result = documents(new DirectoryInfo(folder), name);
            }
            catch (Exception exception)
            {
                problems.Add(ExportTree.SimaticSdDeclaration + ": " + exception.Message);
                return;
            }

            int written = Count(result);
            files += written;

            if (result != null && result.State == DocumentResultState.Success) return;

            problems.Add(ExportTree.SimaticSdDeclaration + ": " + Said(result));
        }

        /// <summary>How many files the SD export really produced, counted rather than assumed.</summary>
        private static int Count(DocumentExportResult result)
        {
            if (result == null || result.ExportedDocuments == null) return 0;

            int written = 0;
            foreach (FileInfo document in result.ExportedDocuments)
            {
                if (document != null) written++;
            }

            return written;
        }

        /// <summary>What TIA said about an SD export that did not fully succeed.</summary>
        private static string Said(DocumentExportResult result)
        {
            if (result == null) return "TIA reported nothing about the export";

            List<string> said = new List<string>();

            try
            {
                foreach (DocumentResultMessage message in result.Messages)
                {
                    if (message != null && !string.IsNullOrEmpty(message.Message)) said.Add(message.Message);
                }
            }
            catch (Exception exception)
            {
                said.Add(exception.Message);
            }

            string state = result.State.ToString();

            return said.Count == 0 ? state : state + " - " + string.Join("; ", said.ToArray());
        }

        /// <summary>One format's answer, folded into the running count or into the problems.</summary>
        private static void Keep(string failed, ref int files, List<string> problems, int written)
        {
            if (failed == null) files += written;
            else problems.Add(failed);
        }

        /// <summary>One format, named in whatever it has to say for itself.</summary>
        private static string Try(string folder, string name, string extension, Action<string> write)
        {
            string path = Path.Combine(folder, name + extension);

            try
            {
                // Openness refuses to write onto a file that is there, and an export is a
                // mirror: what was written last time is what this replaces.
                Fresh(path);
                write(path);

                return null;
            }
            catch (Exception exception)
            {
                return extension + ": " + exception.Message;
            }
        }

        private static void Fresh(string path)
        {
            if (File.Exists(path)) File.Delete(path);
        }

        /// <summary>
        /// Generates the language source of one block or data type.
        ///
        /// **Through its own software unit's external sources when it lives in one**, which is
        /// what <see cref="TiaProjectPlaces.SourcesFor"/> answers: a unit is a compilation
        /// scope of its own.
        ///
        /// **`GenerateOptions.None`, not `WithDependencies`.** This export is a mirror of the
        /// project, one file per object; pulling every called block and every data type into
        /// each source would write the same code into a hundred files and make a diff between
        /// two exports unreadable.
        /// </summary>
        private static void Source(IEngineeringObject item, FileInfo file)
        {
            PlcExternalSourceSystemGroup sources = TiaProjectPlaces.SourcesFor(item);

            if (sources == null)
                throw new InvalidOperationException("this PLC offers no external source folder to generate into");

            sources.GenerateSource(new IGenerateSource[] { (IGenerateSource)item }, file, GenerateOptions.None);
        }
    }
}
