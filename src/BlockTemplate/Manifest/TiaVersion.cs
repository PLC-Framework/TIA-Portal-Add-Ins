using System.Collections.Generic;

namespace BlockTemplate.Manifest
{
    /// <summary>One entry of the manifest's <c>tiaVersions</c>: a TIA Portal version and its values.</summary>
    public sealed class TiaVersion
    {
        internal TiaVersion(string version, IReadOnlyList<TiaVersionVariable> variables)
        {
            Version = version;
            Variables = variables;
        }

        /// <summary>The TIA Portal major version, as digits with no leading zero: <c>20</c>, <c>21</c>.</summary>
        public string Version { get; }

        public IReadOnlyList<TiaVersionVariable> Variables { get; }
    }
}
