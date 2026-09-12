using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    /// <summary>
    /// One section of a block's interface, and the naming rules its members accept.
    ///
    /// Structurally identical to <see cref="PlcTypeObject"/> - a type and a list of rule
    /// ids - and deliberately not the same class. The two carry different vocabularies:
    /// a <see cref="PlcTypeObject.Type"/> names a TIA object kind, <see cref="Type"/> here
    /// names a section inside one. Sharing the class would let a validator accept "OB" as
    /// a section, and would leave the editor with no way to know which drop-down to offer.
    /// </summary>
    [DataContract]
    public class InterfaceSection
    {
        [DataMember(Name = "type")]
        public string Type { get; set; }

        [DataMember(Name = "implements")]
        public List<string> Implements { get; set; }
    }
}
