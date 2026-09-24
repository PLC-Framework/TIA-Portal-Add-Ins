namespace Core.Logging
{
    /// <summary>
    /// What a line is.
    ///
    /// **Everything is written whatever the level, and there is no switch to turn one off**
    /// (2026-09-24, the maintainer asked for a complete log). A level nobody can disable is
    /// only worth having as something to *read* by, which is what this is for: a column to
    /// filter on once the run is over and somebody is looking for the line that matters.
    ///
    /// <see cref="Start"/> and <see cref="End"/> are written by <see cref="Log"/> itself and
    /// are what makes one run findable in a file two processes may both have been appending
    /// to - they carry the run's own identifier at both ends.
    /// </summary>
    public enum LogLevel
    {
        Start,
        Info,
        Warn,
        Error,
        End
    }
}
