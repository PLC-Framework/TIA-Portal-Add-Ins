using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Core;
using Core.Config;
using Core.Repo;
using Core.Repo.PlcProject;
using Core.Repo.PlcCore;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
using Siemens.Engineering.SW.ExternalSources;
using Siemens.Engineering.SW.Tags;
using Siemens.Engineering.SW.TechnologicalObjects;
using Siemens.Engineering.SW.Types;
using Siemens.Engineering.SW.Units;

namespace Satellite.CoreUpdater.Tia
{
    /// <summary>
    /// Attaches to a running TIA Portal through Openness, and reads a PLC out of it.
    ///
    /// Identical in the V20 and V21 executables, and duplicated for the reason the Add-In
    /// adapters already are: the surface it touches is the same in both - read off both
    /// assemblies - but it lives in `Siemens.Engineering` (token `d29ec89bac048f84`) and
    /// `Siemens.Engineering.Base` (token `29bfe5fdf4ba5d3b`), so one binary cannot serve both.
    ///
    /// > **This walk is the satellite's own, not `TiaProjectPlaces` a fourth time.** The
    /// > Add-In's version answers "where is this object", climbing upwards from something the
    /// > menu selected; this one answers "what is in this PLC", descending from a name. If it
    /// > ever grows into the same walk, **link the source file rather than keep a fourth
    /// > copy** - which the decision record already names as the mechanism for identical code
    /// > with Siemens dependencies.
    /// </summary>
    internal sealed class TiaSession : ITiaSession
    {
        /// <summary>
        /// Deep enough for any folder tree an engineer builds by hand, and a guarantee that a
        /// tree which somehow loops cannot hold the worker thread forever.
        /// </summary>
        private const int MaxDepth = 32;

        private TiaPortal _portal;
        private Project _project;

        /// <summary>How many objects a run may name before it only counts the rest.</summary>
        private const int ListedProblems = 10;

        /// <summary>How many objects pass between two updates of the text the window shows.</summary>
        private const int ProgressEvery = 10;

        private Action<string> _progress;
        private string _scratch;
        private int _done;
        private int _refused;

        /// <summary>What this run was asked to keep. Never null while a walk is running.</summary>
        private MapFilter _filter = MapFilter.Everything;

        /// <summary>
        /// What the current walk does with each object it reaches.
        ///
        /// **One tree walk, two jobs.** The survey counts and the map reads, and the only thing
        /// that must not drift between them is which folders were reached - so the walk is
        /// written once and what happens at the leaves is handed in. It matters more here than
        /// in V21: the counting half costs nothing and the reading half is one export per
        /// object, which is the whole reason there is a survey to tick against.
        /// </summary>
        private Action<PlcBlock, string, ProjectMap> _onBlock;
        private Action<PlcTagTable, string, ProjectMap> _onTable;
        private Action<PlcType, string, ProjectMap> _onType;

        /// <summary>
        /// Counts what the survey walks. **Core's, not this adapter's**: how many of a kind
        /// there are is not a TIA question, and two adapters keeping their own tallies would
        /// drift the first time one learned a new rule.
        /// </summary>
        private ProjectSurvey.Builder _survey;

        public TiaAttachment Attach(TiaWanted wanted)
        {
            TiaWanted asked = wanted ?? TiaWanted.Nothing;

            IList<TiaPortalProcess> running;

            try
            {
                running = TiaPortal.GetProcesses();
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    "The running TIA Portals could not be listed.\n\n" + exception.Message);
            }

            List<RunningPortal> considered = Describe(running);
            TiaPortalProcess chosen = Pick(running, considered, asked);

            if (chosen == null) return TiaAttachment.Failed(Why(running.Count, asked), considered);

            return Read(chosen, considered);
        }

        public TiaAttachment AttachTo(int processId)
        {
            IList<TiaPortalProcess> running;

            try
            {
                running = TiaPortal.GetProcesses();
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    "The running TIA Portals could not be listed.\n\n" + exception.Message);
            }

            List<RunningPortal> considered = Describe(running);
            TiaPortalProcess chosen = Process(running, processId);

            if (chosen == null)
                return TiaAttachment.Failed(
                    string.Format(CultureInfo.CurrentCulture,
                        "TIA Portal process {0} is no longer running.", processId),
                    considered);

            return Read(chosen, considered);
        }

        /// <summary>
        /// Which running instance this window belongs to, or null when nothing says.
        ///
        /// **The project decides and the ancestry only corroborates**, which is the opposite
        /// of how this was first written and is why it failed. Matching the process that
        /// launched this against the processes Openness lists found nothing at all with two
        /// TIA Portals open, in either version - see <see cref="TiaWanted"/>.
        /// </summary>
        private static TiaPortalProcess Pick(
            IList<TiaPortalProcess> running, List<RunningPortal> considered, TiaWanted wanted)
        {
            TiaPortalProcess byProject = Only(running, considered, one => wanted.IsProject(one.Project));
            if (byProject != null) return byProject;

            TiaPortalProcess byAncestry = Only(running, considered, one => wanted.IsAncestor(one.Id));
            if (byAncestry != null) return byAncestry;

            // Nothing said which one, and there is only one: that is not a guess.
            return running.Count == 1 ? running[0] : null;
        }

        /// <summary>
        /// The one instance a signal names, or null.
        ///
        /// **Two answers is no answer.** A signal that matches twice - the same project open
        /// in two instances, a chain running through both - has identified nothing, and taking
        /// the first would be exactly the guess this path exists to avoid. It falls through to
        /// the operator instead.
        /// </summary>
        private static TiaPortalProcess Only(
            IList<TiaPortalProcess> running, List<RunningPortal> considered, Func<RunningPortal, bool> wants)
        {
            RunningPortal found = null;

            foreach (RunningPortal one in considered)
            {
                if (!wants(one)) continue;
                if (found != null) return null;

                found = one;
            }

            return found == null ? null : Process(running, found.Id);
        }

        private static TiaPortalProcess Process(IEnumerable<TiaPortalProcess> running, int id)
        {
            foreach (TiaPortalProcess process in running)
            {
                try
                {
                    if (process.Id == id) return process;
                }
                catch (Exception)
                {
                    // An instance that will not say its id is not one anything can attach to.
                }
            }

            return null;
        }

        /// <summary>
        /// Attaches and reads what identifies the project, **keeping the attachment** - every
        /// call after this one needs it, and it belongs to the thread that obtained it.
        ///
        /// **Nothing is disposed, and that is the whole of the lesson.** The first version
        /// wrapped the portal in a `using`, on the reasoning that disposing an *attached*
        /// portal detaches the client rather than closing the application. **It closes TIA
        /// Portal** - confirmed on the VM, where the V20 instance shut down with the project
        /// open the moment this window said "Ready". `TiaPortal.Dispose` is how an Openness
        /// client shuts a portal down, and attaching does not change what the method means.
        /// </summary>
        private TiaAttachment Read(TiaPortalProcess process, List<RunningPortal> considered)
        {
            int id = process.Id;

            try
            {
                _portal = process.Attach();
                _project = _portal.Projects.FirstOrDefault();

                return TiaAttachment.To(id, _project?.Name, _project?.Path?.DirectoryName, considered);
            }
            catch (Exception exception)
            {
                return TiaAttachment.Failed(
                    string.Format(CultureInfo.CurrentCulture,
                        "TIA Portal process {0} refused the connection.\n\n{1}", id, exception.Message),
                    considered);
            }
        }

        public IReadOnlyList<string> Plcs()
        {
            List<string> names = new List<string>();

            foreach (PlcSoftware plc in Software()) names.Add(plc.Name);

            return names;
        }

        /// <summary>
        /// One PLC's software units, or none.
        ///
        /// **Empty covers three different truths and that is fine here**: the PLC is an
        /// S7-1200, which has no units; the project uses none; or this is running against a
        /// TIA old enough that the type does not exist - V17 has no `PlcUnitBase` at all, and
        /// asking for it there throws while the method is being compiled rather than when it
        /// is called. None of the three is a failure, and the window still offers the general
        /// program. Anything genuinely wrong surfaces where the map is walked, which does not
        /// swallow it.
        /// </summary>
        public IReadOnlyList<string> Units(string plc)
        {
            List<string> names = new List<string>();

            try
            {
                PlcSoftware software = Find(plc);
                if (software == null) return names;

                foreach (PlcUnitBase unit in UnitsOf(software)) names.Add(unit.Name);
            }
            catch (Exception)
            {
                return new List<string>();
            }

            return names;
        }

        /// <summary>
        /// **Nothing is exported here**, which is what makes the survey worth having in this
        /// version at all: the kind and the language are typed properties, so counting a PLC
        /// of four hundred costs one pass over the tree, where mapping it costs four hundred
        /// exports. This is what the operator ticks against before paying for any of them.
        /// </summary>
        public ProjectSurvey Survey(string plc, string unit)
        {
            _survey = ProjectSurvey.Building();

            _filter = MapFilter.Everything;
            _progress = null;

            _onBlock = Counted;
            _onTable = Counted;
            _onType = Counted;

            // A map handed in only as somewhere for the walk to put what it could not read.
            // **The survey drops those and the map that follows reports them**: it walks the
            // same tree a moment later, where a problem is part of a document somebody keeps
            // rather than a sentence under a row of tick boxes.
            Walk(plc, unit, ProjectMap.Of(_project?.Name, plc, unit, null));

            return _survey.Done();
        }

        public ProjectMap Map(string plc, string unit, MapFilter filter, Action<string> progress)
        {
            _filter = filter ?? MapFilter.Everything;
            _progress = progress;
            _done = 0;
            _refused = 0;

            _onBlock = Mapped;
            _onTable = Mapped;
            _onType = Mapped;

            ProjectMap map = ProjectMap.Of(
                _project?.Name, plc, unit,
                DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture));

            // Recorded only when it narrowed something. A map of the whole PLC carrying a
            // filter that keeps everything would say nothing, and null already means that.
            map.Filter = _filter.Narrows ? _filter : null;

            Walk(plc, unit, map);

            Noted(map);
            return map;
        }

        /// <summary>
        /// One PLC, or one of its software units, top to bottom.
        ///
        /// **Shared by the survey and the map** rather than written twice: the pair is only
        /// worth anything while the count an operator ticked against and the map they then
        /// asked for cover exactly the same folders.
        /// </summary>
        private void Walk(string plc, string unit, ProjectMap map)
        {
            PlcSoftware software = Find(plc);

            if (software == null)
            {
                map.Problems.Add("This project holds no PLC called '" + plc + "'.");
                return;
            }

            string wanted = Places.UnitOrNull(unit);

            if (wanted == null)
            {
                // The PLC's own program. Technology objects live only here - a software unit
                // exposes blocks, tag tables and types and nothing else.
                Blocks(software.BlockGroup, null, map);
                TechnologyObjects(software.TechnologicalObjectGroup, null, map);
                TagTables(software.TagTableGroup, null, map);
                Types(software.TypeGroup, null, map);

                return;
            }

            PlcUnitBase found = UnitsOf(software)
                .FirstOrDefault(one => string.Equals(one.Name, wanted, StringComparison.OrdinalIgnoreCase));

            if (found == null)
            {
                map.Problems.Add("'" + plc + "' has no software unit called '" + wanted + "'.");
                return;
            }

            Blocks(found.BlockGroup, null, map);
            TagTables(found.TagTableGroup, null, map);
            Types(found.TypeGroup, null, map);
        }

        // ---- What happens at a leaf -----------------------------------------------------------

        /// <summary>
        /// **The filter is applied here, before the export.** That is the whole of its value in
        /// this version: the kind and the language are two typed property reads, and the line
        /// below them writes the object out to disk and reads it back. Filtering afterwards
        /// would pay the minutes and then throw the answer away.
        /// </summary>
        private void Mapped(PlcBlock block, string folder, ProjectMap map)
        {
            string kind = Kind(block);

            if (!_filter.Wants(kind, Language(block))) return;

            map.Objects.Add(Of(block, kind, folder, map));
            Tick();
        }

        private void Mapped(PlcTagTable table, string folder, ProjectMap map)
        {
            if (!_filter.Wants(CodingStyleNames.PlcTagTable, null)) return;

            map.Objects.Add(Of(table, folder, map));
            Tick();
        }

        private void Mapped(PlcType type, string folder, ProjectMap map)
        {
            if (!_filter.Wants(CodingStyleNames.PlcStruct, null)) return;

            map.Objects.Add(Of(type, folder, map));
            Tick();
        }

        private void Counted(PlcBlock block, string folder, ProjectMap notes) =>
            _survey.Found(Kind(block), Language(block));

        private void Counted(PlcTagTable table, string folder, ProjectMap notes) =>
            _survey.Found(CodingStyleNames.PlcTagTable, null);

        private void Counted(PlcType type, string folder, ProjectMap notes) =>
            _survey.Found(CodingStyleNames.PlcStruct, null);

        /// <summary>
        /// A block's programming language as the enum spells it - <c>SCL</c>, <c>LAD</c>,
        /// <c>GRAPH</c>, <c>DB</c>, <c>F_DB</c> - or null when TIA will not answer for it.
        ///
        /// **Null keeps the object.** It passes every language filter, so a block whose
        /// language could not be read is mapped rather than silently dropped by a choice the
        /// operator made about something else.
        /// </summary>
        private static string Language(PlcBlock block)
        {
            try
            {
                return block.ProgrammingLanguage.ToString();
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ---- Writing a plan into the project ------------------------------------------------

        /// <summary>
        /// Where a source lands, decided by the extension the core gave it: <c>.scl</c> is a
        /// block, <c>.udt</c> a PLC data type, <c>.xlsx</c> a tag table of constants.
        ///
        /// **The extension is the core's own answer**, not a guess about content: the repository
        /// writes one kind per extension, and `core.json` carries the file.
        /// </summary>
        private enum Destination { Block, Type, TagTable, Unknown }

        public ImportReport Import(string plc, string unit, DownloadPlan plan, Action<string> progress)
        {
            ImportReport report = new ImportReport();

            if (plan == null || plan.Nodes.Count == 0) return report;

            PlcSoftware software = Find(plc);

            if (software == null)
            {
                report.Add("This project holds no PLC called '" + plc + "'.");
                return report;
            }

            string wanted = Places.UnitOrNull(unit);
            PlcUnitBase into = null;

            if (wanted != null)
            {
                into = UnitsOf(software)
                    .FirstOrDefault(one => string.Equals(one.Name, wanted, StringComparison.OrdinalIgnoreCase));

                if (into == null)
                {
                    report.Add("'" + plc + "' has no software unit called '" + wanted + "'.");
                    return report;
                }
            }

            string scratch = Workspace(report.Add);

            if (scratch == null) return report;

            int done = 0;

            try
            {
                foreach (PlannedNode one in plan.Nodes)
                {
                    done++;

                    progress?.Invoke("Importing " + done + " of " + plan.Nodes.Count + " - " + one.Node.Base);

                    // A dependency already at the version the core stands behind. Reported rather
                    // than left out, so the counts can be read against what was asked for.
                    if (one.Action == DownloadAction.Skip)
                    {
                        report.Add(ImportedNode.Left(one));
                        continue;
                    }

                    report.Add(One(software, into, one, scratch));
                }
            }
            finally
            {
                // Kept when anything is left only as a file there - see Relocated.
                if (report.Stranded == 0) Clear(scratch);
            }

            return report;
        }

        private ImportedNode One(PlcSoftware software, PlcUnitBase into, PlannedNode planned, string scratch)
        {
            if (string.IsNullOrWhiteSpace(planned.Source) || !File.Exists(planned.Source))
                return ImportedNode.Refused(planned, "Its source is not in the copied core: " + planned.Source);

            try
            {
                switch (Where(planned.Source))
                {
                    case Destination.TagTable:
                        return TagTable(software, into, planned, scratch);

                    case Destination.Block:
                    case Destination.Type:
                        return FromSource(software, into, planned, scratch);
                }

                return ImportedNode.Refused(
                    planned, "Nothing here knows what to do with a " + Path.GetExtension(planned.Source) + " file.");
            }
            catch (Exception exception)
            {
                // Know-how protected, in use, inconsistent, or a source TIA will not take. One
                // object that would not go in, not the end of a download of thirty.
                return ImportedNode.Refused(planned, exception.Message);
            }
        }

        /// <summary>
        /// A tag table of constants, which the core keeps as <c>.xlsx</c> - **built object by
        /// object, because Openness will not read the workbook.**
        ///
        /// The first version handed the file straight to `PlcTagTableComposition.Import`, which
        /// takes a `FileInfo` and no format and looked like the door. It is not: on the VM it
        /// answered *"Invalid XML encountered while reading Simatic ML file: Data at the root
        /// level is invalid. Line 1, position 1."* TIA Portal imports Excel from its own user
        /// interface and Openness does not expose that path, so the table, its constants and its
        /// tags are created one at a time - `Create(name)`, `Create(name, dataType, value)` and
        /// `Create(name, dataType, logicalAddress)`, all of which the assembly does declare.
        ///
        /// **The folder is the node's family, like every other object in this download**, and not
        /// the workbook's own `TagTable Properties` sheet. That sheet names a TIA tree folder in
        /// one plant's convention - `90_LIbrary\ADT\ADT` - which is where the table happens to
        /// sit rather than where the core says it belongs; a table placed by it would land
        /// somewhere *sync folders with core* then wants to move it out of.
        ///
        /// **A table already there is deleted and rebuilt**, which is what replacing means: an
        /// enumeration that has dropped a constant must drop it here too, and creating over the
        /// top would leave the retired one behind with nothing saying so. **It is looked for in
        /// the whole tree, not only in the family's folder** - a table of that name anywhere else
        /// made the create fail, a name being unique across a PLC's software - and the new one is
        /// built in the family's folder, so a download also puts a misplaced table back. The old
        /// one is exported first and put back where it was if the new one cannot be made, for the
        /// reason <see cref="Relocated"/> records.
        ///
        /// **A constant TIA refuses is named and the rest still go in.** Nothing is rolled back
        /// in this window, so the honest outcome is the table as complete as the workbook allowed
        /// with what would not go named - a constants table quietly three entries short is the
        /// silent hole every other part of this feature is shaped to avoid.
        /// </summary>
        private static ImportedNode TagTable(
            PlcSoftware software, PlcUnitBase into, PlannedNode planned, string scratch)
        {
            ConstantsWorkbook workbook = ConstantsWorkbook.Of(planned.Source);

            if (!workbook.Read) return ImportedNode.Refused(planned, workbook.Problem);

            PlcTagTableGroup root = into == null ? (PlcTagTableGroup)software.TagTableGroup : into.TagTableGroup;
            PlcTagTableGroup group = Tables(root, planned.Folder);

            PlcTagTable existing = FoundTable(root, workbook.Name);
            string backup = null;
            PlcTagTableGroup home = null;
            bool moved = false;

            if (existing != null)
            {
                // TIA rebuilds the default table if it is deleted, and it never comes from the core.
                if (existing.IsDefault) return ImportedNode.Refused(planned, "It is the PLC's default tag table.");

                moved = group.TagTables.Find(workbook.Name) == null;
                home = existing.Parent as PlcTagTableGroup ?? root;
                backup = Backup(scratch, workbook.Name);

                existing.Export(new FileInfo(backup), ExportOptions.WithDefaults);
                existing.Delete();
            }

            PlcTagTable table;

            try
            {
                table = group.TagTables.Create(workbook.Name);
            }
            catch (Exception exception)
            {
                if (backup == null) throw;

                PlcTagTableGroup back = home;

                return PutBack(
                    planned, exception.Message, backup,
                    () => back.TagTables.Import(new FileInfo(backup), ImportOptions.Override));
            }

            if (backup != null) Drop(backup);

            List<string> refused = new List<string>();

            foreach (PlcCoreConstant constant in workbook.Constants)
            {
                try
                {
                    Describe(
                        table.UserConstants.Create(constant.Name, constant.DataType, constant.Value).Comment,
                        constant.Comment);
                }
                catch (Exception exception)
                {
                    refused.Add(constant.Name + ": " + exception.Message);
                }
            }

            foreach (PlcCoreTag tag in workbook.Tags)
            {
                try
                {
                    Describe(Made(table, tag).Comment, tag.Comment);
                }
                catch (Exception exception)
                {
                    refused.Add(tag.Name + ": " + exception.Message);
                }
            }

            if (refused.Count > 0)
                return ImportedNode.Refused(planned, Named(refused, workbook.Constants.Count + workbook.Tags.Count));

            return moved ? ImportedNode.Moved(planned, planned.From) : ImportedNode.Went(planned);
        }

        /// <summary>
        /// One tag. **A tag with no logical address loses its data type**, because Openness
        /// offers `Create(name)` and `Create(name, dataType, logicalAddress)` and nothing in
        /// between - so a type without an address cannot be expressed. Every tag in the core's
        /// own tables carries one; this is the branch for a workbook that does not.
        /// </summary>
        private static PlcTag Made(PlcTagTable table, PlcCoreTag tag) =>
            string.IsNullOrEmpty(tag.Address)
                ? table.Tags.Create(tag.Name)
                : table.Tags.Create(tag.Name, tag.DataType, tag.Address);

        /// <summary>
        /// The folder <c>core/adt</c> under the tag table tree, made on the way down - the same
        /// shape as the block and type trees, and found before created for the same reason: a
        /// second download into one family must not make a second folder beside the first.
        /// </summary>
        private static PlcTagTableGroup Tables(PlcTagTableGroup root, string folder)
        {
            PlcTagTableGroup group = root;

            foreach (string name in Segments(folder))
            {
                PlcTagTableUserGroupComposition groups = group.Groups;

                group = groups.Find(name) ?? (PlcTagTableGroup)groups.Create(name);
            }

            return group;
        }

        /// <summary>
        /// The workbook's comment onto every editing language the project has.
        ///
        /// **`MultilingualTextItemComposition` has no `Create`** - the items that exist are the
        /// languages the project was set up with, so this writes into those and a project with
        /// none simply keeps no comment. A comment is documentation, and failing an import over
        /// one would be the wrong trade.
        /// </summary>
        private static void Describe(MultilingualText comment, string text)
        {
            if (comment == null || string.IsNullOrEmpty(text)) return;

            try
            {
                foreach (MultilingualTextItem item in comment.Items) item.Text = text;
            }
            catch (Exception)
            {
                // Documentation, not the object. See above.
            }
        }

        /// <summary>
        /// What would not go in, **named rather than counted** - up to five, then a count. A
        /// table three constants short has to say which three; one ninety short would otherwise
        /// fill the report with a line each and bury every other object's outcome.
        /// </summary>
        private static string Named(IReadOnlyList<string> refused, int of)
        {
            string said = refused.Count + " of " + of + " entries would not go in: ";

            for (int i = 0; i < refused.Count && i < 5; i++)
                said += (i > 0 ? "; " : string.Empty) + refused[i];

            return refused.Count > 5
                ? said + "; and " + (refused.Count - 5) + " more."
                : said + ".";
        }

        /// <summary>
        /// A block or a PLC data type, through the external source folder - the only way in for
        /// an <c>.scl</c> or a <c>.udt</c>, and the mirror of how the export writes them out.
        ///
        /// **The external source is deleted again.** It is a step, not something the project
        /// should keep: one per imported block would fill a folder nobody asked to fill, and the
        /// next download would meet its own leftovers.
        ///
        /// **`KeepOnError`**, because a source that generates three blocks and fails on the
        /// fourth has still produced three the project wants; the alternative throws away work
        /// that is already correct.
        /// </summary>
        private static ImportedNode FromSource(
            PlcSoftware software, PlcUnitBase into, PlannedNode planned, string scratch)
        {
            // Copied under the project's own repo\tmp\ first: CreateFromFile reads from where it
            // is pointed, and pointing it at the copied core would leave TIA holding a file the
            // next mirror wants to replace.
            string path = Path.Combine(scratch, Path.GetFileName(planned.Source));

            File.Copy(planned.Source, path, true);

            PlcExternalSourceSystemGroup sources =
                into == null ? software.ExternalSourceGroup : into.ExternalSourceGroup;

            PlcExternalSource source = null;

            try
            {
                source = sources.ExternalSources.CreateFromFile(Path.GetFileName(path), path);

                PlcExternalSource from = source;

                if (Where(planned.Source) == Destination.Type)
                {
                    PlcTypeGroup root = into == null ? (PlcTypeGroup)software.TypeGroup : into.TypeGroup;
                    PlcTypeUserGroup family = Types(software, into, planned.Folder);
                    PlcTypeGroup destination = (PlcTypeGroup)family ?? root;
                    string name = planned.Node.Base;

                    PlcType elsewhere = destination.Types.Find(name) != null ? null : FoundType(root, name);

                    return Relocated(
                        planned, scratch, elsewhere != null,
                        backup => elsewhere.Export(new FileInfo(backup), ExportOptions.WithDefaults),
                        () => elsewhere.Delete(),
                        () => Generate(from, family),
                        () => destination.Types.Find(name) != null,
                        Home(elsewhere?.Parent as PlcTypeGroup, root, (group, backup) =>
                            group.Types.Import(new FileInfo(backup), ImportOptions.Override)));
                }
                else
                {
                    PlcBlockGroup root = into == null ? (PlcBlockGroup)software.BlockGroup : into.BlockGroup;
                    PlcBlockUserGroup family = Blocks(software, into, planned.Folder);
                    PlcBlockGroup destination = (PlcBlockGroup)family ?? root;
                    string name = planned.Node.Base;

                    PlcBlock elsewhere = destination.Blocks.Find(name) != null ? null : FoundBlock(root, name);

                    return Relocated(
                        planned, scratch, elsewhere != null,
                        backup => elsewhere.Export(new FileInfo(backup), ExportOptions.WithDefaults),
                        () => elsewhere.Delete(),
                        () => Generate(from, family),
                        () => destination.Blocks.Find(name) != null,
                        Home(elsewhere?.Parent as PlcBlockGroup, root, (group, backup) =>
                            group.Blocks.Import(new FileInfo(backup), ImportOptions.Override)));
                }
            }
            finally
            {
                Remove(source);
                Drop(path);
            }
        }

        /// <summary>
        /// Writes one block or data type, **and puts it in the folder its family names even when
        /// the project already had it somewhere else**.
        ///
        /// **TIA overwrites an object that exists, silently and where it is** - measured on the
        /// VM: no refusal, no message, and the new block in the old block's folder. With nothing
        /// of that name elsewhere, generating into the family's folder is all there is to do, and
        /// that includes the ordinary replace of one already in the right place.
        ///
        /// **Found elsewhere, it is taken out first**, because Openness has no move and the name
        /// is unique across a PLC's software: exported, deleted, and generated again where it
        /// belongs. That leaves a moment in which the old object is only a file, so:
        ///
        /// - **If it will not export, nothing is deleted** and it is replaced where it stands, as
        ///   TIA would have done anyway - written, not moved, and the report says why. A know-how
        ///   protected block and an inconsistent one are both refused on the way out.
        /// - **If the new one will not generate, the old one is put back** in the folder it came
        ///   from - a failed download must not cost the project a block it already had. A source
        ///   that generates nothing of that name counts as not generating.
        /// - **If even that fails, the file is kept and named**, and the scratch folder stays.
        ///   An object nobody can find again is the one outcome this must never produce.
        /// </summary>
        private static ImportedNode Relocated(
            PlannedNode planned,
            string scratch,
            bool elsewhere,
            Action<string> export,
            Action delete,
            Action generate,
            Func<bool> arrived,
            Action<string> restore)
        {
            if (!elsewhere)
            {
                generate();
                return ImportedNode.Went(planned);
            }

            string backup = Backup(scratch, planned.Node.Base);

            try
            {
                export(backup);
            }
            catch (Exception exception)
            {
                generate();

                return ImportedNode.InPlace(
                    planned,
                    "Replaced where it was rather than moved into " + planned.Folder +
                    ": TIA would not export it to move it. " + exception.Message);
            }

            delete();

            try
            {
                generate();

                if (!arrived())
                    throw new InvalidOperationException(
                        "The source did not produce an object called '" + planned.Node.Base + "'.");
            }
            catch (Exception exception)
            {
                return PutBack(planned, exception.Message, backup, () => restore(backup));
            }

            Drop(backup);

            return ImportedNode.Moved(planned, planned.From);
        }

        /// <summary>
        /// The old object back into the folder it was taken out of, after the new one would not
        /// go in. **Refused** when that works - the project is as it was, and the reason is TIA's;
        /// **stranded** when it does not, with the file that is now the only copy.
        /// </summary>
        private static ImportedNode PutBack(PlannedNode planned, string refusal, string backup, Action restore)
        {
            try
            {
                restore();
            }
            catch (Exception exception)
            {
                return ImportedNode.Stranded(
                    planned,
                    "The core's version would not go in (" + refusal + "), and the one the project had " +
                    "would not go back in either (" + exception.Message + "). It is kept at " + backup + ".",
                    backup);
            }

            Drop(backup);

            return ImportedNode.Refused(
                planned, "The core's version would not go in, so the one the project had was put back: " + refusal);
        }

        /// <summary>
        /// How to put an object back into the folder it came from - **that folder**, found before
        /// the delete, and the tree's root when it cannot be told.
        /// </summary>
        private static Action<string> Home<TGroup>(TGroup home, TGroup root, Action<TGroup, string> import)
            where TGroup : class =>
            backup => import(home ?? root, backup);

        /// <summary>
        /// Named after the object, like a move's file: when it is the only copy of something it
        /// has to be recognisable in the folder it was left in.
        /// </summary>
        private static string Backup(string scratch, string name) =>
            Path.Combine(scratch, "replace-" + Core.Exports.ExportTree.Segment(name) + ".xml");

        private static void Generate(PlcExternalSource source, PlcBlockUserGroup group)
        {
            if (group == null) source.GenerateBlocksFromSource(GenerateBlockOption.KeepOnError);
            else source.GenerateBlocksFromSource(group, GenerateBlockOption.KeepOnError);
        }

        private static void Generate(PlcExternalSource source, PlcTypeUserGroup group)
        {
            if (group == null) source.GenerateBlocksFromSource(GenerateBlockOption.KeepOnError);
            else source.GenerateBlocksFromSource(group, GenerateBlockOption.KeepOnError);
        }

        private static void Remove(PlcExternalSource source)
        {
            try
            {
                source?.Delete();
            }
            catch (Exception)
            {
                // It is a step rather than something to keep; one that will not go is untidy and
                // nothing more, and saying so would bury the object's own outcome.
            }
        }

        /// <summary>
        /// The folder <c>core/adt/queue</c> under the block tree, made on the way down.
        ///
        /// **Found before created**, so a second download into the same family does not make a
        /// second folder beside the first.
        /// </summary>
        private static PlcBlockUserGroup Blocks(PlcSoftware software, PlcUnitBase into, string folder)
        {
            PlcBlockGroup root = into == null ? (PlcBlockGroup)software.BlockGroup : into.BlockGroup;
            PlcBlockUserGroup group = null;

            foreach (string name in Segments(folder))
            {
                PlcBlockUserGroupComposition groups = group == null ? root.Groups : group.Groups;

                group = groups.Find(name) ?? groups.Create(name);
            }

            return group;
        }

        private static PlcTypeUserGroup Types(PlcSoftware software, PlcUnitBase into, string folder)
        {
            PlcTypeGroup root = into == null ? (PlcTypeGroup)software.TypeGroup : into.TypeGroup;
            PlcTypeUserGroup group = null;

            foreach (string name in Segments(folder))
            {
                PlcTypeUserGroupComposition groups = group == null ? root.Groups : group.Groups;

                group = groups.Find(name) ?? groups.Create(name);
            }

            return group;
        }

        private static IEnumerable<string> Segments(string folder) =>
            string.IsNullOrEmpty(folder)
                ? new string[0]
                : folder.Split(new[] { Places.Separator }, StringSplitOptions.RemoveEmptyEntries);

        private static Destination Where(string source)
        {
            string extension = (Path.GetExtension(source) ?? string.Empty).ToLowerInvariant();

            if (extension == ".udt") return Destination.Type;
            if (extension == ".xlsx") return Destination.TagTable;
            if (extension == ".scl" || extension == ".awl" || extension == ".db") return Destination.Block;

            return Destination.Unknown;
        }

        /// <summary>
        /// Where a source is put before TIA reads it: the project's own <c>repo\tmp\</c>, which
        /// exists for exactly this. Inside the project rather than in %TEMP% for the reason the
        /// coding-style check already records - what is in these files is somebody's source code.
        /// </summary>
        private string Workspace(Action<string> problem)
        {
            string folder = RepoPaths.TmpFor(_project?.Path?.DirectoryName);

            if (folder == null)
            {
                problem("This project has no folder, so there is nowhere to put a source.");
                return null;
            }

            try
            {
                Directory.CreateDirectory(folder);
                return folder;
            }
            catch (Exception exception)
            {
                problem("The scratch folder could not be made: " + exception.Message);
                return null;
            }
        }

        private static void Drop(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // The folder goes at the end of the run, and a file TIA still holds is not a
                // reason to report a successful import as a failure.
            }
        }

        private static void Clear(string folder)
        {
            try
            {
                Directory.Delete(folder, true);
            }
            catch (Exception)
            {
                // Empty by now; the next run reuses it.
            }
        }

        // ---- Moving an object into the folder its family names --------------------------------

        /// <summary>
        /// How deep a find will go looking for an object. A hand-built folder tree is three or
        /// four deep; this is a guard against a cycle rather than a limit anybody meets.
        /// </summary>
        private const int FindDepth = 32;

        /// <summary>
        /// **Openness has no move.** All 2,269 types were searched for one - no `Move`, no `Cut`,
        /// no `Reparent`, no `ChangeGroup`, no `Relocate` - so this exports the object, deletes
        /// it, and imports it into the folder its family names. The order is forced rather than
        /// chosen: an object's name is unique across a PLC's software, so the copy cannot go into
        /// its new folder while the original is still in the old one.
        ///
        /// **The scratch folder is kept when anything was left stranded.** Between the delete and
        /// the import the object exists only as a file, and clearing the folder after a refusal
        /// would throw away the only copy. Every other run clears it, because what is in there is
        /// somebody's source code.
        /// </summary>
        public SyncReport Sync(string plc, string unit, SyncPlan plan, Action<string> progress)
        {
            SyncReport report = new SyncReport();

            if (plan == null || plan.Count == 0) return report;

            PlcSoftware software = Find(plc);

            if (software == null)
            {
                report.Add("This project holds no PLC called '" + plc + "'.");
                return report;
            }

            string wanted = Places.UnitOrNull(unit);
            PlcUnitBase into = null;

            if (wanted != null)
            {
                into = UnitsOf(software)
                    .FirstOrDefault(one => string.Equals(one.Name, wanted, StringComparison.OrdinalIgnoreCase));

                if (into == null)
                {
                    report.Add("'" + plc + "' has no software unit called '" + wanted + "'.");
                    return report;
                }
            }

            string scratch = Workspace(report.Add);

            if (scratch == null) return report;

            int done = 0;

            try
            {
                foreach (MisplacedObject one in plan.Objects)
                {
                    done++;

                    progress?.Invoke("Moving " + done + " of " + plan.Count + " - " + one.Name);

                    report.Add(Moved(software, into, one, scratch));
                }
            }
            finally
            {
                if (report.Stranded == 0) Clear(scratch);
            }

            return report;
        }

        private MovedObject Moved(
            PlcSoftware software, PlcUnitBase into, MisplacedObject planned, string scratch)
        {
            // Named after the object rather than reused like the map's read.xml: a file that is
            // the only copy of something has to be recognisable in the folder it was left in.
            string file = Path.Combine(scratch, "move-" + Core.Exports.ExportTree.Segment(planned.Name) + ".xml");

            try
            {
                switch (For(planned.Kind))
                {
                    case Destination.Type:
                        return MovedType(software, into, planned, file);

                    case Destination.TagTable:
                        return MovedTable(software, into, planned, file);

                    default:
                        return MovedBlock(software, into, planned, file);
                }
            }
            catch (Exception exception)
            {
                // Everything past the delete is caught inside each of the three, so anything
                // arriving here happened before it: the object is still where it was.
                return MovedObject.Refused(planned, exception.Message);
            }
        }

        private static MovedObject MovedBlock(
            PlcSoftware software, PlcUnitBase into, MisplacedObject planned, string file)
        {
            PlcBlockGroup root = into == null ? (PlcBlockGroup)software.BlockGroup : into.BlockGroup;
            PlcBlock block = FoundBlock(root, planned.Name);

            if (block == null) return MovedObject.Refused(planned, "It is no longer in this PLC.");

            PlcBlockUserGroup destination = Blocks(software, into, planned.Family);

            if (destination == null)
                return MovedObject.Refused(planned, "'" + planned.Family + "' is not a folder this can build.");

            // WithDefaults, not None: this is the whole object on its way back in, where the
            // coding-style check's throwaway export is read for one string and deleted.
            block.Export(new FileInfo(file), ExportOptions.WithDefaults);
            block.Delete();

            try
            {
                destination.Blocks.Import(new FileInfo(file), ImportOptions.Override);
            }
            catch (Exception exception)
            {
                return MovedObject.Stranded(planned, exception.Message, file);
            }

            Drop(file);

            return MovedObject.Went(planned);
        }

        private static MovedObject MovedType(
            PlcSoftware software, PlcUnitBase into, MisplacedObject planned, string file)
        {
            PlcTypeGroup root = into == null ? (PlcTypeGroup)software.TypeGroup : into.TypeGroup;
            PlcType type = FoundType(root, planned.Name);

            if (type == null) return MovedObject.Refused(planned, "It is no longer in this PLC.");

            PlcTypeUserGroup destination = Types(software, into, planned.Family);

            if (destination == null)
                return MovedObject.Refused(planned, "'" + planned.Family + "' is not a folder this can build.");

            type.Export(new FileInfo(file), ExportOptions.WithDefaults);
            type.Delete();

            try
            {
                destination.Types.Import(new FileInfo(file), ImportOptions.Override);
            }
            catch (Exception exception)
            {
                return MovedObject.Stranded(planned, exception.Message, file);
            }

            Drop(file);

            return MovedObject.Went(planned);
        }

        /// <summary>
        /// A tag table moves the same way, **and here the SimaticML is the only door that
        /// works**: `PlcTagTableComposition.Import` reads SimaticML and refuses the workbook the
        /// core keeps, which is why a download builds a table object by object. What comes out of
        /// a project is SimaticML, so a move needs none of that.
        /// </summary>
        private static MovedObject MovedTable(
            PlcSoftware software, PlcUnitBase into, MisplacedObject planned, string file)
        {
            PlcTagTableGroup root = into == null ? (PlcTagTableGroup)software.TagTableGroup : into.TagTableGroup;
            PlcTagTable table = FoundTable(root, planned.Name);

            if (table == null) return MovedObject.Refused(planned, "It is no longer in this PLC.");

            // TIA rebuilds the default table if it is deleted, and it never comes from the core.
            if (table.IsDefault)
                return MovedObject.Refused(planned, "It is the PLC's default tag table.");

            PlcTagTableGroup destination = Tables(root, planned.Family);

            table.Export(new FileInfo(file), ExportOptions.WithDefaults);
            table.Delete();

            try
            {
                destination.TagTables.Import(new FileInfo(file), ImportOptions.Override);
            }
            catch (Exception exception)
            {
                return MovedObject.Stranded(planned, exception.Message, file);
            }

            Drop(file);

            return MovedObject.Went(planned);
        }

        /// <summary>
        /// Which tree an object lives in, from the kind the map recorded.
        ///
        /// **From the kind, never from the folder the map wrote.** A folder begins with TIA's own
        /// name for its tree, which follows the interface language; the kind is
        /// `CodingStyleNames`' vocabulary, which is ours and is the same in every language.
        ///
        /// Everything that is not a data type or a tag table is a `PlcBlock` - an OB, an FC, an
        /// FB, any of the data blocks, a technology object - so that is the default rather than a
        /// list to keep in step with the object model.
        /// </summary>
        private static Destination For(string kind)
        {
            if (string.Equals(kind, CodingStyleNames.PlcStruct, StringComparison.OrdinalIgnoreCase))
                return Destination.Type;

            if (string.Equals(kind, CodingStyleNames.PlcTagTable, StringComparison.OrdinalIgnoreCase))
                return Destination.TagTable;

            return Destination.Block;
        }

        /// <summary>
        /// The object, wherever in the tree it is. **By name**, which is exact: a name is unique
        /// across a PLC's software, so this cannot find the wrong one.
        /// </summary>
        private static PlcBlock FoundBlock(PlcBlockGroup group, string name, int depth = 0)
        {
            if (group == null || depth > FindDepth) return null;

            PlcBlock found = group.Blocks.Find(name);

            if (found != null) return found;

            foreach (PlcBlockUserGroup child in group.Groups)
            {
                found = FoundBlock(child, name, depth + 1);

                if (found != null) return found;
            }

            return null;
        }

        private static PlcType FoundType(PlcTypeGroup group, string name, int depth = 0)
        {
            if (group == null || depth > FindDepth) return null;

            PlcType found = group.Types.Find(name);

            if (found != null) return found;

            foreach (PlcTypeUserGroup child in group.Groups)
            {
                found = FoundType(child, name, depth + 1);

                if (found != null) return found;
            }

            return null;
        }

        private static PlcTagTable FoundTable(PlcTagTableGroup group, string name, int depth = 0)
        {
            if (group == null || depth > FindDepth) return null;

            PlcTagTable found = group.TagTables.Find(name);

            if (found != null) return found;

            foreach (PlcTagTableUserGroup child in group.Groups)
            {
                found = FoundTable(child, name, depth + 1);

                if (found != null) return found;
            }

            return null;
        }

        // ---- The walk -----------------------------------------------------------------------

        private void Blocks(PlcBlockGroup group, string parent, ProjectMap map, int depth = 0)
        {
            if (group == null || Deep(depth, parent, map)) return;

            string here = Join(parent, group.Name);

            Each(map, here, () =>
            {
                foreach (PlcBlock block in group.Blocks) _onBlock(block, here, map);
            });

            foreach (PlcBlockUserGroup child in Children(group.Groups, here, map)) Blocks(child, here, map, depth + 1);

            // SystemBlockGroups is deliberately not walked: TIA fills and names it, and no
            // core block is ever in it.
        }

        private void TechnologyObjects(TechnologicalInstanceDBGroup group, string parent, ProjectMap map, int depth = 0)
        {
            if (group == null || Deep(depth, parent, map)) return;

            string here = Join(parent, group.Name);

            Each(map, here, () =>
            {
                // A technology object is a PlcBlock, so it is filtered and counted like one -
                // its kind is TechnologicalInstanceDB and its language is whatever TIA gave it.
                foreach (TechnologicalInstanceDB found in group.TechnologicalObjects) _onBlock(found, here, map);
            });

            foreach (TechnologicalInstanceDBUserGroup child in Children(group.Groups, here, map))
                TechnologyObjects(child, here, map, depth + 1);
        }

        private void TagTables(PlcTagTableGroup group, string parent, ProjectMap map, int depth = 0)
        {
            if (group == null || Deep(depth, parent, map)) return;

            string here = Join(parent, group.Name);

            Each(map, here, () =>
            {
                foreach (PlcTagTable table in group.TagTables) _onTable(table, here, map);
            });

            foreach (PlcTagTableUserGroup child in Children(group.Groups, here, map)) TagTables(child, here, map, depth + 1);
        }

        private void Types(PlcTypeGroup group, string parent, ProjectMap map, int depth = 0)
        {
            if (group == null || Deep(depth, parent, map)) return;

            string here = Join(parent, group.Name);

            Each(map, here, () =>
            {
                foreach (PlcType type in group.Types) _onType(type, here, map);
            });

            foreach (PlcTypeUserGroup child in Children(group.Groups, here, map)) Types(child, here, map, depth + 1);
        }

        // ---- One object ---------------------------------------------------------------------

        private ProjectObject Of(PlcBlock block, string kind, string folder, ProjectMap map)
        {
            ProjectObject found = Described(block.Name, kind, folder, TitleOf(block, map));

            // TIA's own VERSION and FAMILY headers, which the core writes alongside its TITLE
            // and which a comparison can fall back on when the TITLE cannot be read.
            found.HeaderVersion = Native(() => block.HeaderVersion?.ToString());
            found.HeaderFamily = Native(() => block.HeaderFamily);

            return found;
        }

        private ProjectObject Of(PlcType type, string folder, ProjectMap map) =>
            Described(type.Name, CodingStyleNames.PlcStruct, folder, TitleOf(type, map));

        /// <summary>
        /// **Every block and every type is exported to read its title**, which is the whole
        /// difference between this adapter and V21's.
        ///
        /// V17-V20 has no `Title` property on `PlcBlock` or `PlcType`, and the untyped escape
        /// hatch refuses it outright - measured on the VM: *"'Title' is not supported by type
        /// 'Siemens.Engineering.SW.Blocks.OB'"*. The export is the only way left, and it is a
        /// way this project already trusts: the coding-style check reads interfaces out of
        /// exactly these files, in exactly this TIA version.
        ///
        /// **It is not free and it is not optional.** One export per object, so a PLC of four
        /// hundred is four hundred of them - which is why this reports progress and why the
        /// maintainer was asked before it went in. Without it a block would be identified only
        /// by its native `VERSION` and `FAMILY` headers and a PLC data type, which has neither
        /// in any TIA version, not at all - and 91 of the core's 249 sources are data types.
        ///
        /// **`ExportOptions.None`**: the file is read for one string and deleted a moment
        /// later, so there is no reason to ask TIA to write out every default value as well.
        /// </summary>
        private string TitleOf(PlcBlock block, ProjectMap map) =>
            Exported(file => block.Export(file, ExportOptions.None), block.Name, map);

        private string TitleOf(PlcType type, ProjectMap map) =>
            Exported(file => type.Export(file, ExportOptions.None), type.Name, map);

        private string Exported(Action<FileInfo> export, string name, ProjectMap map)
        {
            string folder = Scratch(map);
            if (folder == null) return null;

            // One file, reused: it is written, read and deleted before the next object, so a
            // run leaves nothing behind even if it is interrupted half way.
            string path = Path.Combine(folder, "read.xml");

            try
            {
                Fresh(path);
                export(new FileInfo(path));

                using (FileStream stream = File.OpenRead(path))
                {
                    string problem;
                    string title = SimaticMlTitle.Read(stream, out problem);

                    if (problem != null) Refused(map, name + ": " + problem);

                    return title;
                }
            }
            catch (Exception exception)
            {
                // Know-how protected, inconsistent, or something TIA will not write out. One
                // object without metadata, not the end of a walk over several thousand.
                Refused(map, name + ": " + exception.Message);
                return null;
            }
            finally
            {
                Fresh(path);
            }
        }

        /// <summary>
        /// Where the exports go: the project's own <c>repo\tmp\</c>, which exists for this.
        ///
        /// **Inside the project rather than in %TEMP%**, for the reason the coding-style check
        /// already records: what is in these files is somebody's source code, and the folder
        /// beside it is the one the project already gitignores.
        /// </summary>
        private string Scratch(ProjectMap map)
        {
            if (_scratch != null) return _scratch;

            string folder = RepoPaths.TmpFor(_project?.Path?.DirectoryName);

            if (folder == null)
            {
                Refused(map, "This project has no folder, so there is nowhere to export to.");
                return null;
            }

            try
            {
                Directory.CreateDirectory(folder);
                _scratch = folder;
            }
            catch (Exception exception)
            {
                Refused(map, "The scratch folder could not be created: " + exception.Message);
                return null;
            }

            return _scratch;
        }

        /// <summary>
        /// Says where the walk has got to, every tenth object. **Not every one**: each is a
        /// call across to the UI thread, and a number changing four hundred times is not a
        /// number anybody reads.
        /// </summary>
        private void Tick()
        {
            _done++;

            if (_progress == null || _done % ProgressEvery != 0) return;

            _progress("Read " + _done + " objects...");
        }

        private static void Fresh(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception)
            {
                // The next export overwrites it, and the folder goes at the end of the run.
            }
        }

        /// <summary>
        /// One object that would not come out. **Named for the first ten and counted after**:
        /// a PLC where every block is know-how protected would otherwise bury the map's other
        /// problems under four hundred identical lines.
        /// </summary>
        private void Refused(ProjectMap map, string problem)
        {
            _refused++;

            if (_refused <= ListedProblems) map.Problems.Add(problem);
        }

        /// <summary>What the run has to say for itself once the walk is over.</summary>
        private void Noted(ProjectMap map)
        {
            if (_refused > ListedProblems)
                map.Problems.Add("…and " + (_refused - ListedProblems) + " more that would not be read.");

            Discard();
        }

        /// <summary>
        /// The scratch folder goes when the run ends, whatever happened - it held the
        /// project's own source code for as long as one file at a time.
        /// </summary>
        private void Discard()
        {
            if (_scratch == null) return;

            try
            {
                Directory.Delete(_scratch, true);
            }
            catch (Exception)
            {
                // A folder that will not go is empty by now; the next run reuses it.
            }

            _scratch = null;
        }

        /// <summary>A native header field, or null when TIA will not answer for it.</summary>
        private static string Native(Func<string> read)
        {
            try
            {
                return read();
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// A tag table's metadata is not on the table: it is in the **comment of the constant
        /// named after it**, which is how the core's `.xlsx` carries it and what TIA imports
        /// that workbook into. `PlcTagTable` has a `Name` and nothing else - no `Title`, no
        /// `Comment` - so there is nowhere else it could be.
        /// </summary>
        private static ProjectObject Of(PlcTagTable table, string folder, ProjectMap map)
        {
            string title = null;

            try
            {
                PlcUserConstant marker = table.UserConstants
                    .FirstOrDefault(one => string.Equals(one.Name, table.Name, StringComparison.OrdinalIgnoreCase));

                if (marker != null) title = Title(marker.Comment);
            }
            catch (Exception exception)
            {
                map.Problems.Add(table.Name + ": its constants could not be read - " + exception.Message);
            }

            return Described(table.Name, CodingStyleNames.PlcTagTable, folder, title);
        }

        private static ProjectObject Described(string name, string kind, string folder, string title)
        {
            string problem;
            BlockMetadata metadata = BlockMetadata.Read(title, out problem);

            ProjectObject found = new ProjectObject
            {
                Name = name,
                Kind = kind,
                Folder = folder,
                Problem = problem
            };

            if (metadata == null) return found;

            found.Version = metadata.Number;
            found.Status = metadata.Status;
            found.DeprecatedBy = metadata.DeprecatedBy;
            found.Family = metadata.Family;
            found.Dependencies = metadata.Dependencies;

            return found;
        }

        /// <summary>
        /// **Most specific first**, or every technology object reports as an instance DB:
        /// `TechnologicalInstanceDB` derives from `InstanceDB`, which derives from `DataBlock`.
        /// The names are `CodingStyleNames`', so one vocabulary serves the check, the
        /// configuration and this.
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

        /// <summary>
        /// The first thing a multilingual text actually says. **Any language will do**: the
        /// metadata is JSON, the same in every one of them, and demanding a particular
        /// language would make the map depend on which the project was edited in.
        /// </summary>
        private static string Title(MultilingualText text)
        {
            if (text == null) return null;

            try
            {
                foreach (MultilingualTextItem item in text.Items)
                    if (!string.IsNullOrWhiteSpace(item.Text)) return item.Text;
            }
            catch (Exception)
            {
                // A title that will not come back is not a reason to lose the object: it
                // arrives with no metadata, which is what "not from the core" looks like.
            }

            return null;
        }

        // ---- Plumbing -----------------------------------------------------------------------

        private IEnumerable<PlcSoftware> Software()
        {
            List<PlcSoftware> found = new List<PlcSoftware>();
            HashSet<PlcSoftware> seen = new HashSet<PlcSoftware>();

            if (_project == null) return found;

            Devices(_project.Devices, found, seen);
            Devices(_project.UngroupedDevicesGroup?.Devices, found, seen);

            foreach (DeviceUserGroup group in _project.DeviceGroups) Group(group, found, seen);

            return found;
        }

        private void Group(DeviceUserGroup group, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (group == null) return;

            Devices(group.Devices, found, seen);

            foreach (DeviceUserGroup child in group.Groups) Group(child, found, seen);
        }

        private void Devices(DeviceComposition devices, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (devices == null) return;

            foreach (Device device in devices)
            {
                if (device == null) continue;

                foreach (DeviceItem item in device.DeviceItems) Items(item, found, seen);
            }
        }

        private void Items(DeviceItem item, List<PlcSoftware> found, HashSet<PlcSoftware> seen)
        {
            if (item == null) return;

            PlcSoftware plc = SoftwareOf(item);
            if (plc != null && seen.Add(plc)) found.Add(plc);

            foreach (DeviceItem child in item.DeviceItems) Items(child, found, seen);
        }

        /// <summary>
        /// The PLC a device item carries, or null. Asked of every module in a device, and most
        /// of them are not a CPU - a refusal there is an answer, not a failure.
        /// </summary>
        private static PlcSoftware SoftwareOf(DeviceItem item)
        {
            try
            {
                return item.GetService<SoftwareContainer>()?.Software as PlcSoftware;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private PlcSoftware Find(string plc) =>
            Software().FirstOrDefault(one => string.Equals(one.Name, plc, StringComparison.OrdinalIgnoreCase));

        /// <summary>
        /// Units and safety units alike. Only the S7-1500 family has them; on an S7-1200 the
        /// provider service is simply not there, which is a normal answer.
        /// </summary>
        private static IEnumerable<PlcUnitBase> UnitsOf(PlcSoftware plc)
        {
            PlcUnitSystemGroup group;

            try
            {
                group = plc.GetService<PlcUnitProvider>()?.UnitGroup;
            }
            catch (Exception)
            {
                group = null;
            }

            List<PlcUnitBase> units = new List<PlcUnitBase>();
            if (group == null) return units;

            foreach (PlcUnit unit in group.Units) units.Add(unit);
            foreach (PlcSafetyUnit unit in group.SafetyUnits) units.Add(unit);

            return units;
        }

        /// <summary>
        /// **The objects of one folder are guarded; the tree is not walked past a failure.**
        /// A folder that silently failed to read would drop out of the map and leave it
        /// looking complete, which is the one outcome a comparison must never be handed.
        /// </summary>
        private static void Each(ProjectMap map, string folder, Action read)
        {
            try
            {
                read();
            }
            catch (Exception exception)
            {
                map.Problems.Add((folder ?? "(root)") + ": " + exception.Message);
            }
        }

        /// <summary>
        /// A folder's own folders, read into a list before anything walks them.
        ///
        /// **Typed, not `dynamic`.** The four compositions share no base worth naming, so a
        /// dynamic call looked tempting - and would have traded a compile-time check plus a
        /// `Microsoft.CSharp` reference for four saved lines, in the one method that decides
        /// whether the walk reaches the rest of the tree.
        /// </summary>
        private static IEnumerable<T> Children<T>(IEnumerable<T> groups, string folder, ProjectMap map)
        {
            try
            {
                return new List<T>(groups);
            }
            catch (Exception exception)
            {
                map.Problems.Add((folder ?? "(root)") + ": its folders could not be read - " + exception.Message);
                return new List<T>();
            }
        }

        private static bool Deep(int depth, string folder, ProjectMap map)
        {
            if (depth < MaxDepth) return false;

            map.Problems.Add("Stopped at " + MaxDepth + " folders deep: " + folder);
            return true;
        }

        private static string Join(string parent, string name)
        {
            if (string.IsNullOrEmpty(name)) return parent ?? string.Empty;

            return string.IsNullOrEmpty(parent) ? name : parent + Places.Separator + name;
        }

        private static string Why(int running, TiaWanted wanted)
        {
            if (running == 0)
                return "No TIA Portal is running. Open the project in TIA Portal and start this again.";

            if (wanted.NamesProject)
                return string.Format(CultureInfo.CurrentCulture,
                    "No running TIA Portal has '{0}' open - it may have been closed, or reopened, " +
                    "since this window was started.", wanted.ProjectName);

            return "Several TIA Portals are running and nothing said which one this window belongs to.";
        }

        /// <summary>
        /// Every running TIA Portal, as parts rather than as a sentence: the project is what
        /// an operator recognises *and* what identifies the instance, and the process id is
        /// what tells two of the same project apart.
        ///
        /// **An instance that will not say its id is left out**, because there is nothing to
        /// attach to; one that will not say its project is listed anyway, since it is on the
        /// machine and a list that disagrees with the machine is worse than one with a gap.
        /// </summary>
        private static List<RunningPortal> Describe(IEnumerable<TiaPortalProcess> running)
        {
            List<RunningPortal> found = new List<RunningPortal>();

            foreach (TiaPortalProcess process in running)
            {
                int id;

                try
                {
                    id = process.Id;
                }
                catch (Exception)
                {
                    continue;
                }

                try
                {
                    found.Add(RunningPortal.With(
                        id, process.ProjectPath == null ? null : process.ProjectPath.FullName));
                }
                catch (Exception)
                {
                    // An instance that is starting up, or shutting down, answers nothing useful.
                    found.Add(RunningPortal.Unreadable(id));
                }
            }

            return found;
        }

        public void Dispose()
        {
            // **Deliberately empty of disposals.** Disposing the portal closes TIA Portal;
            // the references are dropped and the connection goes when this process ends.
            _project = null;
            _portal = null;
        }
    }
}
