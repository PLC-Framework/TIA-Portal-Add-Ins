using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

using Newtonsoft.Json.Linq;

namespace Satellite.ConfigEditor.Editing
{
    /// <summary>
    /// One folder in a hierarchy tree, bound to the <see cref="JObject"/> it came from.
    ///
    /// The node holds the entry rather than a copy of its name, so an edit lands in the
    /// document immediately and a save has nothing to reconcile.
    /// </summary>
    public sealed class GroupNode : INotifyPropertyChanged
    {
        private readonly Action _changed;
        private bool _isDuplicate;

        public GroupNode(JObject entry, GroupNode parent, Action changed)
        {
            Entry = entry;
            Parent = parent;
            _changed = changed;

            Children = new ObservableCollection<GroupNode>();
            IsExpanded = true;

            JArray groups = entry["groups"] as JArray;
            if (groups == null) return;

            foreach (JObject child in groups.OfType<JObject>())
                Children.Add(new GroupNode(child, this, changed));
        }

        public JObject Entry { get; }

        /// <summary>Null for a node at the root of its list.</summary>
        public GroupNode Parent { get; }

        public ObservableCollection<GroupNode> Children { get; }

        /// <summary>Expanded by default: a collapsed tree hides the thing being edited.</summary>
        public bool IsExpanded { get; set; }

        public string Name
        {
            get => Entry["name"]?.Value<string>() ?? string.Empty;
            set
            {
                if (string.Equals(Name, value, StringComparison.Ordinal)) return;

                Entry["name"] = value;
                Notify(nameof(Name));
                _changed?.Invoke();
            }
        }

        /// <summary>
        /// True when a sibling of the same parent has this name.
        ///
        /// Shown **on the node** rather than only in the message at the bottom: the
        /// validator reports the path, but in a tree the position of the mistake is what
        /// makes it obvious. Case-insensitive, because two folders called "core" and "Core"
        /// are the same folder to anybody reading the tree.
        /// </summary>
        public bool IsDuplicate
        {
            get => _isDuplicate;
            set
            {
                if (_isDuplicate == value) return;

                _isDuplicate = value;
                Notify(nameof(IsDuplicate));
            }
        }

        /// <summary>
        /// The folder name.
        ///
        /// Not decoration: a `TreeViewItem` takes its **automation name** from the bound
        /// object's `ToString()`, so without this every node announces itself as
        /// "Satellite.ConfigEditor.Editing.GroupNode" — to a screen reader as much as to a
        /// test. The `TextBlock` in the template is what the eye sees; this is what
        /// everything else sees.
        /// </summary>
        public override string ToString() => Name;

        /// <summary>The JSON array this node's children live in, created if needed.</summary>
        public JArray ChildArray()
        {
            JArray groups = Entry["groups"] as JArray;

            if (groups == null)
            {
                groups = new JArray();
                Entry["groups"] = groups;
            }

            return groups;
        }

        /// <summary>
        /// Drops the <c>groups</c> key once the last child is gone. `groups` is optional and
        /// most folders are leaves, so leaving an empty array behind would add noise to the
        /// file that means nothing.
        /// </summary>
        public void TidyChildArray()
        {
            if (Children.Count == 0) Entry.Remove("groups");
        }

        private void Notify(string name) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public event PropertyChangedEventHandler PropertyChanged;
    }

    /// <summary>
    /// One of the seven trees: a JSON array of groups, and the operations on it.
    ///
    /// Add, rename and remove — **no moving**, decided deliberately. The folder names carry
    /// numeric prefixes (`00-OB`, `01-FSL`) and TIA orders them by name, so a reordering
    /// gesture would cost a fair amount of work to change nothing anybody sees.
    /// </summary>
    public sealed class GroupTree
    {
        private readonly JArray _array;
        private readonly Action _changed;

        public GroupTree(JArray array, Action changed)
        {
            _array = array;
            _changed = changed;

            Roots = new ObservableCollection<GroupNode>();

            foreach (JObject entry in array.OfType<JObject>())
                Roots.Add(new GroupNode(entry, null, changed));

            MarkDuplicates();
        }

        public ObservableCollection<GroupNode> Roots { get; }

        /// <summary>Adds a folder at the top of this concern.</summary>
        public GroupNode AddRoot()
        {
            JObject entry = new JObject { ["name"] = Unused(Roots) };
            _array.Add(entry);

            GroupNode node = new GroupNode(entry, null, _changed);
            Roots.Add(node);

            MarkDuplicates();
            _changed?.Invoke();

            return node;
        }

        /// <summary>Adds a folder inside another.</summary>
        public GroupNode AddChild(GroupNode parent)
        {
            if (parent == null) return AddRoot();

            JObject entry = new JObject { ["name"] = Unused(parent.Children) };
            parent.ChildArray().Add(entry);

            GroupNode node = new GroupNode(entry, parent, _changed);
            parent.Children.Add(node);
            parent.IsExpanded = true;

            MarkDuplicates();
            _changed?.Invoke();

            return node;
        }

        /// <summary>
        /// Removes a folder **and everything under it**. That is what deleting a folder
        /// means, and the button says so.
        /// </summary>
        public void Remove(GroupNode node)
        {
            if (node == null) return;

            node.Entry.Remove();

            if (node.Parent == null)
            {
                Roots.Remove(node);
            }
            else
            {
                node.Parent.Children.Remove(node);
                node.Parent.TidyChildArray();
            }

            MarkDuplicates();
            _changed?.Invoke();
        }

        /// <summary>
        /// Flags every node that shares a name with a sibling. Recomputed after any change,
        /// and while typing — a rule enforced only at save time is one the operator meets
        /// at the worst moment.
        /// </summary>
        public void MarkDuplicates()
        {
            Mark(Roots);
        }

        private static void Mark(IReadOnlyList<GroupNode> siblings)
        {
            // Among siblings only: the same name in a different branch is a different
            // folder, and flagging it would be wrong.
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            HashSet<string> twice = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GroupNode node in siblings)
                if (!seen.Add(node.Name.Trim())) twice.Add(node.Name.Trim());

            foreach (GroupNode node in siblings)
            {
                node.IsDuplicate = twice.Contains(node.Name.Trim());
                Mark(node.Children);
            }
        }

        private static string Unused(IReadOnlyList<GroupNode> siblings)
        {
            const string wanted = "New group";

            List<string> taken = siblings.Select(node => node.Name).ToList();
            if (!taken.Contains(wanted)) return wanted;

            for (int n = 2; ; n++)
            {
                string candidate = wanted + " " + n;
                if (!taken.Contains(candidate)) return candidate;
            }
        }
    }
}
