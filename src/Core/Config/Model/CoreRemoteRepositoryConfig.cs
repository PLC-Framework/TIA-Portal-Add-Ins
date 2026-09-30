using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class CoreRemoteRepositoryConfig
    {
        /// <summary>
        /// Which host's API to speak: <c>github</c> today.
        ///
        /// **Absent means <c>github</c>**, which is what every configuration written before this
        /// key existed says by omission - and those files must go on working. So the reading is
        /// not "nobody decided" but "the only provider there was".
        ///
        /// **It is here rather than in <c>coreSource</c>**, which answers a different question:
        /// a folder on this machine or a repository over a wire. Widening that closed set to
        /// <c>github | gitlab</c> would have broken every file already saying <c>remote</c>, and
        /// mixed where a core lives with who hosts it.
        /// </summary>
        [DataMember(Name = "provider")]
        public string Provider { get; set; }

        [DataMember(Name = "apiUrl")]
        public string ApiUrl { get; set; }

        [DataMember(Name = "owner")]
        public string Owner { get; set; }

        [DataMember(Name = "repository")]
        public string Repository { get; set; }

        [DataMember(Name = "branch")]
        public string Branch { get; set; }

        /// <summary>
        /// Path inside the repository down to the core. **Read <see cref="Folder"/> rather than
        /// this**, which falls back to the key's former name - see
        /// <see cref="CoreLocalRepositoryConfig.LegacyFolder"/>.
        /// </summary>
        [DataMember(Name = "coreFolder")]
        public string CoreFolder { get; set; }

        /// <summary>What <c>coreFolder</c> was called until 2026-09-30, still read.</summary>
        [DataMember(Name = "folder")]
        public string LegacyFolder { get; set; }

        /// <summary>The core's path in force: <c>coreFolder</c>, or the former key without it.</summary>
        public string Folder => CoreFolder ?? LegacyFolder;

        /// <summary>Which key <see cref="Folder"/> came from, for an issue's path.</summary>
        public string FolderKey => CoreFolder != null || LegacyFolder == null ? "coreFolder" : "folder";

        /// <summary>
        /// Path inside the repository down to the templates, as
        /// <see cref="CoreLocalRepositoryConfig.TemplateFolder"/> is on the local side.
        /// Optional: blank means this repository offers none.
        /// </summary>
        [DataMember(Name = "templateFolder")]
        public string TemplateFolder { get; set; }

        [DataMember(Name = "dependencyFile")]
        public string DependencyFile { get; set; }

        [DataMember(Name = "token")]
        public string Token { get; set; }
    }
}
