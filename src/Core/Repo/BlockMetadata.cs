using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace Core.Repo
{
    /// <summary>
    /// The JSON object a core block carries on its <c>TITLE</c> line, as it arrives from a
    /// TIA project.
    ///
    /// <code>
    /// FUNCTION_BLOCK "_queue" : Void
    /// TITLE = {"version":"v3.0","author":"cyanezf","family":"core/adt/queue",
    ///          "status":"current","deprecatedBy":null,
    ///          "dependencies":["queueInstanceAttributes-v3.0","MOVE_BLK_VARIANT"]}
    /// </code>
    ///
    /// **This is the only thing that says a block came from the core**, and the maintainer
    /// settled why it has to be: in the repository a file is identified by its name, and a
    /// block inside a TIA project has no file name. The six keys are the generator's contract
    /// - <c>tools/dependency_graph_builder</c> in the core repository defines them - and this
    /// reads all six, where the generator itself keeps only three.
    ///
    /// **The version here and the version in a file name have been seen to disagree**, which
    /// is why the generator gained a check for it: a project block has only this one, so a
    /// TITLE saying v1.0 on a file called -v1.1 is read as v1.0 by everything downstream.
    /// </summary>
    [DataContract]
    public sealed class BlockMetadata
    {
        /// <summary>Version **with** the leading <c>v</c>, as the TITLE writes it: <c>"v1.0"</c>.</summary>
        [DataMember(Name = "version")]
        public string Version { get; set; }

        [DataMember(Name = "author")]
        public string Author { get; set; }

        /// <summary>
        /// The folder the block belongs in, as <c>core/&lt;dirs&gt;</c>. **It travels inside
        /// the block**, which is what makes a misplaced one detectable without the repository
        /// being to hand: cross-checked against the real core, this matches the node's own
        /// folder on all 264 of them.
        /// </summary>
        [DataMember(Name = "family")]
        public string Family { get; set; }

        /// <summary><c>current</c> or <c>deprecated</c> - see <see cref="CoreStatus"/>.</summary>
        [DataMember(Name = "status")]
        public string Status { get; set; }

        [DataMember(Name = "deprecatedBy")]
        public string DeprecatedBy { get; set; }

        [DataMember(Name = "dependencies")]
        public List<string> Dependencies { get; set; }

        /// <summary>The version without its leading <c>v</c>, which is how `core.json` writes it.</summary>
        public string Number =>
            string.IsNullOrEmpty(Version) ? Version : Version.TrimStart('v', 'V');

        /// <summary>
        /// Reads the metadata out of a block's title, or answers null.
        ///
        /// **Null with no problem is the ordinary case**: most blocks in a real project are
        /// not from the core and their title is a title. Null *with* a problem is a block that
        /// meant to carry metadata and does not parse - which the map records against the
        /// block rather than dropping, because a core block nobody can read is exactly what
        /// somebody needs telling about.
        /// </summary>
        /// <param name="problem">Why a title that looked like metadata could not be read.</param>
        public static BlockMetadata Read(string title, out string problem)
        {
            problem = null;

            if (string.IsNullOrWhiteSpace(title)) return null;

            string text = title.Trim();

            // Not JSON at all, so not a core block. The generator draws the line the same way.
            if (text.Length < 2 || text[0] != '{' || text[text.Length - 1] != '}') return null;

            try
            {
                using (MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(text)))
                {
                    BlockMetadata read =
                        new DataContractJsonSerializer(typeof(BlockMetadata)).ReadObject(stream) as BlockMetadata;

                    if (read == null)
                    {
                        problem = "the TITLE is not a metadata object";
                        return null;
                    }

                    if (read.Dependencies == null) read.Dependencies = new List<string>();

                    return read;
                }
            }
            catch (Exception exception)
            {
                // **Strict, unlike the generator, and deliberately so.** `run.py` scans the
                // TITLE with a tolerant regex so a stray comma still yields its dependencies;
                // being tolerant in a *different* way here would give two readings of one
                // file, which is worse than one refusal that names the block.
                problem = "the TITLE could not be read: " + exception.Message;
                return null;
            }
        }
    }
}
