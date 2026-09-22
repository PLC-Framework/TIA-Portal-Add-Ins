using System;

namespace Core.Repo.PlcCore
{
    /// <summary>
    /// The closed set a node's <c>status</c> comes from, spelled once.
    ///
    /// Taken verbatim from a hand-written TITLE, so anything outside the two is **unknown**
    /// rather than assumed current - a block whose status nobody can read is not a block to
    /// report as up to date.
    /// </summary>
    public static class PlcCoreStatus
    {
        public const string Current = "current";
        public const string Deprecated = "deprecated";

        public static bool IsCurrent(string status) => string.Equals(status, Current, StringComparison.Ordinal);

        public static bool IsDeprecated(string status) => string.Equals(status, Deprecated, StringComparison.Ordinal);

        public static bool IsKnown(string status) => IsCurrent(status) || IsDeprecated(status);
    }
}
