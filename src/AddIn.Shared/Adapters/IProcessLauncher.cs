namespace AddIn.Shared.Adapters
{
    /// <summary>
    /// Starts an executable shipped with the framework - a satellite, or a command-line
    /// helper - from inside TIA Portal.
    ///
    /// This is a port and not a direct call for two reasons. Siemens ships its own Process
    /// wrapper and pairs it with ProcessStartPermission, so that wrapper is the sanctioned
    /// way to start anything from an Add-In. And the assembly holding it,
    /// Siemens.Engineering.AddIn.Utilities, has the same name but a different public key
    /// token in V20 and V21, so even source-identical code cannot be compiled once.
    /// </summary>
    public interface IProcessLauncher
    {
        /// <summary>
        /// Starts the executable without waiting for it. Returns null on success, or the
        /// reason it failed, so the caller can report it the way it reports anything else.
        /// </summary>
        string Start(string fileName);

        /// <summary>
        /// Starts the executable and writes <paramref name="standardInput"/> into it,
        /// then closes the pipe so the child sees end of input.
        ///
        /// This is how a satellite is told what to work on. Nothing touches disk, so there
        /// is no temporary file to clean up, no permission question about where to put it,
        /// and no stale handoff left behind by a run that crashed.
        /// </summary>
        string Start(string fileName, string standardInput);
    }
}
