using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    /// <summary>
    /// One naming rule. The same shape serves both catalogues: <c>objectRules</c>, whose
    /// entries name TIA objects, and <c>interfaceRules</c>, whose entries name what lives
    /// inside one.
    /// </summary>
    [DataContract]
    public class Rule
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "regex")]
        public string Regex { get; set; }

        [DataMember(Name = "descriptions")]
        public List<string> Descriptions { get; set; }

        /// <summary>
        /// What this rule expects to find inside the object it names, per interface
        /// section. Only an object rule carries one.
        ///
        /// **It hangs off the rule rather than off the type, and that is the whole
        /// design.** An FB whose name matches <c>object_container_for_sequences</c> must
        /// hold sequence variables in its Static; an FB whose name matches
        /// <c>function</c> must not. Both are FBs, so a list hanging off the type could
        /// never tell them apart - it is the rule that matched which says what belongs
        /// inside.
        ///
        /// Optional. Absent means the interface is not checked.
        /// </summary>
        [DataMember(Name = "interface")]
        public List<InterfaceSection> Interface { get; set; }
    }
}
