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

    /// <summary>
    /// What the config editor needs, which is only the project.
    ///
    /// Its own type rather than a reused HandoffPayload: sharing one would send the editor
    /// a null <c>plc</c> and a null <c>dataBlocks</c> on every launch, and the day a reader
    /// starts trusting a field it was never given is the day this gets hard to follow.
    /// Same partial-trust rule as above - public, and top level.
    /// </summary>
    [DataContract]
    public sealed class ConfigEditorPayload
    {
        [DataMember(Name = "projectDirectory")]
        public string ProjectDirectory { get; set; }

        [DataMember(Name = "projectName")]
        public string ProjectName { get; set; }
    }
}
