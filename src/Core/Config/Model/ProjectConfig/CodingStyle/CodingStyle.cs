using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Config
{
    [DataContract]
    public class CodingStyle
    {
        /// <summary>
        /// Rules for the names of TIA objects. Each may carry an <c>interface</c> saying
        /// what belongs inside the object it names.
        /// </summary>
        [DataMember(Name = "objectRules")]
        public List<Rule> ObjectRules { get; set; }

        /// <summary>
        /// Rules for the names of what lives inside an object: interface members, tags,
        /// constants. Optional, so that a configuration written before the catalogue was
        /// split stays valid.
        /// </summary>
        [DataMember(Name = "interfaceRules")]
        public List<Rule> InterfaceRules { get; set; }

        /// <summary>
        /// What <c>objectRules</c> used to be called, read so that a configuration already
        /// living in a TIA project keeps working.
        ///
        /// The model carries both keys because a DTO's job here is to report what the file
        /// says, not to decide: <see cref="Catalogue"/> is what picks, and the validator is
        /// what reports a file carrying both. The editor writes the current name, so a
        /// document migrates the first time somebody saves it.
        /// </summary>
        [DataMember(Name = "rules")]
        public List<Rule> LegacyRules { get; set; }

        /// <summary>
        /// The object-rule catalogue actually in force, under the current key when it is
        /// there and the legacy one otherwise.
        /// </summary>
        public List<Rule> Catalogue => ObjectRules ?? LegacyRules;

        /// <summary>Which key <see cref="Catalogue"/> came from, for an issue's path.</summary>
        public string CatalogueKey => ObjectRules != null || LegacyRules == null
            ? "objectRules"
            : "rules";

        [DataMember(Name = "blocks")]
        public List<PlcTypeObject> Blocks { get; set; }

        [DataMember(Name = "technologyObjects")]
        public List<PlcTypeObject> TechnologyObjects { get; set; }

        [DataMember(Name = "tagTables")]
        public List<PlcTypeObject> TagTables { get; set; }

        [DataMember(Name = "types")]
        public List<PlcTypeObject> Types { get; set; }

        [DataMember(Name = "alarmTextLists")]
        public List<PlcTypeObject> AlarmTextLists { get; set; }
    }
}
