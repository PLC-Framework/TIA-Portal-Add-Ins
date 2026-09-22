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

        [DataMember(Name = "folder")]
        public string Folder { get; set; }

        [DataMember(Name = "dependencyFile")]
        public string DependencyFile { get; set; }
        
        [DataMember(Name = "token")]
        public string Token { get; set; }
    }
}
