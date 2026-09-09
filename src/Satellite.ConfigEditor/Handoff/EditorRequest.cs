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

        public EditorRequest(string projectDirectory, string projectName)
        {
            ProjectDirectory = projectDirectory;
            ProjectName = projectName;
        }

        /// <summary>Directory of the open TIA project, where .plc-framework lives.</summary>
        public string ProjectDirectory { get; }

        /// <summary>Shown in the header so two open editors can be told apart.</summary>
        public string ProjectName { get; }
    }
}
