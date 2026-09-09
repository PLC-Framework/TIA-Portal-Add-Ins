using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Newtonsoft.Json.Linq;

using Satellite.ConfigEditor.Document;

namespace Satellite.ConfigEditor.Editing
{
    /// <summary>
    /// One rule a type implements, shown as a tag with a cross rather than as a tick box.
    ///
    /// The catalogue runs to fifteen rules and a section to ten types: a box per pairing is
    /// a hundred and fifty controls, nearly all of them empty, and the noise hides the one
    /// thing worth reading — what this type actually implements. A tick box that
    /// *disappears* when you clear it would also behave like nothing else in Windows; a
    /// cross says exactly what it does.
    /// </summary>
    public sealed class RuleTag
    {
        public RuleTag(string id, bool isKnown)
        {
            Id = id;
            IsKnown = isKnown;
        }

        public string Id { get; }

        /// <summary>
        /// False when no rule of that name exists — a reference left dangling by a
        /// hand-edited file.
        ///
        /// It is shown in red rather than hidden, and that is the point: with tick boxes
        /// built from the catalogue there was no box to clear, so the editor could report
        /// the problem and not repair it. As a tag, removing it is one click.
        /// </summary>
        public bool IsKnown { get; }
    }

    /// <summary>One entry of one of the five lists: a TIA object type and its rules.</summary>
    public sealed class TypeRow : INotifyPropertyChanged
    {
        private readonly JObject _entry;
        private readonly Action _changed;
        private readonly IReadOnlyList<string> _catalogue;

        public TypeRow(JObject entry, IReadOnlyList<string> catalogue, Action changed)
        {
            _entry = entry;
            _catalogue = catalogue;
            _changed = changed;

            AvailableTypes = new ObservableCollection<string>();
            Implemented = new ObservableCollection<RuleTag>();
            AvailableRules = new ObservableCollection<string>();

            Refresh();
        }

        /// <summary>
        /// What this row's type drop-down may offer: the types nothing else in the section
        /// is using, **plus its own** — without which a row could not display the value it
        /// already holds. Kept in step by <see cref="AppliesSection.Refresh"/>.
        /// </summary>
        public ObservableCollection<string> AvailableTypes { get; }

        /// <summary>The rules this type implements, in catalogue order.</summary>
        public ObservableCollection<RuleTag> Implemented { get; }

        /// <summary>What the + button may offer: the rules this type does not have yet.</summary>
        public ObservableCollection<string> AvailableRules { get; }

        public bool CanAddRule => AvailableRules.Count > 0;

        public string Type
        {
            get => _entry["type"]?.Value<string>();
            set
            {
                if (string.Equals(Type, value, StringComparison.Ordinal)) return;

                _entry["type"] = value;
                Notify(nameof(Type));
                _changed?.Invoke();
            }
        }

        /// <summary>The entry itself, so the section can remove it from its array.</summary>
        public JObject Entry => _entry;

        public void AddRule(string id)
        {
            if (string.IsNullOrEmpty(id)) return;

            List<string> ids = Implemented.Select(tag => tag.Id).ToList();
            if (ids.Contains(id)) return;

            ids.Add(id);
            Write(ids);
        }

        public void RemoveRule(RuleTag tag)
        {
            if (tag == null) return;

            Write(Implemented.Where(other => !ReferenceEquals(other, tag))
                             .Select(other => other.Id));
        }

        private void Write(IEnumerable<string> ids)
        {
            // Written in catalogue order, with anything unrecognised kept at the end: the
            // file should not change just because two people clicked in a different
            // sequence, and a dangling reference must survive a save so it can be seen and
            // removed rather than silently dropped.
            List<string> wanted = ids.ToList();

            IEnumerable<string> ordered =
                _catalogue.Where(wanted.Contains)
                          .Concat(wanted.Where(id => !_catalogue.Contains(id)));

            CodingStyleEditor.SetImplements(_entry, ordered);

            Refresh();
            _changed?.Invoke();
        }

        /// <summary>Rebuilds the tags and what is left to offer, from the document.</summary>
        public void Refresh()
        {
            IReadOnlyList<string> implements = CodingStyleEditor.ImplementsOf(_entry);

            Implemented.Clear();
            foreach (string id in implements)
                Implemented.Add(new RuleTag(id, _catalogue.Contains(id)));

            AvailableRules.Clear();
            foreach (string id in _catalogue.Where(id => !implements.Contains(id)))
                AvailableRules.Add(id);

            Notify(nameof(CanAddRule));
        }

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// One of the five lists, ready to bind.
    ///
    /// **A type appears at most once per section.** A second row for the same type would
    /// say nothing the first does not: the pairing of a TIA object type with the rules it
    /// accepts is already complete, and two of them only raise the question of which one
    /// counts. So a type in use is offered nowhere it could be duplicated — neither by
    /// "Add type" nor by another row's drop-down.
    /// </summary>
    public sealed class AppliesSection : INotifyPropertyChanged
    {
        public AppliesSection(Applies definition)
        {
            Definition = definition;
            Rows = new ObservableCollection<TypeRow>();
            FreeTypes = new ObservableCollection<string>();
        }

        public Applies Definition { get; }

        public string Title => Definition.Title;

        public ObservableCollection<TypeRow> Rows { get; }

        /// <summary>The types no row is using, which is what "Add type" may offer.</summary>
        public ObservableCollection<string> FreeTypes { get; }

        /// <summary>False when every type of the closed set already has a row.</summary>
        public bool CanAdd => FreeTypes.Count > 0;

        /// <summary>
        /// Adds a row and keeps the drop-downs honest.
        ///
        /// The section wraps the caller's callback rather than leaving the window to
        /// remember: every change to a row can change what the *other* rows may offer, and
        /// a refresh that depends on somebody remembering is one that eventually is not
        /// done.
        /// </summary>
        public TypeRow Add(JObject entry, IReadOnlyList<string> catalogue, Action changed)
        {
            TypeRow row = new TypeRow(entry, catalogue, () =>
            {
                Refresh();
                changed?.Invoke();
            });

            Rows.Add(row);
            Refresh();

            return row;
        }

        public void Remove(TypeRow row)
        {
            if (!Rows.Remove(row)) return;

            row.Entry.Remove();
            Refresh();
        }

        /// <summary>
        /// Recomputes what every type drop-down may show. Called after anything that
        /// changes which types are taken: adding a row, removing one, or changing one's
        /// type.
        /// </summary>
        public void Refresh()
        {
            HashSet<string> used = new HashSet<string>(
                Rows.Select(row => row.Type).Where(type => type != null), StringComparer.Ordinal);

            Sync(FreeTypes, Definition.Types.Where(type => !used.Contains(type)));

            foreach (TypeRow row in Rows)
            {
                // Its own value first, so the drop-down opens on what the row actually
                // holds rather than on whatever happens to be free.
                IEnumerable<string> offered = Definition.Types
                    .Where(type => !used.Contains(type) ||
                                   string.Equals(type, row.Type, StringComparison.Ordinal));

                Sync(row.AvailableTypes, offered);
            }

            Notify(nameof(CanAdd));
        }

        /// <summary>
        /// Brings a bound collection to a wanted state **in place**.
        ///
        /// Clearing and refilling would be shorter and wrong: a ComboBox whose ItemsSource
        /// empties loses its selection, and the row would come back blank — which then
        /// writes an empty type into the document.
        /// </summary>
        private static void Sync(ObservableCollection<string> target, IEnumerable<string> wanted)
        {
            List<string> list = wanted.ToList();

            for (int i = target.Count - 1; i >= 0; i--)
                if (!list.Contains(target[i])) target.RemoveAt(i);

            for (int i = 0; i < list.Count; i++)
            {
                int at = target.IndexOf(list[i]);

                if (at < 0) target.Insert(i, list[i]);
                else if (at != i) target.Move(at, i);
            }
        }

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
