using System.Collections.Generic;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Document
{
    /// <summary>
    /// The <c>hierarchy</c> section: the folder trees the Add-In creates inside a PLC.
    ///
    /// **Seven trees, not four.** The four top-level concerns, plus three more inside
    /// <c>softwareUnits</c> — which is optional as a whole, exists only on an S7-1500, and
    /// deliberately has no <c>technologyObjects</c>, because a software unit exposes blocks,
    /// tag tables and types only.
    /// </summary>
    public sealed class HierarchyEditor
    {
        public const string Path = "projectConfig.hierarchy";
        public const string Units = "softwareUnits";

        /// <summary>The four lists at the top level.</summary>
        public static readonly IReadOnlyList<Concern> Concerns = new[]
        {
            new Concern("blocks", "Blocks"),
            new Concern("technologyObjects", "Technology objects"),
            new Concern("tagTables", "Tag tables"),
            new Concern("types", "Types")
        };

        /// <summary>What a software unit exposes. No technologyObjects: it has none.</summary>
        public static readonly IReadOnlyList<Concern> UnitConcerns = new[]
        {
            new Concern("blocks", "Blocks"),
            new Concern("tagTables", "Tag tables"),
            new Concern("types", "Types")
        };

        private readonly ConfigDocument _document;

        public HierarchyEditor(ConfigDocument document)
        {
            _document = document;
        }

        /// <summary>
        /// One of the four top-level lists, created empty if the key is absent.
        ///
        /// Created rather than left missing because the contract requires the key: `[]` says
        /// "this concern has no folders", a missing key says nothing, and only one of those
        /// is a decision somebody made.
        /// </summary>
        public JArray Groups(string key) => _document.ArrayAt(Path + "." + key, true);

        /// <summary>True when the optional software-unit section is present.</summary>
        public bool HasUnits => _document.Has(Path + "." + Units);

        public JArray UnitGroups(string key) =>
            _document.ArrayAt(Path + "." + Units + "." + key, true);

        /// <summary>
        /// Adds the section with all three of its lists. Half a section is worse than none:
        /// the contract requires every list once the section exists.
        /// </summary>
        public void AddUnits()
        {
            foreach (Concern concern in UnitConcerns) UnitGroups(concern.Key);
        }

        /// <summary>
        /// Removes the section entirely — folders and all. Only ever from an explicit
        /// choice, since it is the operator throwing away a tree they built.
        /// </summary>
        public void RemoveUnits()
        {
            JObject hierarchy = _document.ObjectAt(Path);
            hierarchy?.Remove(Units);
        }
    }

    /// <summary>One list of groups, and what the window calls it.</summary>
    public sealed class Concern
    {
        public Concern(string key, string title)
        {
            Key = key;
            Title = title;
        }

        public string Key { get; }

        public string Title { get; }

        public override string ToString() => Title;
    }
}
