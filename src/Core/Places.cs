namespace Core
{
    /// <summary>
    /// How the framework writes where something lives inside a PLC's software.
    ///
    /// Two constants, and they earn a file because each has consumers that must not spell it
    /// themselves: the coding-style report writes the star into a column and the core updater
    /// takes it as an argument, and both satellites' walks join folder names before the
    /// comparison reads them apart again. Two `"*"` literals in two assemblies is exactly how
    /// two halves stop agreeing without anything failing to compile.
    /// </summary>
    public static class Places
    {
        /// <summary>
        /// What separates one folder from the next inside a PLC: <c>Program blocks/03-ALL</c>.
        ///
        /// **Not the platform's.** It is written into `project.json` and read back by whatever
        /// compares that file, so it is the document's own spelling rather than whatever
        /// `Path.DirectorySeparatorChar` says on the machine that happens to be reading.
        /// </summary>
        public const string Separator = "/";


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
