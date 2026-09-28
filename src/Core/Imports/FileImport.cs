namespace Core.Imports
{
    /// <summary>
    /// What became of one file, in the words the window shows and the log writes - **one
    /// spelling for both**, the reason `ComparedRow` keeps its words beside its facts.
    /// </summary>
    public sealed class FileImport
    {
        private FileImport(string fileName, ImportOutcome outcome, string detail)
        {
            FileName = fileName;
            Outcome = outcome;
            Detail = detail;
        }

        public string FileName { get; }

        public ImportOutcome Outcome { get; }

        /// <summary>
        /// TIA's reason for a refusal, or where an overwritten object was - or null.
        /// </summary>
        public string Detail { get; }

        public static FileImport Imported(ImportFile file) =>
            new FileImport(file?.FileName, ImportOutcome.Imported, null);

        public static FileImport Overwritten(ImportFile file, string where) =>
            new FileImport(file?.FileName, ImportOutcome.Overwritten, string.IsNullOrWhiteSpace(where) ? null : where);

        public static FileImport Left(ImportFile file) =>
            new FileImport(file?.FileName, ImportOutcome.Left, null);

        /// <summary>A refusal always says why; one with no reason says that nothing came back.</summary>
        public static FileImport Refused(ImportFile file, string reason) =>
            new FileImport(file?.FileName, ImportOutcome.Refused,
                           string.IsNullOrWhiteSpace(reason) ? "TIA Portal gave no reason." : reason.Trim());

        /// <summary>What the result column says.</summary>
        public string Words
        {
            get
            {
                switch (Outcome)
                {
                    case ImportOutcome.Imported: return "imported";
                    case ImportOutcome.Overwritten: return Detail == null ? "overwritten" : "overwritten in " + Detail;
                    case ImportOutcome.Left: return "left as it is";
                    default: return "refused - " + Detail;
                }
            }
        }

        public override string ToString() => FileName + ": " + Words;
    }
}
