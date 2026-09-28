using System;
using System.Collections.Generic;
using System.Linq;

namespace Core.Imports
{
    /// <summary>
    /// Where an import goes: the folder that was right-clicked, as the PLC, the software unit,
    /// the tree and the folders below its root - and how that travels on the command line from
    /// the Add-In to the satellite.
    ///
    /// **One type for both ends**, the same reason <see cref="Places"/> exists: the Add-In writes
    /// the arguments and the satellite reads them, and two spellings of one layout is how the two
    /// stop agreeing without anything failing to compile.
    ///
    /// **The tree is ours to name and the folders are TIA's.** A tree's root is called what TIA's
    /// interface language calls it - "Program blocks", "Programmbausteine" - so it travels as a
    /// token of this framework's own; the folders under it are the names the engineer typed,
    /// which is what the satellite looks for, one level at a time.
    ///
    /// **Each folder is an argument of its own, never a joined path**: TIA takes any character in
    /// a folder name - measured on the VM on 2026-09-25, while provoking a refusal for the
    /// hierarchy - so a separator could be part of a name, and joining would make two folders
    /// out of one.
    /// </summary>
    public sealed class ImportPlace
    {
        private const string BlocksToken = "blocks";
        private const string TypesToken = "types";
        private const string TagTablesToken = "tags";
        private const string TechnologyToken = "technology";

        /// <summary>Project, PLC, unit and tree come first; any folders follow.</summary>
        private const int FixedArguments = 4;

        private ImportPlace(string project, string plc, string unit, ObjectTree tree, IReadOnlyList<string> folders)
        {
            Project = project;
            Plc = plc;
            Unit = unit;
            Tree = tree;
            Folders = folders;
        }

        /// <summary>
        /// The project file the Add-In was in, which is what tells the satellite which TIA Portal
        /// to attach to. Null for a project that was never saved.
        /// </summary>
        public string Project { get; }

        public string Plc { get; }

        /// <summary>The software unit, or null for the PLC's own program.</summary>
        public string Unit { get; }

        public ObjectTree Tree { get; }

        /// <summary>The folders below the tree's root, outermost first. Empty for the root itself.</summary>
        public IReadOnlyList<string> Folders { get; }

        public static ImportPlace Of(string project, string plc, string unit, ObjectTree tree, IEnumerable<string> folders)
        {
            if (string.IsNullOrWhiteSpace(plc)) throw new ArgumentException("A place names its PLC.", nameof(plc));

            return new ImportPlace(
                string.IsNullOrWhiteSpace(project) ? null : project.Trim(),
                plc.Trim(),
                Places.UnitOrNull(unit),
                tree,
                (folders ?? Enumerable.Empty<string>()).Where(f => !string.IsNullOrEmpty(f)).ToList());
        }

        /// <summary>
        /// The command line: <c>&lt;project&gt; &lt;plc&gt; &lt;unit|*&gt; &lt;tree&gt; [folder ...]</c>.
        /// **Every position is always there**: a project never saved sends an empty first argument,
        /// the core updater's rule for its own third one, so nothing after it shifts.
        /// </summary>
        public string[] Arguments()
        {
            List<string> arguments = new List<string>
            {
                Project ?? string.Empty,
                Plc,
                Places.UnitOrGeneral(Unit),
                TokenOf(Tree)
            };

            arguments.AddRange(Folders);
            return arguments.ToArray();
        }

        /// <summary>
        /// A place read back off the command line. **Null with no problem** means there were no
        /// arguments at all - a window started by hand, which is not a mistake - and null with a
        /// problem means arguments that do not say a place.
        /// </summary>
        public static ImportPlace Parse(IReadOnlyList<string> arguments, out string problem)
        {
            problem = null;

            if (arguments == null || arguments.Count == 0) return null;

            if (arguments.Count < FixedArguments)
            {
                problem = "The command line names no folder to import into: it needs the project, the PLC, " +
                          "the software unit and the kind of folder, and has " + arguments.Count + " argument(s).";
                return null;
            }

            if (string.IsNullOrWhiteSpace(arguments[1]))
            {
                problem = "The command line names no PLC to import into.";
                return null;
            }

            ObjectTree tree;

            if (!TryTree(arguments[3], out tree))
            {
                problem = "'" + arguments[3] + "' is not a kind of folder this imports into - " +
                          "it takes " + string.Join(", ", BlocksToken, TypesToken, TagTablesToken, TechnologyToken) + ".";
                return null;
            }

            return Of(arguments[0], arguments[1], arguments[2], tree, arguments.Skip(FixedArguments));
        }

        public static string TokenOf(ObjectTree tree)
        {
            switch (tree)
            {
                case ObjectTree.Types: return TypesToken;
                case ObjectTree.TagTables: return TagTablesToken;
                case ObjectTree.TechnologyObjects: return TechnologyToken;
                default: return BlocksToken;
            }
        }

        public static bool TryTree(string token, out ObjectTree tree)
        {
            switch ((token ?? string.Empty).Trim())
            {
                case BlocksToken: tree = ObjectTree.Blocks; return true;
                case TypesToken: tree = ObjectTree.Types; return true;
                case TagTablesToken: tree = ObjectTree.TagTables; return true;
                case TechnologyToken: tree = ObjectTree.TechnologyObjects; return true;
                default: tree = ObjectTree.Blocks; return false;
            }
        }

        /// <summary>
        /// The tree as a person reads it, in English. **Only ever shown**, never looked for:
        /// TIA's own name for it follows the interface language, and the satellite finds the
        /// tree through the object model rather than by name.
        /// </summary>
        public string TreeName
        {
            get
            {
                switch (Tree)
                {
                    case ObjectTree.Types: return "PLC data types";
                    case ObjectTree.TagTables: return "PLC tags";
                    case ObjectTree.TechnologyObjects: return "Technology objects";
                    default: return "Program blocks";
                }
            }
        }

        /// <summary>The folder as a person reads it: the tree, then each folder below it.</summary>
        public string FolderPath =>
            Folders.Count == 0 ? TreeName : TreeName + " / " + string.Join(" / ", Folders);

        /// <summary><c>KF1022 · * · Program blocks / 03-ALL / adt</c>.</summary>
        public override string ToString() => Plc + " · " + Places.UnitOrGeneral(Unit) + " · " + FolderPath;
    }
}
