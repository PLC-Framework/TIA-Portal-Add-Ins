using System.Collections.Generic;
using System.Linq;
using System.Text;

using AddIn.Shared.Adapters;
using Core.Config;
using Core.Config.Validation;
using Core.Logging;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// Creates the group hierarchy declared under projectConfig.hierarchy inside a PLC.
    ///
    /// Existing groups are kept, so running it twice is harmless: it only fills in what
    /// is missing. Nothing here touches Siemens; the four IGroupNode roots come from the
    /// version-specific Add-In, which is the only side that knows about TIA.
    /// </summary>
    public static class CreateProjectHierarchyAction
    {
        public const string Title = "Create project hierarchy";
        public const string IconPath = "AddIn/create-folder-hierarchy.ico";

        /// <summary>How many problems a message names before it only counts the rest.</summary>
        private const int ListedProblems = 10;

        /// <param name="log">
        /// The click's log, which gets every validation problem where the message names ten, and
        /// every folder that could not be found or created - which the message does not name at
        /// all: it counts the folders in place, and one missing is only a count one too low.
        /// </param>
        public static void Execute(ITiaNotifier notifier, Hierarchy hierarchy, HierarchyTargets targets, Log log = null)
        {
            if (notifier == null) return;

            Log said = log ?? Log.Nothing();

            if (targets == null)
            {
                notifier.Error(Title, "Select a PLC device.");
                return;
            }

            if (hierarchy == null)
            {
                notifier.Warning(Title, $"'{ConfigPaths.File}' has no 'projectConfig.hierarchy' section.");
                return;
            }

            // **Validated here, after loading, because hand-editing bypasses the editor** - the
            // rule this framework states and, until 2026-09-22, did not keep on this side: only
            // the coding-style check validated its own concern, and this one walked straight
            // into TIA with whatever the file said.
            //
            // Nothing was ever corrupted by that, which is why it went unnoticed: `Ensure` steps
            // over a blank name and `FindOrCreate` answers the folder a duplicate names. What it
            // cost was **silence** - a file with a nameless folder, or two siblings spelled the
            // same, came back "Hierarchy ready" with a count that was one too many, and the
            // defect the editor would have marked in red was never mentioned again.
            //
            // Only this concern's validator, and the path prefix the whole document would give,
            // so each issue points into the file as written rather than at a section in the air.
            ValidationResult validation = HierarchyValidator.Validate(hierarchy, "projectConfig.hierarchy");
            if (!validation.IsValid)
            {
                foreach (ValidationIssue issue in validation.Issues) said.Warn(ConfigPaths.File + " - " + issue);

                notifier.Error(Title, Invalid(validation));
                return;
            }

            // Named after the keys of config.json rather than after TIA's folders, whose names
            // follow the interface language: a line points at the file somebody would open.
            int groups = Ensure(targets.Blocks, hierarchy.Blocks, "blocks", said)
                       + Ensure(targets.TechnologyObjects, hierarchy.TechnologyObjects, "technologyObjects", said)
                       + Ensure(targets.TagTables, hierarchy.TagTables, "tagTables", said)
                       + Ensure(targets.Types, hierarchy.Types, "types", said);

            int unitCount = targets.SoftwareUnits?.Count ?? 0;
            int unitGroups = EnsureSoftwareUnits(hierarchy.SoftwareUnits, targets.SoftwareUnits, said);

            if (groups + unitGroups == 0)
            {
                notifier.Warning(Title, $"'{ConfigPaths.File}' declares no groups under 'projectConfig.hierarchy'.");
                return;
            }

            string message = $"Hierarchy ready: {groups} group(s).";

            if (unitCount > 0)
                message += $"\nSoftware units: {unitGroups} group(s) across {unitCount} unit(s).";

            notifier.Success(Title, message);
        }

        /// <summary>
        /// Applies the same declared structure inside every existing software unit.
        ///
        /// Units are never created here: they are named by the user according to the
        /// plant's architecture. An S7-1200 has none, so the list is empty and this
        /// contributes nothing — which is not an error.
        /// </summary>
        private static int EnsureSoftwareUnits(
            SoftwareUnitHierarchy desired,
            IReadOnlyList<HierarchySoftwareUnitTargets> units,
            Log said)
        {
            if (desired == null || units == null) return 0;

            int count = 0;

            foreach (HierarchySoftwareUnitTargets unit in units)
            {
                if (unit == null) continue;

                string where = "softwareUnits[" + unit.Name + "].";

                count += Ensure(unit.Blocks, desired.Blocks, where + "blocks", said);
                count += Ensure(unit.TagTables, desired.TagTables, where + "tagTables", said);
                count += Ensure(unit.Types, desired.Types, where + "types", said);
            }

            return count;
        }

        /// <summary>
        /// Walks one branch depth-first, creating what is missing. Returns how many groups
        /// ended up in place. A null target or a null list simply contributes nothing:
        /// a project that declares no tag-table groups is not an error.
        /// </summary>
        public static int Ensure(IGroupNode parent, IReadOnlyList<Group> desired) =>
            Ensure(parent, desired, string.Empty, Log.Nothing());

        private static int Ensure(IGroupNode parent, IReadOnlyList<Group> desired, string where, Log said)
        {
            if (parent == null || desired == null) return 0;

            int count = 0;

            foreach (Group group in desired)
            {
                if (string.IsNullOrWhiteSpace(group?.Name)) continue;

                string here = where + "/" + group.Name;

                IGroupNode child = parent.FindOrCreate(group.Name);
                if (child == null)
                {
                    // The adapter answers null when TIA refused, and keeps the reason to itself;
                    // nothing under this folder can be made either. Until the port can say why,
                    // the log at least says which.
                    said.Warn("the folder " + here + " could not be found or created, nor anything under it");
                    continue;
                }

                count++;
                count += Ensure(child, group.Groups, here, said);
            }

            return count;
        }

        /// <summary>
        /// What is wrong with the hierarchy, and that nothing was created because of it.
        ///
        /// **It says nothing was done, and means it**: the walk is refused whole rather than
        /// run as far as the first bad name, because a folder tree half created is one nobody
        /// can tell from a tree somebody built by hand.
        /// </summary>
        private static string Invalid(ValidationResult validation)
        {
            StringBuilder text = new StringBuilder();

            text.Append($"\n\n'{ConfigPaths.File}' has problems in its hierarchy, so no group was created:\n\n");

            foreach (ValidationIssue issue in validation.Issues.Take(ListedProblems))
                text.Append("  ").Append(issue).Append('\n');

            int rest = validation.Issues.Count - ListedProblems;
            if (rest > 0) text.Append($"  ...and {rest} more.\n");

            text.Append("\nOpen Config. Editor to fix them.");
            return text.ToString();
        }
    }
}
