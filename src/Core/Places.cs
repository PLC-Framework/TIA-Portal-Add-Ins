namespace Core
{
    /// <summary>
    /// How the framework writes where something lives inside a PLC's software.
    ///
    /// One constant, and it earns a file because it has two consumers that must not each
    /// spell it themselves: the coding-style report writes it into a column, and the core
    /// updater takes it as an argument. Two `"*"` literals in two assemblies is exactly how
    /// two halves stop agreeing without anything failing to compile.
    /// </summary>
    public static class Places
    {
        /// <summary>
        /// The PLC's own program rather than one of its software units.
        ///
        /// **A presentation, not a value.** Whatever produces a location keeps "no unit" as
        /// an empty string; this is what it becomes when a person has to read it, so an empty
        /// cell still means "this does not say" rather than claiming the general program.
        /// </summary>
        public const string GeneralProgram = "*";

        /// <summary>The unit as a reader sees it: its name, or <see cref="GeneralProgram"/>.</summary>
        public static string UnitOrGeneral(string unit) =>
            string.IsNullOrWhiteSpace(unit) ? GeneralProgram : unit;

        /// <summary>
        /// The unit as the object model sees it: null for the general program, whichever of
        /// the two spellings arrives. The inverse of <see cref="UnitOrGeneral"/>, and the
        /// reason both live here rather than being re-derived at each call site.
        /// </summary>
        public static string UnitOrNull(string unit) =>
            string.IsNullOrWhiteSpace(unit) || unit.Trim() == GeneralProgram ? null : unit.Trim();

        public static bool IsGeneralProgram(string unit) => UnitOrNull(unit) == null;
    }
}
