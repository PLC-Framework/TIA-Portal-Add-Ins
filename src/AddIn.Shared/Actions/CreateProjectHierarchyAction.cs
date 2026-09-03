using System.Collections.Generic;

using AddIn.Shared.Adapters;
using Core.Config;

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
        public const string IconPath = "AddIn/folder-hierarchy.ico";

        public static void Execute(ITiaNotifier notifier, Hierarchy hierarchy, HierarchyTargets targets)
        {
            if (notifier == null) return;

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

            int groups = Ensure(targets.Blocks, hierarchy.Blocks)
                       + Ensure(targets.TechnologyObjects, hierarchy.TechnologyObjects)
                       + Ensure(targets.TagTables, hierarchy.TagTables)
                       + Ensure(targets.Types, hierarchy.Types);

            if (groups == 0)
            {
                notifier.Warning(Title, $"'{ConfigPaths.File}' declares no groups under 'projectConfig.hierarchy'.");
                return;
            }

            notifier.Success(Title, $"Hierarchy ready: {groups} group(s).");
        }

        /// <summary>
        /// Walks one branch depth-first, creating what is missing. Returns how many groups
        /// ended up in place. A null target or a null list simply contributes nothing:
        /// a project that declares no tag-table groups is not an error.
        /// </summary>
        public static int Ensure(IGroupNode parent, IReadOnlyList<Group> desired)
        {
            if (parent == null || desired == null) return 0;

            int count = 0;

            foreach (Group group in desired)
            {
                if (string.IsNullOrWhiteSpace(group?.Name)) continue;

                IGroupNode child = parent.FindOrCreate(group.Name);
                if (child == null) continue;

                count++;
                count += Ensure(child, group.Groups);
            }

            return count;
        }
    }
}
