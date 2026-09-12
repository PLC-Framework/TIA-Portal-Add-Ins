using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class PlcTypeObject
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "implements")]
        public List<string> Implements { get; set; }

        /// <summary>
        /// Naming rules for what lives *inside* this object, per interface section.
        ///
        /// It is nested here rather than declared as its own top-level section because the
        /// binding is what carries the meaning: a static of an FB and a temp of an FC are
        /// both "variables" and may well answer to different rules. A flat
        /// <c>variables</c> list could only ever say "every variable everywhere".
        ///
        /// Optional. Absent means the interface is not checked, which is the state every
        /// configuration written before this existed is in.
        /// </summary>
        [DataMember(Name = "interface")]
        public List<InterfaceSection> Interface { get; set; }
    }
}
