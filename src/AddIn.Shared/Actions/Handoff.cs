using System.Runtime.Serialization;

namespace AddIn.Shared.Actions
{
    /// <summary>
    /// The CPU half of the satellite handoff.
    ///
    /// Public, and top level, on purpose. TIA Portal runs an Add-In in <b>partial
    /// trust</b>, and DataContractJsonSerializer refuses to serialize a type that is not
    /// visible there: an internal type throws SecurityException from WriteObject with
    /// "not serializable in partial trust because it is not public". Nesting these inside
    /// the action would satisfy the rule but leaves a '+' in the type name for no gain.
    /// </summary>
    [DataContract]
    public sealed class HandoffPlc
    {
        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "addresses")]
        public string[] Addresses { get; set; }
    }

    /// <summary>What the Add-In writes into the satellite's standard input.</summary>
    [DataContract]
    public sealed class HandoffPayload
    {
        [DataMember(Name = "projectDirectory")]
        public string ProjectDirectory { get; set; }

        [DataMember(Name = "plc")]
        public HandoffPlc Plc { get; set; }

        [DataMember(Name = "dataBlocks")]
        public string[] DataBlocks { get; set; }
    }
}
