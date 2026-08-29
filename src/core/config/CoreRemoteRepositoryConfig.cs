using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class CoreRemoteRepositoryConfig
    {
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
