namespace Core.Imports
{
    /// <summary>
    /// What became of one file, in the words the window shows and the log writes - **one
    /// spelling for both**, the reason `ComparedRow` keeps its words beside its facts.
    /// </summary>
    public sealed class FileImport
    {
        private FileImport(string fileName, ImportOutcome outcome, string detail, string note)
        {
            FileName = fileName;
            Outcome = outcome;
            Detail = detail;
            Note = note;
        }

        public string FileName { get; }

        public ImportOutcome Outcome { get; }

        /// <summary>
        /// TIA's reason for a refusal, or where an overwritten object was - or null.
        /// </summary>
        public string Detail { get; }

        /// <summary>
        /// What TIA said beside a file that did go in, or null. **Neither a success nor a
        /// refusal**, and reporting it as either would mislead: SIMATIC SD answers
        /// <c>PartialSuccess</c> with messages, and a source can come back clean without every
        /// object it declares. The file went in; this says what is worth reading about it.
        /// </summary>
        public string Note { get; }

        public static FileImport Imported(ImportFile file, string note = null) =>
            new FileImport(file?.FileName, ImportOutcome.Imported, null, Blank(note));

        public static FileImport Overwritten(ImportFile file, string where, string note = null) =>
            new FileImport(file?.FileName, ImportOutcome.Overwritten, Blank(where), Blank(note));

        public static FileImport Left(ImportFile file) =>
            new FileImport(file?.FileName, ImportOutcome.Left, null, null);

        /// <summary>A refusal always says why; one with no reason says that nothing came back.</summary>
        public static FileImport Refused(ImportFile file, string reason) =>
            new FileImport(file?.FileName, ImportOutcome.Refused,
                           Blank(reason) ?? "TIA Portal gave no reason.", null);

        /// <summary>What the result column says.</summary>
        public string Words
        {
            get
            {
                string said;

                switch (Outcome)
                {
                    case ImportOutcome.Imported: said = "imported"; break;
                    case ImportOutcome.Overwritten: said = Detail == null ? "overwritten" : "overwritten in " + Detail; break;
                    case ImportOutcome.Left: said = "left as it is"; break;
                    default: return "refused - " + Detail;
                }

                return Note == null ? said : said + " - " + Note;
            }
        }

        public override string ToString() => FileName + ": " + Words;

        private static string Blank(string text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }
}
