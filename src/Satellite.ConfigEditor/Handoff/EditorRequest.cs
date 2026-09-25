namespace Satellite.ConfigEditor.Handoff
{
    /// <summary>
    /// What the Add-In knows and this window does not: which project is open.
    ///
    /// One field, and optional even so. Started by hand the window still works - the
    /// operator points it at a project - and that mode is how everything underneath was
    /// verified without TIA Portal installed.
    /// </summary>
    public sealed class EditorRequest
    {
        public static readonly EditorRequest Empty = new EditorRequest(null, null);

        public EditorRequest(string projectDirectory, string projectName, string problem = null)
        {
            ProjectDirectory = projectDirectory;
            ProjectName = projectName;
            Problem = problem;
        }

        /// <summary>Directory of the open TIA project, where .plc-framework lives.</summary>
        public string ProjectDirectory { get; }

        /// <summary>Shown in the header so two open editors can be told apart.</summary>
        public string ProjectName { get; }

        /// <summary>
        /// Why what TIA Portal sent could not be read, or null.
        ///
        /// **Something that arrived broken is not a window started by hand**, and the two used
        /// to open identically: an empty form, with nothing to say that the Add-In had sent a
        /// project and it had been lost on the way. The form is still empty - everything in it
        /// can be typed - but the window and the log now say why.
        /// </summary>
        public string Problem { get; }

        /// <summary>An empty request that says what went wrong on the way in.</summary>
        public static EditorRequest Unreadable(string problem) => new EditorRequest(null, null, problem);
    }
}
