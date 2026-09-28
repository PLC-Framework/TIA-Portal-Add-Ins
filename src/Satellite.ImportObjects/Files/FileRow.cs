using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

using Core.Imports;

namespace Satellite.ImportObjects.Files
{
    /// <summary>
    /// One importable file of the chosen folder, as the list shows it: a tick box, what it is,
    /// what it declares, and - once a run has been - what became of it.
    ///
    /// **The words live here, not in the window**, the reason `DownloadRow` gives: what a row
    /// says and what the status line counts are the same facts.
    /// </summary>
    public sealed class FileRow : INotifyPropertyChanged
    {
        private readonly Func<string, Brush> _ink;
        private readonly Action _changed;
        private bool _take;
        private FileImport _result;

        public FileRow(ImportFile file, Func<string, Brush> ink, Action changed)
        {
            File = file;
            _ink = ink;
            _changed = changed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ImportFile File { get; }

        public bool Take
        {
            get { return _take; }
            set
            {
                if (_take == value) return;

                _take = value;
                Raise(nameof(Take));
                _changed?.Invoke();
            }
        }

        public string FileName => File.FileName;

        /// <summary>
        /// The format as the operator knows it. **A SIMATIC SD pair says so on the .s7dcl's row**:
        /// TIA reads the .s7res beside it whether or not anybody ticked it, so it is not a row of
        /// its own that a tick could leave out.
        /// </summary>
        public string Format
        {
            get
            {
                switch (File.Format)
                {
                    case ImportFormat.SimaticMl: return "SimaticML";
                    case ImportFormat.Document: return File.Companion == null ? "SIMATIC SD" : "SIMATIC SD, with .s7res";
                    default: return "source";
                }
            }
        }

        /// <summary>What it declares, or why that could not be read.</summary>
        public string Declares =>
            File.Objects.Count == 0
                ? File.Problem ?? "nothing"
                : string.Join(", ", File.Objects.Select(o => o.Kind + " " + o.Name));

        public Brush DeclaresInk => _ink(File.Objects.Count == 0 ? "InkWarn" : "Ink");

        /// <summary>What the last run did with it, or nothing before one.</summary>
        public FileImport Result
        {
            get { return _result; }
            set
            {
                _result = value;
                Raise(nameof(Result));
                Raise(nameof(ResultWords));
                Raise(nameof(ResultInk));
            }
        }

        public string ResultWords => _result?.Words ?? string.Empty;

        /// <summary>
        /// **A refusal is what is marked**, and an overwrite in the warning ink: the one is what
        /// needs acting on, the other what TIA did without asking anybody but this window.
        /// </summary>
        public Brush ResultInk
        {
            get
            {
                if (_result == null) return _ink("Ink");

                switch (_result.Outcome)
                {
                    case ImportOutcome.Refused: return _ink("InkBad");
                    case ImportOutcome.Overwritten: return _ink("InkWarn");
                    case ImportOutcome.Left: return _ink("InkMuted");
                    default: return _ink("InkGood");
                }
            }
        }

        public Brush Ink => _ink("Ink");

        public Brush Muted => _ink("InkMuted");

        /// <summary>
        /// The file name, **and that is not decoration**: a `ListViewItem` takes its automation
        /// name from the bound object's `ToString()`.
        /// </summary>
        public override string ToString() => FileName;

        private void Raise(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
