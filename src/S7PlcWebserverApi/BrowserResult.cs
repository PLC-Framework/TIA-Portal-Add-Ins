using System.Collections.Generic;

namespace S7PlcWebserverApi
{
    public sealed class BrowseResult
    {
        public IReadOnlyList<PlcVariable> Variables { get; }

        /// <summary>True when the walk stopped at a limit, so the list is incomplete.</summary>
        public bool Truncated { get; }

        public BrowseResult(IReadOnlyList<PlcVariable> variables, bool truncated)
        {
            Variables = variables;
            Truncated = truncated;
        }

    }
}
