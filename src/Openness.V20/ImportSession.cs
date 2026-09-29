using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;

using Core.Config;
using Core.Imports;
using Core.Logging;

using Openness.Shared;

using Siemens.Engineering;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace Openness
{
    /// <summary>
    /// Imports files into one folder of a PLC through Openness: finds the folder that was
    /// right-clicked, says what the chosen files would overwrite, and takes them in one at a time.
    ///
    /// **Three doors, one per format**, each read off both TIA versions before this was written:
    ///
    /// - SimaticML: <c>Import(FileInfo, ImportOptions)</c> on the folder's composition - blocks,
    ///   data types, tag tables and technology objects alike.
    /// - SIMATIC SD: <c>ImportFromDocuments(DirectoryInfo, name, ImportDocumentOptions)</c>, which
    ///   only blocks and data types have. It reads the pair of that name the folder holds, so a
    ///   <c>.s7res</c> beside the <c>.s7dcl</c> is read whether or not anybody ticked it.
    /// - A source: an external source made from the file, blocks generated from it into the
    ///   folder, and the external source deleted again.
    ///
    /// **What it overwrites is overwritten where it is** (the maintainer's decision, 2026-09-28):
    /// SimaticML and SIMATIC SD go into the folder the existing object sits in, with
    /// <c>Override</c>, and a source needs nothing done - TIA overwrites an existing block in
    /// place, measured on the VM on 2026-09-19.
    ///
    /// **Nothing the project has is written without having been asked about.** A source
    /// overwrites without saying a word, so before any file goes in, what it declares is looked
    /// for again: an object that is there and was not in the list the operator answered stops
    /// the file. The one exception is what this window wrote itself earlier in the run - the same
    /// object chosen in two formats goes in twice, the second over the first, which is the
    /// operator's call (the maintainer's decision, 2026-09-28).
    ///
    /// **Nothing is compiled and nothing is rolled back**, the framework's rule wherever it
    /// writes into a project. A refusal is TIA's own reason, returned rather than thrown.
    /// </summary>
    public sealed class ImportSession : TiaClient, IImportSession
    {
        /// <summary>Deeper than any tree an engineer builds, and a stop for one that loops.</summary>
        private const int MaxDepth = 32;

        /// <summary>
        /// Where a clean-up that did not happen is written down: an external source left behind
        /// changes nothing an import came to, and is exactly what nobody would otherwise know about.
        /// </summary>
        private readonly Log _log;

        /// <summary>
        /// The names this session has put into the project. What lets the second of two files
        /// declaring one object go over the first, which nobody could have been asked about -
        /// the question was answered before either went in.
        /// </summary>
        private readonly HashSet<string> _written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public ImportSession() : this(null)
        {
        }

        public ImportSession(Log log)
        {
            _log = log ?? Log.Nothing();
        }

        public string Check(ImportPlace place)
        {
            Resolve(place, out string problem);

            return problem;
        }

        /// <summary>
        /// **A place that cannot be found throws** rather than answering "nothing is there": an
        /// empty answer would send the run on as though nothing would be overwritten, and every
        /// file would then be refused for a reason the operator should have heard once.
        /// </summary>
        public IReadOnlyList<ExistingObject> Existing(ImportPlace place, IReadOnlyCollection<string> names)
        {
            Scope scope = Resolve(place, out string problem);

            if (scope == null) throw new InvalidOperationException(problem);

            return Search(scope, place.Tree, names).Select(hit => hit.Existing).ToList();
        }

        public FileImport Import(ImportPlace place, FileToImport file)
        {
            if (file?.File == null) throw new ArgumentNullException(nameof(file));

            ImportFile what = file.File;

            Scope scope = Resolve(place, out string problem);

            if (scope == null) return FileImport.Refused(what, problem);

            List<string> names = what.Objects.Select(o => o.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            List<Hit> hits;

            try
            {
                hits = Search(scope, place.Tree, names);
            }
            catch (Exception exception)
            {
                return FileImport.Refused(what, "What the folder already holds could not be read - " + Said(exception));
            }

            HashSet<string> asked = new HashSet<string>(file.Existing.Select(e => e.Name), StringComparer.OrdinalIgnoreCase);
            Hit unasked = hits.FirstOrDefault(hit => !asked.Contains(hit.Name) && !_written.Contains(hit.Name));

            if (unasked != null)
                return FileImport.Refused(what,
                    "'" + unasked.Name + "' is already in " + unasked.Where +
                    " and was not in the list you were asked about, so nothing was written.");

            FileImport result;

            try
            {
                switch (what.Format)
                {
                    case ImportFormat.SimaticMl:
                        result = Xml(scope, place.Tree, what, hits);
                        break;

                    case ImportFormat.Document:
                        result = Document(scope, place.Tree, what, hits);
                        break;

                    default:
                        result = Source(scope, place.Tree, what, names, hits);
                        break;
                }
            }
            catch (Exception exception)
            {
                return FileImport.Refused(what, Said(exception));
            }

            if (result.Outcome != ImportOutcome.Refused)
                foreach (string name in names) _written.Add(name);

            return result;
        }

        // ---- The three doors --------------------------------------------------------------------

        /// <summary>
        /// SimaticML, into the place's own composition - **whatever the file holds**: a data type
        /// offered to a block folder is TIA's to refuse (the maintainer's decision, 2026-09-28),
        /// not a second set of rules here.
        /// </summary>
        private static FileImport Xml(Scope scope, ObjectTree tree, ImportFile file, List<Hit> hits)
        {
            Hit home = hits.FirstOrDefault(hit => hit.Tree == tree);
            ImportOptions options = home == null ? ImportOptions.None : ImportOptions.Override;
            FileInfo path = new FileInfo(file.Path);
            int count;

            switch (tree)
            {
                case ObjectTree.Types:
                    count = Count((home?.Group as PlcTypeGroup ?? scope.Types).Types.Import(path, options));
                    break;

                case ObjectTree.TagTables:
                    count = Count((home?.Group as PlcTagTableGroup ?? scope.TagTables).TagTables.Import(path, options));
                    break;

                case ObjectTree.TechnologyObjects:
                    count = Count((home?.Group as TechnologicalInstanceDBGroup ?? scope.Technology).TechnologicalObjects.Import(path, options));
                    break;

                default:
                    count = Count((home?.Group as PlcBlockGroup ?? scope.Blocks).Blocks.Import(path, options));
                    break;
            }

            if (count == 0) return FileImport.Refused(file, "TIA Portal reported no error and imported nothing from it.");

            return Went(file, home == null ? null : new[] { home }, null);
        }

        /// <summary>
        /// SIMATIC SD. **It answers in its return value, not by throwing** - the one import call
        /// that does, as <c>ExportAsDocuments</c> is the one export call that does - so a clean
        /// return can still be a failure, and <c>PartialSuccess</c> is a file that went in with
        /// something TIA wants read.
        /// </summary>
        private static FileImport Document(Scope scope, ObjectTree tree, ImportFile file, List<Hit> hits)
        {
            if (tree != ObjectTree.Blocks && tree != ObjectTree.Types)
                return FileImport.Refused(file,
                    "SIMATIC SD holds blocks and PLC data types, and TIA Portal offers no way to import it into " +
                    ImportPlace.NameOf(tree) + ".");

            Hit home = hits.FirstOrDefault(hit => hit.Tree == tree);
            ImportDocumentOptions options = home == null ? ImportDocumentOptions.None : ImportDocumentOptions.Override;
            DirectoryInfo folder = new DirectoryInfo(Path.GetDirectoryName(file.Path) ?? ".");
            string name = Path.GetFileNameWithoutExtension(file.Path);

            DocumentImportResult result = tree == ObjectTree.Types
                ? (DocumentImportResult)(home?.Group as PlcTypeGroup ?? scope.Types).Types.ImportFromDocuments(folder, name, options)
                : (home?.Group as PlcBlockGroup ?? scope.Blocks).Blocks.ImportFromDocuments(folder, name, options);

            if (result == null) return FileImport.Refused(file, "Nothing came back from TIA Portal.");

            string said = Messages(result);

            switch (result.State)
            {
                case DocumentResultState.Success:
                    return Went(file, home == null ? null : new[] { home }, null);

                case DocumentResultState.PartialSuccess:
                    return Went(file, home == null ? null : new[] { home },
                                "TIA Portal answered partial success" + (said == null ? "" : ": " + said));

                default:
                    return FileImport.Refused(file, said ?? "TIA Portal answered failure and said nothing more.");
            }
        }

        /// <summary>
        /// A source: an external source made from the file, generated into the place, deleted.
        ///
        /// **<c>GenerateBlockOption.None</c>, where the core updater takes <c>KeepOnError</c>.**
        /// There a source is one block of a download and three that went in are three the project
        /// wants; here a file is the unit the operator chose, asked about and reported as one (the
        /// maintainer's decision, 2026-09-28), so it goes in whole or not at all.
        ///
        /// **A clean return is not taken as everything having arrived** - the lesson the core
        /// updater learned: what the file declares is looked for afterwards, and what is missing
        /// is said.
        /// </summary>
        private FileImport Source(Scope scope, ObjectTree tree, ImportFile file, List<string> names, List<Hit> hits)
        {
            if (tree != ObjectTree.Blocks && tree != ObjectTree.Types)
                return FileImport.Refused(file,
                    "A source generates blocks and PLC data types, and cannot go into " + ImportPlace.NameOf(tree) + ".");

            PlcExternalSourceComposition sources = scope.Sources.ExternalSources;
            PlcExternalSource source = null;

            try
            {
                source = sources.CreateFromFile(FreeName(sources, file.FileName), file.Path);

                if (tree == ObjectTree.Types) Generate(source, scope.Types as PlcTypeUserGroup);
                else Generate(source, scope.Blocks as PlcBlockUserGroup);
            }
            finally
            {
                Remove(source);
            }

            string note = null;

            if (names.Count > 0)
            {
                List<string> missing;

                try
                {
                    HashSet<string> there = new HashSet<string>(
                        Search(scope, tree, names).Select(hit => hit.Name), StringComparer.OrdinalIgnoreCase);

                    missing = names.Where(n => !there.Contains(n)).ToList();
                }
                catch (Exception exception)
                {
                    // The generation already happened; not being able to look afterwards is a
                    // note about the check, never a refusal of what went in.
                    _log.Warn("could not look for what " + file.FileName + " declares after generating it - " + Said(exception));
                    missing = new List<string>();
                }

                if (missing.Count == names.Count)
                    return FileImport.Refused(file,
                        "TIA Portal reported no error, and nothing the source declares is in the project afterwards: " +
                        Listed(missing) + ".");

                if (missing.Count > 0) note = Listed(missing) + " not in the project afterwards";
            }

            return Went(file, hits, note);
        }

        private static void Generate(PlcExternalSource source, PlcBlockUserGroup group)
        {
            if (group == null) source.GenerateBlocksFromSource(GenerateBlockOption.None);
            else source.GenerateBlocksFromSource(group, GenerateBlockOption.None);
        }

        private static void Generate(PlcExternalSource source, PlcTypeUserGroup group)
        {
            if (group == null) source.GenerateBlocksFromSource(GenerateBlockOption.None);
            else source.GenerateBlocksFromSource(group, GenerateBlockOption.None);
        }

        /// <summary>
        /// A name no external source in the project has yet. **The engineer may keep a source of
        /// that very name**, and deleting it after generating would destroy something that was
        /// never this window's: a clash is avoided rather than refused, and only what was made
        /// here is ever deleted.
        /// </summary>
        private static string FreeName(PlcExternalSourceComposition sources, string fileName)
        {
            if (sources.Find(fileName) == null) return fileName;

            string stem = Path.GetFileNameWithoutExtension(fileName);
            string extension = Path.GetExtension(fileName);

            for (int n = 2; n < 100; n++)
            {
                string candidate = stem + " (" + n + ")" + extension;

                if (sources.Find(candidate) == null) return candidate;
            }

            return stem + " (" + Guid.NewGuid().ToString("N").Substring(0, 8) + ")" + extension;
        }

        /// <summary>
        /// The external source is a step, not something the project should keep. **One that will
        /// not go is logged, not reported**: the file's own outcome is what the row is for.
        /// </summary>
        private void Remove(PlcExternalSource source)
        {
            if (source == null) return;

            string name;

            try
            {
                name = source.Name;
            }
            catch (Exception)
            {
                name = "(unnamed)";
            }

            try
            {
                source.Delete();
            }
            catch (Exception exception)
            {
                _log.Warn("the external source " + name + " was left in the project after generating from it - " + Said(exception));
            }
        }

        /// <summary>Overwritten where what it met was, or imported when it met nothing.</summary>
        private static FileImport Went(ImportFile file, IReadOnlyCollection<Hit> met, string note)
        {
            if (met == null || met.Count == 0) return FileImport.Imported(file, note);

            return FileImport.Overwritten(file, string.Join(", ", met.Select(hit => hit.Where).Distinct()), note);
        }

        // ---- Where the place is ---------------------------------------------------------------

        /// <summary>
        /// The place as TIA has it now, or null and why not. **Asked again on every call**: the
        /// engineer can rename or delete a folder while this window is open, and TIA Portal is the
        /// only one who knows.
        /// </summary>
        private Scope Resolve(ImportPlace place, out string problem)
        {
            problem = null;

            if (place == null)
            {
                problem = "No folder was named to import into.";
                return null;
            }

            if (AttachedProject == null)
            {
                problem = "The TIA Portal this attached to has no project open.";
                return null;
            }

            PlcSoftware software = Plc(place.Plc);

            if (software == null)
            {
                problem = "'" + AttachedProject.Name + "' has no PLC called '" + place.Plc + "'.";
                return null;
            }

            Scope scope;

            if (place.Unit == null)
            {
                scope = General(software);
            }
            else
            {
                try
                {
                    scope = Unit(software, place.Unit);
                }
                catch (Exception exception)
                {
                    problem = "The software units of '" + place.Plc + "' could not be read - " + Said(exception);
                    return null;
                }

                if (scope == null)
                {
                    problem = "PLC '" + place.Plc + "' has no software unit called '" + place.Unit + "'.";
                    return null;
                }
            }

            switch (place.Tree)
            {
                case ObjectTree.Types:
                    scope.Types = Walk(scope.TypeRoot, place, (group, name) => group.Groups.Find(name), out problem);
                    return scope.Types == null ? null : scope;

                case ObjectTree.TagTables:
                    scope.TagTables = Walk(scope.TagTableRoot, place, (group, name) => group.Groups.Find(name), out problem);
                    return scope.TagTables == null ? null : scope;

                case ObjectTree.TechnologyObjects:
                    if (scope.TechnologyRoot == null)
                    {
                        problem = "A software unit has no technology objects, so '" + place.Unit + "' has no such folder.";
                        return null;
                    }

                    scope.Technology = Walk(scope.TechnologyRoot, place, (group, name) => group.Groups.Find(name), out problem);
                    return scope.Technology == null ? null : scope;

                default:
                    scope.Blocks = Walk(scope.BlockRoot, place, (group, name) => group.Groups.Find(name), out problem);
                    return scope.Blocks == null ? null : scope;
            }
        }

        /// <summary>
        /// The place's folders, one level at a time, **by the names the engineer typed** - the
        /// same names the Add-In read off the tree, so an exact match is the right one.
        /// </summary>
        private static TGroup Walk<TGroup>(TGroup root, ImportPlace place, Func<TGroup, string, TGroup> child, out string problem)
            where TGroup : class
        {
            problem = null;
            TGroup group = root;

            for (int i = 0; i < place.Folders.Count; i++)
            {
                TGroup next = group == null ? null : child(group, place.Folders[i]);

                if (next == null)
                {
                    string parent = ImportPlace.NameOf(place.Tree) + string.Concat(place.Folders.Take(i).Select(f => " / " + f));

                    problem = "'" + parent + "' has no folder called '" + place.Folders[i] +
                              "' any more - it may have been renamed, moved or deleted since the menu was opened.";
                    return null;
                }

                group = next;
            }

            return group;
        }

        private static Scope General(PlcSoftware software) =>
            new Scope
            {
                BlockRoot = software.BlockGroup,
                TypeRoot = software.TypeGroup,
                TagTableRoot = software.TagTableGroup,
                TechnologyRoot = software.TechnologicalObjectGroup,
                Sources = software.ExternalSourceGroup
            };

        /// <summary>
        /// A software unit's scope, or null when the PLC has none of that name. **A method of its
        /// own and never inlined**: V17 has no software units, and a method naming their types
        /// fails while it is being compiled - so the general program must not share one with them.
        /// A unit has no technology objects; that tree stays null.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static Scope Unit(PlcSoftware software, string name)
        {
            PlcUnitSystemGroup group = software.GetService<PlcUnitProvider>()?.UnitGroup;

            if (group == null) return null;

            PlcUnitBase unit = null;

            foreach (PlcUnit one in group.Units)
                if (string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)) unit = one;

            if (unit == null)
                foreach (PlcSafetyUnit one in group.SafetyUnits)
                    if (string.Equals(one.Name, name, StringComparison.OrdinalIgnoreCase)) unit = one;

            if (unit == null) return null;

            return new Scope
            {
                BlockRoot = unit.BlockGroup,
                TypeRoot = unit.TypeGroup,
                TagTableRoot = unit.TagTableGroup,
                Sources = unit.ExternalSourceGroup
            };
        }

        // ---- What is already there ------------------------------------------------------------

        /// <summary>
        /// Every object of those names in the trees a file dropped into the place's tree could
        /// write into, and where each is.
        ///
        /// **Blocks and data types are searched together**: a source put into a block folder may
        /// declare a data type too, and TIA overwrites that one in place as silently as a block.
        /// **By TIA's own lookup**, <c>Find</c>, so whatever TIA takes as the same name, this does.
        /// </summary>
        private static List<Hit> Search(Scope scope, ObjectTree tree, IEnumerable<string> names)
        {
            List<string> wanted = (names ?? Enumerable.Empty<string>())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            List<Hit> hits = new List<Hit>();

            if (wanted.Count == 0) return hits;

            foreach (ObjectTree one in TreesFor(tree))
            {
                HashSet<string> left = new HashSet<string>(wanted, StringComparer.OrdinalIgnoreCase);
                List<string> path = new List<string>();

                switch (one)
                {
                    case ObjectTree.Types:
                        Look(scope.TypeRoot, one, path, left, hits, 0,
                             (group, name, where) => Of(group.Types.Find(name), one, where, group),
                             group => group.Groups, group => group.Name);
                        break;

                    case ObjectTree.TagTables:
                        Look(scope.TagTableRoot, one, path, left, hits, 0,
                             (group, name, where) => Of(group.TagTables.Find(name), one, where, group),
                             group => group.Groups, group => group.Name);
                        break;

                    case ObjectTree.TechnologyObjects:
                        Look(scope.TechnologyRoot, one, path, left, hits, 0,
                             (group, name, where) => Of(group.TechnologicalObjects.Find(name), one, where, group),
                             group => group.Groups, group => group.Name);
                        break;

                    default:
                        Look(scope.BlockRoot, one, path, left, hits, 0,
                             (group, name, where) => Of(group.Blocks.Find(name), one, where, group),
                             group => group.Groups, group => group.Name);
                        break;
                }
            }

            return hits;
        }

        private static IEnumerable<ObjectTree> TreesFor(ObjectTree tree)
        {
            switch (tree)
            {
                case ObjectTree.Blocks: return new[] { ObjectTree.Blocks, ObjectTree.Types };
                case ObjectTree.Types: return new[] { ObjectTree.Types, ObjectTree.Blocks };
                default: return new[] { tree };
            }
        }

        /// <summary>
        /// One tree, folder by folder, until every name is found. **The walk stops asking for a
        /// name once it is found**: a name is unique across the scope, so a second match cannot
        /// exist, and asking would cost one call into TIA per folder for nothing.
        /// </summary>
        private static void Look<TGroup>(
            TGroup group,
            ObjectTree tree,
            List<string> path,
            HashSet<string> left,
            List<Hit> hits,
            int depth,
            Func<TGroup, string, string, Hit> find,
            Func<TGroup, IEnumerable<TGroup>> children,
            Func<TGroup, string> nameOf)
            where TGroup : class
        {
            if (group == null || left.Count == 0 || depth > MaxDepth) return;

            string where = ImportPlace.NameOf(tree) + string.Concat(path.Select(f => " / " + f));

            foreach (string name in left.ToList())
            {
                Hit found = find(group, name, where);

                if (found == null) continue;

                hits.Add(found);
                left.Remove(name);
            }

            foreach (TGroup child in children(group).ToList())
            {
                if (left.Count == 0) return;

                path.Add(nameOf(child));
                Look(child, tree, path, left, hits, depth + 1, find, children, nameOf);
                path.RemoveAt(path.Count - 1);
            }
        }

        private static Hit Of(PlcBlock block, ObjectTree tree, string where, object group) =>
            block == null ? null : new Hit(block.Name, Kind(block), tree, where, group);

        private static Hit Of(PlcType type, ObjectTree tree, string where, object group) =>
            type == null ? null : new Hit(type.Name, CodingStyleNames.PlcStruct, tree, where, group);

        private static Hit Of(PlcTagTable table, ObjectTree tree, string where, object group) =>
            table == null ? null : new Hit(table.Name, CodingStyleNames.PlcTagTable, tree, where, group);

        private static Hit Of(TechnologicalInstanceDB technology, ObjectTree tree, string where, object group) =>
            technology == null ? null : new Hit(technology.Name, CodingStyleNames.TechnologicalInstanceDB, tree, where, group);

        /// <summary>
        /// **Most specific first**, or every technology object reads as an instance DB:
        /// `TechnologicalInstanceDB` derives from `InstanceDB`, which derives from `DataBlock`.
        /// </summary>
        private static string Kind(PlcBlock block)
        {
            if (block is TechnologicalInstanceDB) return CodingStyleNames.TechnologicalInstanceDB;
            if (block is InstanceDB) return CodingStyleNames.InstanceDB;
            if (block is ArrayDB) return CodingStyleNames.ArrayDB;
            if (block is GlobalDB) return CodingStyleNames.GlobalDB;
            if (block is OB) return CodingStyleNames.OB;
            if (block is FB) return CodingStyleNames.FB;
            if (block is FC) return CodingStyleNames.FC;

            return block.GetType().Name;
        }

        // ---- Words ----------------------------------------------------------------------------

        /// <summary>
        /// TIA's reason on one line. **Its messages carry line breaks**, and a result is one line
        /// in the window and one in the log.
        /// </summary>
        private static string Said(Exception exception) => Flat(exception?.Message);

        private static string Messages(DocumentImportResult result)
        {
            List<string> said = new List<string>();

            try
            {
                foreach (DocumentResultMessage message in result.Messages)
                {
                    string text = Flat(message?.Message);
                    if (!string.IsNullOrEmpty(text)) said.Add(text);
                }
            }
            catch (Exception)
            {
                // The state already says what happened; its messages are the detail.
            }

            return said.Count == 0 ? null : string.Join(" ", said);
        }

        private static string Flat(string text) =>
            string.Join(" ", (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0));

        private static string Listed(IEnumerable<string> names) => string.Join(", ", names.Select(n => "'" + n + "'"));

        private static int Count<T>(ICollection<T> imported) => imported == null ? 0 : imported.Count;

        // ---- Shapes ---------------------------------------------------------------------------

        /// <summary>
        /// The roots of one scope - a PLC's own program or one software unit - and, once resolved,
        /// the one folder of the place's tree.
        /// </summary>
        private sealed class Scope
        {
            public PlcBlockGroup BlockRoot;
            public PlcTypeGroup TypeRoot;
            public PlcTagTableGroup TagTableRoot;
            public TechnologicalInstanceDBGroup TechnologyRoot;
            public PlcExternalSourceSystemGroup Sources;

            public PlcBlockGroup Blocks;
            public PlcTypeGroup Types;
            public PlcTagTableGroup TagTables;
            public TechnologicalInstanceDBGroup Technology;
        }

        /// <summary>An object already there: its name, its kind, where it is, and the folder that holds it.</summary>
        private sealed class Hit
        {
            public Hit(string name, string kind, ObjectTree tree, string where, object group)
            {
                Name = name;
                Kind = kind;
                Tree = tree;
                Where = where;
                Group = group;
            }

            public string Name { get; }

            public string Kind { get; }

            public string Where { get; }

            /// <summary>The folder it sits in, as its tree's group type - where an overwrite goes.</summary>
            public object Group { get; }

            public ObjectTree Tree { get; }

            public ExistingObject Existing => new ExistingObject(Name, Kind, Where);
        }
    }
}
