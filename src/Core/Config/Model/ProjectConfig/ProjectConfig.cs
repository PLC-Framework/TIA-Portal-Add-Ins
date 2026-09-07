using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class ProjectConfig
    {
        [DataMember(Name = "hierarchy")]
        public Hierarchy Hierarchy { get; set; }

        [DataMember(Name = "codingStyle")]
        public CodingStyle CodingStyle { get; set; }
    }
}