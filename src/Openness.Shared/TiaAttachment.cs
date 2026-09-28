using System;
using System.Collections.Generic;
using System.IO;

namespace Openness.Shared
{
    /// <summary>
    /// What came of attaching: which TIA Portal, which project, or why neither.
    ///
    /// **A failure carries what was on the machine**, because the two reasons this fails look
    /// identical from a window and need opposite answers: no TIA Portal is running at all, or
    /// several are and none of them is the one that launched this. Listing what was found is
    /// the difference between "start TIA Portal" and "that one has no project open".
    /// </summary>
    public sealed class TiaAttachment
    {
        private TiaAttachment(
            int processId,
            string projectName,
            string projectDirectory,
            string problem,
            IReadOnlyList<RunningPortal> considered)
        {
            ProcessId = processId;
            ProjectName = projectName;
            ProjectDirectory = projectDirectory;
            Problem = problem;
            Considered = considered;
        }

        /// <summary>The TIA Portal this attached to, or 0 when it did not.</summary>
        public int ProcessId { get; }

        public string ProjectName { get; }

        /// <summary>Where the project lives, which is where <c>.plc-framework\</c> is.</summary>
        public string ProjectDirectory { get; }

        /// <summary>Why nothing was attached, or null when something was.</summary>
        public string Problem { get; }

        /// <summary>
        /// Every TIA Portal that was running when this ran - or **null when nothing ever
        /// looked**, which is a different fact: with the Openness assemblies missing, the
        /// failure happens before any enumeration, and an empty list would read back as "no TIA
        /// Portal was running" under an error about a missing assembly, with a TIA Portal open
        /// on screen behind it. Seen on the VM, in the core updater.
        /// </summary>
        public IReadOnlyList<RunningPortal> Considered { get; }

        /// <summary>Whether the running TIA Portals were enumerated at all.</summary>
        public bool Looked => Considered != null;

        public bool Attached => Problem == null;

        /// <summary>
        /// Nothing was attached, but there is something to attach *to* - so the window offers
        /// the list rather than stopping. **Not the window guessing**: handing the operator the
        /// same list makes the choice theirs, which is the one person qualified to make it.
        /// </summary>
        public bool CanChoose => !Attached && Considered != null && Considered.Count > 0;

        /// <summary>
        /// The project was found and has a folder. One never saved has none - and everything a
        /// satellite writes into a project goes under its folder, so an attach to it is still
        /// not something to work with.
        /// </summary>
        public bool HasProjectFolder => Attached && !string.IsNullOrWhiteSpace(ProjectDirectory);

        public static TiaAttachment To(
            int processId, string projectName, string projectDirectory, IReadOnlyList<RunningPortal> considered) =>
            new TiaAttachment(processId, projectName, projectDirectory, null, considered);

        public static TiaAttachment Failed(string problem, IReadOnlyList<RunningPortal> considered = null) =>
            new TiaAttachment(0, null, null, problem ?? "TIA Portal could not be reached.", considered);

        /// <summary>
        /// An attach that threw before it could say anything else - nothing listed, since
        /// nothing got that far.
        ///
        /// **The two failures worth telling apart are named**, because their answers differ: the
        /// assemblies are not on this machine, or they are and TIA Portal refused the connection
        /// - which almost always means the Windows user is not in the Openness group.
        /// </summary>
        /// <param name="whereItLooked">
        /// What the assembly resolver searched, from <see cref="OpennessAssemblies.Report"/>.
        /// A registry layout cannot be checked on a machine without TIA Portal, so a resolver
        /// that only says "not found" turns every wrong guess into another round trip to the VM.
        /// </param>
        public static TiaAttachment Failed(Exception exception, string whereItLooked)
        {
            string looked = string.IsNullOrWhiteSpace(whereItLooked) ? string.Empty : "\n\n" + whereItLooked;

            return Failed(Describe(exception) + looked);
        }

        private static string Describe(Exception exception)
        {
            if (exception == null) return "TIA Portal could not be attached to.";

            if (exception is FileNotFoundException || exception is FileLoadException)
                return "The TIA Openness assemblies could not be loaded, so this cannot talk to " +
                       "TIA Portal on this machine.\n\n" + exception.Message;

            if (exception is UnauthorizedAccessException)
                return "TIA Portal refused the connection. The Windows user has to belong to the " +
                       "local \"Siemens TIA Openness\" group, and the membership is read at logon.\n\n" +
                       exception.Message;

            return "TIA Portal could not be attached to.\n\n" + exception.Message;
        }
    }
}
