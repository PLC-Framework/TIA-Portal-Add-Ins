using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class CoreLocalRepositoryConfig
    {
        [DataMember(Name = "repository")]
        public string Repository { get; set; }

        /// <summary>
        /// Path inside the repository down to the core, <c>plc/s7-1x00/core</c>, with forward
        /// slashes. **Read <see cref="Folder"/> rather than this**, which falls back to the
        /// key's former name.
        /// </summary>
        [DataMember(Name = "coreFolder")]
        public string CoreFolder { get; set; }

        /// <summary>
        /// What <c>coreFolder</c> was called until 2026-09-30, when <c>templateFolder</c> arrived
        /// beside it and a bare "folder" stopped saying which of the two it meant. Read so that
        /// a configuration already living in a TIA project keeps working; the editor renames
        /// it the first time it opens the file, as it did <c>rules</c>.
        /// </summary>
        [DataMember(Name = "folder")]
        public string LegacyFolder { get; set; }

        /// <summary>
        /// The core's path in force: <c>coreFolder</c> when the file has it, the former key
        /// otherwise. The validator is what reports a file carrying both.
        /// </summary>
        public string Folder => CoreFolder ?? LegacyFolder;

        /// <summary>Which key <see cref="Folder"/> came from, for an issue's path.</summary>
        public string FolderKey => CoreFolder != null || LegacyFolder == null ? "coreFolder" : "folder";

        /// <summary>
        /// Path inside the repository down to the templates a block can be generated from -
        /// <c>plc/s7-1x00/template</c> - written like <see cref="CoreFolder"/>. **Optional:
        /// blank means this repository offers none**, which is every configuration written
        /// before the key existed.
        ///
        /// **Beside the core rather than a section of its own**, because the templates live in
        /// the core's repository and call the core's blocks: the same <c>coreSource</c> that
        /// picks where the core comes from picks where they come from, and a project that names
        /// no core has none.
        /// </summary>
        [DataMember(Name = "templateFolder")]
        public string TemplateFolder { get; set; }

        [DataMember(Name = "dependencyFile")]
        public string DependencyFile { get; set; }
    }
}
