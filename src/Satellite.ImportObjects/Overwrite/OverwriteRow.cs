using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;

using Core.Imports;

namespace Satellite.ImportObjects.Overwrite
{
    /// <summary>
    /// One file that would overwrite something, as the question shows it: the file, what it
    /// meets and where, and what ticking it does. **Ticked is yes** - overwrite, where it is.
    /// </summary>
    public sealed class OverwriteRow : INotifyPropertyChanged
    {
        private readonly Func<string, Brush> _ink;
        private readonly Action _changed;
        private bool _take;

        public OverwriteRow(ImportFile file, IReadOnlyList<ExistingObject> meets, Func<string, Brush> ink, Action changed)
        {
            File = file;
            Meets = meets;
            _ink = ink;
            _changed = changed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public ImportFile File { get; }

        public IReadOnlyList<ExistingObject> Meets { get; }

        public bool Take
        {
            get { return _take; }
            set
            {
                if (_take == value) return;

                _take = value;
                Raise(nameof(Take));
                Raise(nameof(Outcome));
                Raise(nameof(OutcomeInk));
                _changed?.Invoke();
            }
        }

        public string FileName => File.FileName;

        /// <summary>Every object it would overwrite, each with the folder it is in.</summary>
        public string Held => string.Join(", ", Meets.Select(m => m.ToString()));

        /// <summary>
        /// What ticking it does. **"Where it is" is the point**: the folder right-clicked is not
        /// where an existing object goes, and nothing else on screen says so.
        /// </summary>
        public string Outcome => Take ? "overwrite, where it is" : "leave as it is";

        public Brush OutcomeInk => _ink(Take ? "InkWarn" : "InkMuted");

        public Brush Ink => _ink("Ink");

        public override string ToString() => FileName;

        private void Raise(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
