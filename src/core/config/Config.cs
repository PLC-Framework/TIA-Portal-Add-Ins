using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class Config
    {
        [DataMember(Name = "metadata")]
        public Metadata Metadata { get; set; }

        [DataMember(Name = "coreRemoteRepositoryConfig")]
        public CoreRemoteRepositoryConfig CoreRemoteRepositoryConfig { get; set; }

        [DataMember(Name = "coreLocalRepositoryConfig")]
        public CoreLocalRepositoryConfig CoreLocalRepositoryConfig { get; set; }

        [DataMember(Name = "projectConfig")]
        public ProjectConfig ProjectConfig { get; set; }
    }
}
