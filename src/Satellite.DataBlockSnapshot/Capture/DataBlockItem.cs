using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Satellite.DataBlockSnapshot.Capture
{
    /// <summary>One row of the block list: what to capture, and how it went.</summary>
    public sealed class DataBlockItem : INotifyPropertyChanged
    {
        private bool _selected = true;
        private CaptureOutcome _outcome = CaptureOutcome.Pending;
        private string _detail = string.Empty;

        public DataBlockItem(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public bool Selected
        {
            get => _selected;
            set => Set(ref _selected, value);
        }

        public CaptureOutcome Outcome
        {
            get => _outcome;
            set
            {
                if (Set(ref _outcome, value)) Raise(nameof(Status));
            }
        }

        public string Detail
        {
            get => _detail;
            set => Set(ref _detail, value);
        }

        /// <summary>
        /// Words rather than a symbol. A capture that was not written has to say so in a
        /// way nobody skims past.
        /// </summary>
        public string Status
        {
            get
            {
                switch (_outcome)
                {
                    case CaptureOutcome.Working: return "reading";
                    case CaptureOutcome.Written: return "written";
                    case CaptureOutcome.Missing: return "not on this CPU";
                    case CaptureOutcome.Failed: return "NOT WRITTEN";
                    case CaptureOutcome.Cancelled: return "cancelled";
                    default: return string.Empty;
                }
            }
        }

        public void Reset()
        {
            Outcome = CaptureOutcome.Pending;
            Detail = string.Empty;
        }

        /// <summary>
        /// Not decoration: a <c>ListViewItem</c> takes its **automation name** from the bound
        /// object's ToString(), so without this every row announced itself as
        /// "Satellite.DataBlockSnapshot.Capture.DataBlockItem" — to a screen reader as much
        /// as to a test. The columns are what the eye reads; this is what everything else
        /// reads. Same reason GroupNode overrides it in the config editor.
        /// </summary>
        public override string ToString() => Name;

        public event PropertyChangedEventHandler PropertyChanged;

        private bool Set<T>(ref T field, T value, [CallerMemberName] string property = null)
        {
            if (Equals(field, value)) return false;

            field = value;
            Raise(property);
            return true;
        }

        private void Raise(string property) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    }
}
