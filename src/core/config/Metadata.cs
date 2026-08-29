using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class Metadata
    {
        [DataMember(Name = "author")]
        public string Author { get; set; }

        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "description")]
        public string Description { get; set; }

        [DataMember(Name = "coreSource")]
        public string CoreSource { get; set; }
    }
}
