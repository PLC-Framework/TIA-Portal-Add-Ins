using System;
using System.Collections.Generic;

namespace Core.Config.Validation
{
    /// <summary>
    /// The <c>hierarchy</c> section: the folder tree the Add-In creates in a PLC.
    /// </summary>
    public static class HierarchyValidator
    {
        /// <summary>
        /// How deep the group tree may nest before the walk gives up.
        ///
        /// Not a style rule - a guard. This code runs **inside TIA Portal's process**, and
        /// a StackOverflowException cannot be caught in .NET: it takes the host down with
        /// it. A hand-written folder tree is three or four deep, so thirty-two leaves room
        /// for anything real while a file that recurses without end reports a problem
        /// instead of killing TIA.
        /// </summary>
        public const int MaxDepth = 32;

        public static ValidationResult Validate(Hierarchy hierarchy, string path = "hierarchy")
        {
            Issues issues = new Issues();
            Collect(hierarchy, path, issues);
            return new ValidationResult(issues.All);
        }

        internal static void Collect(Hierarchy hierarchy, string path, Issues issues)
        {
            if (!issues.RequiredObject(path, hierarchy)) return;

            Groups(hierarchy.Blocks, Issues.Field(path, "blocks"), issues);
            Groups(hierarchy.TechnologyObjects, Issues.Field(path, "technologyObjects"), issues);
            Groups(hierarchy.TagTables, Issues.Field(path, "tagTables"), issues);
            Groups(hierarchy.Types, Issues.Field(path, "types"), issues);

            // Optional as a whole - only the S7-1500 has software units, and on an S7-1200
            // the section is ignored rather than missed. But once it is there it is a
            // statement of intent, and half of it filled in is a mistake.
            if (hierarchy.SoftwareUnits == null) return;

            string units = Issues.Field(path, "softwareUnits");
            Groups(hierarchy.SoftwareUnits.Blocks, Issues.Field(units, "blocks"), issues);
            Groups(hierarchy.SoftwareUnits.TagTables, Issues.Field(units, "tagTables"), issues);
            Groups(hierarchy.SoftwareUnits.Types, Issues.Field(units, "types"), issues);

            // There is no technologyObjects here, and that is not an omission: a software
            // unit exposes blocks, tag tables and types only.
        }

        private static void Groups(IReadOnlyList<Group> groups, string path, Issues issues)
        {
            if (!issues.RequiredList(path, groups)) return;

            Level(groups, path, issues, 1);
        }

        /// <summary>
        /// One level of siblings, then each of their children. Depth-first, so the issues
        /// come out in document order.
        /// </summary>
        private static void Level(IReadOnlyList<Group> groups, string path, Issues issues, int depth)
        {
            if (depth > MaxDepth)
            {
                issues.Add(path, "Nested more than " + MaxDepth + " levels deep. Not walked any further.");
                return;
            }

            // Case-insensitive, deliberately. Two folders called "core" and "Core" are the
            // same folder to anybody reading the tree, so the pair is reported rather than
            // quietly created twice.
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < groups.Count; i++)
            {
                Group group = groups[i];
                string here = Issues.At(path, i);

                if (!issues.RequiredObject(here, group)) continue;

                string name = Issues.Field(here, "name");
                if (issues.Required(name, group.Name) && !seen.Add(group.Name.Trim()))
                {
                    issues.Add(name,
                        "'" + group.Name.Trim() + "' appears twice among the same siblings. " +
                        "The same name in a different branch is fine.");
                }

                // Absent children are the normal case: most folders are leaves.
                if (group.Groups != null)
                    Level(group.Groups, Issues.Field(here, "groups"), issues, depth + 1);
            }
        }
    }
}
