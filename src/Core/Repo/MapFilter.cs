using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo
{
    /// <summary>
    /// What a map is asked to cover: which kinds of object, and which programming languages.
    ///
    /// **It exists to make a walk cheap.** In V17-V20 every block and type is exported to read
    /// its title, so a PLC of four hundred is four hundred exports; both the kind and the
    /// language are typed properties that cost nothing to read, so deciding *before* the
    /// export is what turns minutes into seconds for somebody who only wants the SCL.
    ///
    /// **A filtered map records its filter and is not a map of the project.** Read back
    /// without one, a map covering only FBs says the project has no FCs - which is the kind of
    /// silent hole this whole design exists to avoid. <see cref="ProjectMap.Filter"/> carries
    /// it so a comparison can say "of what was mapped" rather than "of the project".
    ///
    /// **Empty means everything.** Not "nothing": a filter nobody filled in is the absence of
    /// a decision, and the safe reading of that is the whole PLC.
    /// </summary>
    [DataContract]
    public sealed class MapFilter
    {
        /// <summary>
        /// The kinds to keep, as <c>Core.Config.CodingStyleNames</c> spells them - <c>OB</c>,
        /// <c>FC</c>, <c>FB</c>, <c>GlobalDB</c>, <c>PlcStruct</c>, <c>PlcTagTable</c> and the
        /// rest. Empty or absent keeps every kind.
        /// </summary>
        [DataMember(Name = "kinds", Order = 0)]
        public List<string> Kinds { get; set; }

        /// <summary>
        /// The programming languages to keep, as the Openness enum names them - <c>SCL</c>,
        /// <c>LAD</c>, <c>FBD</c>, <c>STL</c>, <c>GRAPH</c>, <c>DB</c>. Empty or absent keeps
        /// every language.
        ///
        /// **It only narrows things that have one.** A PLC data type and a tag table have no
        /// programming language at all, so a language filter would otherwise silently remove
        /// every one of them - which is not what ticking "SCL" means.
        /// </summary>
        [DataMember(Name = "languages", Order = 1)]
        public List<string> Languages { get; set; }

        /// <summary>A filter that keeps everything, which is what no filter means.</summary>
        public static MapFilter Everything => new MapFilter();

        public static MapFilter Of(IEnumerable<string> kinds, IEnumerable<string> languages) =>
            new MapFilter
            {
                Kinds = kinds == null ? null : new List<string>(kinds),
                Languages = languages == null ? null : new List<string>(languages)
            };

        /// <summary>Whether this filter narrows anything at all.</summary>
        public bool Narrows => Some(Kinds) || Some(Languages);

        /// <summary>
        /// Whether an object of this kind, in this language, is wanted.
        /// </summary>
        /// <param name="language">
        /// The block's language, or **nothing at all for something that has none** - which
        /// always passes the language half: see <see cref="Languages"/>.
        ///
        /// Empty counts as nothing, exactly as null does. There is no language called <c>""</c>
        /// to tick, so the two can only mean the same thing - and the cost of disagreeing about
        /// it is every PLC data type and every tag table vanishing out of a map the moment
        /// somebody ticks <c>SCL</c>. Found by a caller that passed an empty string.
        /// </param>
        public bool Wants(string kind, string language)
        {
            if (!Has(Kinds, kind)) return false;

            return string.IsNullOrEmpty(language) || Has(Languages, language);
        }

        private static bool Has(List<string> wanted, string value)
        {
            if (!Some(wanted)) return true;

            foreach (string one in wanted)
                if (string.Equals(one, value, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool Some(List<string> values) => values != null && values.Count > 0;
    }
}
