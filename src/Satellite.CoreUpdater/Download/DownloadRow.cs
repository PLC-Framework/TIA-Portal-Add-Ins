using System;
using System.ComponentModel;
using System.Windows.Media;

using Core.Repo;
using Core.Repo.PlcCore;

namespace Satellite.CoreUpdater.Download
{
    /// <summary>
    /// One entry of a download as the window asking about it shows it: its tick box, why it is
    /// on the list, what the project already has of it, and what ticking it would do.
    ///
    /// **The words live here, not in the window**, for the reason `ComparedRow` records: what a
    /// row says and what the status line counts are the same facts, and two spellings of them is
    /// how a count stops matching the rows it describes.
    /// </summary>
    public sealed class DownloadRow : INotifyPropertyChanged
    {
        private readonly Func<string, Brush> _ink;
        private readonly Action _changed;
        private bool _take;

        public DownloadRow(PlannedNode planned, bool take, Func<string, Brush> ink, Action changed)
        {
            Planned = planned;
            _take = take;
            _ink = ink;
            _changed = changed;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public PlannedNode Planned { get; }

        public string Id => Planned.Node.Id;

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

        public string Name => Planned.Node.Base;

        public string Version => "v" + Planned.Node.Version;

        /// <summary>
        /// Why it is on the list: ticked in the core tree, or needed by something that is. **A
        /// dependency names what needs it**, because a line reading only "dependency" does not
        /// say what unticking it would break.
        /// </summary>
        public string Why =>
            Planned.Chosen || Planned.Needers.Count == 0
                ? "ticked"
                : "needed by " + string.Join(", ", Planned.Needers);

        /// <summary>What the project holds of this name, and where — or a dash for nothing.</summary>
        public string Held
        {
            get
            {
                if (!Collides) return "—";

                string version = string.IsNullOrWhiteSpace(Planned.HeldVersion) ? "no version" : "v" + Planned.HeldVersion;

                return string.IsNullOrWhiteSpace(Planned.From) ? version : version + " in " + Planned.From;
            }
        }

        /// <summary>
        /// Who else in the project depends on it. **A floor, not a total**: only blocks that
        /// carry the core's metadata declare what they use, and the window says so above the list.
        /// </summary>
        public string UsedBy => Planned.Users.Count == 0 ? string.Empty : string.Join(", ", Planned.Users);

        public bool Collides => Planned.Found != null;

        /// <summary>
        /// What ticking it does, in the words the operator decides on. **Overwriting says where
        /// it will end up** when that is not where it is: TIA overwrites in place, so a download
        /// that also moves is doing something the operator would not otherwise expect.
        /// </summary>
        public string Outcome
        {
            get
            {
                if (Take)
                {
                    if (!Collides) return "import";

                    return Planned.Moves ? "overwrite, and move into " + Planned.Folder : "overwrite";
                }

                return Collides ? "leave as it is" : "leave out — the project has none";
            }
        }

        /// <summary>
        /// **Overwriting is what is marked**, because it is the one choice TIA will not question;
        /// leaving out something the project does not have is marked as a fault, because whatever
        /// needs it will not build without it.
        /// </summary>
        public Brush OutcomeInk
        {
            get
            {
                if (Take) return _ink(Collides ? "InkWarn" : "Ink");

                return _ink(Collides ? "InkMuted" : "InkBad");
            }
        }

        public Brush Ink => _ink("Ink");

        public Brush Muted => _ink("InkMuted");

        /// <summary>
        /// The name, **and that is not decoration**: a `ListViewItem` takes its automation name
        /// from the bound object's `ToString()`, so without this every row would announce itself
        /// by type name, to a screen reader as much as to a test.
        /// </summary>
        public override string ToString() => Name;

        private void Raise(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
