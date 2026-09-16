using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

using Core;
using Core.Config;
using Core.Repo;

using Siemens.Engineering;
using Siemens.Engineering.HW;
using Siemens.Engineering.HW.Features;
using Siemens.Engineering.SW;
using Siemens.Engineering.SW.Blocks;
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

        /// <summary>How many objects pass between two updates of the text the window shows.</summary>
        private const int ProgressEvery = 25;

        private Action<string> _progress;
        private int _done;

        private TiaPortal _portal;
        private Project _project;

        /// <summary>What this run was asked to keep. Never null while a walk is running.</summary>
        private MapFilter _filter = MapFilter.Everything;

        /// <summary>
        /// What the current walk does with each object it reaches.
        ///
        /// **One tree walk, two jobs.** The survey counts and the map reads, and the only thing
        /// that must not drift between them is which folders were reached - so the walk is
        /// written once and what happens at the leaves is handed in.
        /// </summary>
        private Action<PlcBlock, string, ProjectMap> _onBlock;
        private Action<PlcTagTable, string, ProjectMap> _onTable;
        private Action<PlcType, string, ProjectMap> _onType;

        private Dictionary<string, int> _kinds;
        private Dictionary<string, int> _languages;
        private int _total;

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

        public ProjectSurvey Survey(string plc, string unit)
        {
            _kinds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _languages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            _total = 0;

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

            return ProjectSurvey.Of(_kinds, _languages, _total);
        }

        public ProjectMap Map(string plc, string unit, MapFilter filter, Action<string> progress)
        {
            _filter = filter ?? MapFilter.Everything;
            _progress = progress;
            _done = 0;

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
        /// **The filter is applied here, before anything is read.** The kind and the language
        /// are typed properties that cost nothing; in V17-V20 the line below them exports the
        /// object. Filtering after the read would keep the cost and throw the answer away.
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
            Count(Kind(block), Language(block));

        private void Counted(PlcTagTable table, string folder, ProjectMap notes) =>
            Count(CodingStyleNames.PlcTagTable, null);

        private void Counted(PlcType type, string folder, ProjectMap notes) =>
            Count(CodingStyleNames.PlcStruct, null);

        private void Count(string kind, string language)
        {
            _total++;

            Add(_kinds, kind);

            // Only what has one. A PLC data type and a tag table are not counted here at all,
            // which is the same rule the filter reads by - null passes the language half.
            if (language != null) Add(_languages, language);
        }

        private static void Add(IDictionary<string, int> counted, string name)
        {
            if (string.IsNullOrEmpty(name)) return;

            int found;

            counted[name] = counted.TryGetValue(name, out found) ? found + 1 : 1;
        }

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
        /// **V21 has `Title` as a typed property; V20 does not have it at all.** That is a real
        /// divergence between the two object models, found by compiling this file against both,
        /// and it is why these two adapters are not identical the way the others are.
        /// </summary>
        private string TitleOf(PlcBlock block, ProjectMap map) => Title(block.Title);

        private string TitleOf(PlcType type, ProjectMap map) => Title(type.Title);

        /// <summary>
        /// Says where the walk has got to, every twenty-fifth object. **Not every one**: each
        /// is a call across to the UI thread, and a number changing thousands of times is not
        /// a number anybody reads. Less often than V20's, which exports each object and is
        /// therefore slow enough that the count is the only sign of life.
        /// </summary>
        private void Tick()
        {
            _done++;

            if (_progress == null || _done % ProgressEvery != 0) return;

            _progress("Read " + _done + " objects...");
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

            return string.IsNullOrEmpty(parent) ? name : parent + "/" + name;
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
