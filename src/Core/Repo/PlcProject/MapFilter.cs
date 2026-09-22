using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace Core.Repo.PlcProject
{
    /// <summary>
    /// What a map is asked to cover: which kinds of object, and **within each kind**, which
    /// programming languages.
    ///
    /// **It exists to make a walk cheap.** In V17-V20 every block and type is exported to read
    /// its title, so a PLC of four hundred is four hundred exports; both the kind and the
    /// language are typed properties that cost nothing, so deciding *before* the export is what
    /// turns minutes into seconds for somebody who only wants the SCL.
    ///
    /// **The language belongs to the kind, and that was a correction** (2026-09-17). It was one
    /// flat list across the whole map, which cannot say "the SCL functions and every data
    /// block": unticking SCL to drop a function block took the functions with it. A language
    /// only ever means anything inside the kind it was counted in, and now that is what it
    /// says.
    ///
    /// **A filtered map records its filter and is not a map of the project.** Read back without
    /// one, a map covering only FBs says the project has no FCs - which is the kind of silent
    /// hole this whole design exists to avoid. <see cref="ProjectMap.Filter"/> carries it so a
    /// comparison can say "of what was mapped" rather than "of the project".
    ///
    /// **Empty means everything.** Not "nothing": a filter nobody filled in is the absence of
    /// a decision, and the safe reading of that is the whole PLC.
    /// </summary>
    [DataContract]
    public sealed class MapFilter
    {
        /// <summary>
        /// One entry per kind to keep, each naming the languages wanted inside it. Empty or
        /// absent keeps every kind, and a kind with no entry is not kept at all.
        /// </summary>
        [DataMember(Name = "kinds", Order = 0)]
        public List<KindFilter> Kinds { get; set; }

        /// <summary>A filter that keeps everything, which is what no filter means.</summary>
        public static MapFilter Everything => new MapFilter();

        public static MapFilter Of(IEnumerable<KindFilter> kinds)
        {
            List<KindFilter> wanted = new List<KindFilter>();

            if (kinds != null)
                foreach (KindFilter one in kinds)
                    if (one != null && !string.IsNullOrEmpty(one.Kind)) wanted.Add(one);

            return new MapFilter { Kinds = wanted.Count == 0 ? null : wanted };
        }

        /// <summary>Whether this filter narrows anything at all.</summary>
        public bool Narrows => Kinds != null && Kinds.Count > 0;

        /// <summary>
        /// Whether an object of this kind, in this language, is wanted.
        /// </summary>
        /// <param name="language">
        /// The block's language, or **nothing at all for something that has none** - which
        /// always passes the language half.
        ///
        /// Empty counts as nothing, exactly as null does. There is no language called <c>""</c>
        /// to tick, so the two can only mean the same thing - and the cost of disagreeing about
        /// it is every PLC data type and every tag table vanishing out of a map the moment
        /// somebody ticks <c>SCL</c>. Found by a caller that passed an empty string.
        /// </param>
        public bool Wants(string kind, string language)
        {
            if (!Narrows) return true;

            KindFilter found = For(kind);

            if (found == null) return false;

            // A kind ticked with nothing said about its languages is the whole kind - the same
            // reading as an empty filter, one level down.
            if (!Some(found.Languages)) return true;

            return string.IsNullOrEmpty(language) || Has(found.Languages, language);
        }

        /// <summary>What was asked for inside one kind, or null when the kind was not asked for.</summary>
        public KindFilter For(string kind)
        {
            if (!Narrows) return null;

            foreach (KindFilter one in Kinds)
                if (string.Equals(one.Kind, kind, StringComparison.OrdinalIgnoreCase)) return one;

            return null;
        }

        private static bool Has(List<string> wanted, string value)
        {
            foreach (string one in wanted)
                if (string.Equals(one, value, StringComparison.OrdinalIgnoreCase)) return true;

            return false;
        }

        private static bool Some(List<string> values) => values != null && values.Count > 0;
    }
}
