namespace Core.Checks
{
    public enum CheckOutcome
    {
        /// <summary>The name matched at least one of the rules offered to it.</summary>
        Passed,

        /// <summary>It matched none of them.</summary>
        Failed,

        /// <summary>
        /// Nothing in the configuration covers this type, so there was nothing to check it
        /// against. Reported rather than skipped: a type nobody configured is a hole in the
        /// coding style, and a report that silently omits it looks like a clean result.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// Not checked, and the note says why - a rule whose pattern will not compile or
        /// times out, or a member of an object whose own name matched nothing, so no
        /// interface is known.
        /// </summary>
        Skipped
    }
}
