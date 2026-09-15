using System;

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

        /// <summary>
        /// Starts the executable, **keeps its input open while <paramref name="payload"/>
        /// does the work**, and writes what that returns.
        ///
        /// This is the long-running shape of the one above: a check of a whole PLC takes
        /// seconds to minutes, and a window that only appears once it is finished is
        /// indistinguishable from one that never appeared. Started first, the window says
        /// what is being worked on and fills itself in when the report lands.
        ///
        /// **The work is a delegate rather than the caller keeping a handle, and that is not
        /// a style choice**: an Add-In may not hold a Siemens engineering object in a field,
        /// and the Publisher refuses to package one that does - see the note in CLAUDE.md.
        /// Passing the work inwards keeps the process a local variable of the adapter.
        ///
        /// A payload of null closes the input with nothing in it, which tells the child the
        /// run produced no report rather than leaving it waiting for one.
        /// </summary>
        /// <param name="arguments">
        /// What the child is told before there is anything to send - which project is being
        /// checked, and what was selected.
        /// </param>
        string Start(string fileName, string arguments, Func<string> payload);

        /// <summary>
        /// Shows a folder in the file browser. Returns null on success, or the reason.
        ///
        /// **The intent is a port, the executable is not.** Which program shows a folder is
        /// the host's business, and it is the adapter that already holds the one wrapper
        /// `ProcessStartPermission` authorises - so an action asks for a folder to be shown
        /// and never learns the name of a Windows program.
        /// </summary>
        string Browse(string folder);
    }
}
