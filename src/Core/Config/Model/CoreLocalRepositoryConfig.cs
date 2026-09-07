using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class CoreLocalRepositoryConfig
    {
        [DataMember(Name = "repository")]
        public string Repository { get; set; }

        [DataMember(Name = "folder")]
        public string Folder { get; set; }

        [DataMember(Name = "dependencyFile")]
        public string DependencyFile { get; set; }
    }
}
