using System.Collections.Generic;

namespace BlockTemplate
{
    /// <summary>One entry of the CONFIG-JSON's <c>tiaVersions</c>: a TIA Portal version and its values.</summary>
    public sealed class TiaVersion
    {
        public TiaVersion(string version, IReadOnlyList<TiaVersionVariable> variables)
        {
            Version = version;
            Variables = variables;
        }

        /// <summary>The TIA Portal major version, as digits: <c>20</c>, <c>21</c>.</summary>
        public string Version { get; }

        public IReadOnlyList<TiaVersionVariable> Variables { get; }
    }
}
