using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Core;
using Core.Config;
using Core.Config.Validation;
using Core.Secrets;

using Newtonsoft.Json.Linq;

using Satellite.ConfigEditor.Document;
using Satellite.ConfigEditor.Editing;
using Satellite.ConfigEditor.Handoff;

// System.Windows.Controls declares a ValidationResult of its own - WPF's, for binding
// validation - so the plain name is ambiguous in any window that also validates a
// document. Aliasing the one meant here beats renaming Core's type, which is correct
// where it lives.
using ValidationResult = Core.Config.Validation.ValidationResult;

namespace Satellite.ConfigEditor
{
    public partial class MainWindow : Window
    {
        /// <summary>
        /// The one secret this editor owns. config.json always carries the literal
        /// <c>${GITHUB_TOKEN}</c>; the value lives in the per-user .env, because the
        /// project folder is under version control.
        /// </summary>
        private const string TokenVariable = "GITHUB_TOKEN";
        private const string TokenReference = "${" + TokenVariable + "}";

        /// <summary>
        /// The drop-down's word for a null coreSource. Shown, never written: the file gets a
        /// JSON null, which is what Core and the schema both read as "no repository".
        /// </summary>
        private const string NoCoreSource = "none";

        private readonly EditorRequest _request;
        private readonly ObservableCollection<Section> _sections = new ObservableCollection<Section>();

        private ConfigDocument _document;
        private string _configPath;

        /// <summary>
        /// True while the controls are being filled from the document. Without it every
        /// load would look like an edit, and the window would open already dirty.
        /// </summary>
        private bool _loading;

        private string _savedToken;
        private bool _tokenChanged;

        public MainWindow(EditorRequest request)
        {
            InitializeComponent();

            // In the title bar, so it reaches every screenshot a tester sends
            // without them having to go and find it.
            Title = "Config editor " + Product.Version;

            _request = request;

            _sections.Add(new Section("Metadata", "metadata"));
            _sections.Add(new Section("Repository", "coreRemoteRepositoryConfig", "coreLocalRepositoryConfig"));
            _sections.Add(new Section("Hierarchy", "projectConfig.hierarchy"));
            _sections.Add(new Section("Coding style", "projectConfig.codingStyle"));

            Nav.ItemsSource = _sections;

            CoreSourceBox.Items.Add(MetadataValidator.Local);
            CoreSourceBox.Items.Add(MetadataValidator.Remote);
            CoreSourceBox.Items.Add(NoCoreSource);

            ProjectLine.Text = Describe(request);

            Open(Resolve(request.ProjectDirectory));
        }

        private static string Describe(EditorRequest request)
        {
            List<string> parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(request.ProjectName)) parts.Add(request.ProjectName);
            if (!string.IsNullOrWhiteSpace(request.ProjectDirectory)) parts.Add(request.ProjectDirectory);

            return parts.Count == 0 ? "Started without a project" : string.Join("  -  ", parts);
        }

        // ----------------------------------------------------------------- opening

        private void Open(string path)
        {
            _configPath = path;

            if (string.IsNullOrWhiteSpace(path))
            {
                ShowMissing("No project was handed over.",
                            "Choose the folder of a TIA project to edit its configuration.");
                return;
            }

            if (!File.Exists(path))
            {
                ShowMissing("This project has no configuration yet.", path);
                return;
            }

            _document = ConfigDocument.Load(path, out string error);

            if (_document == null)
            {
                // A file that exists but cannot be parsed is NOT offered a template: that
                // button would overwrite whatever is in there, and a file somebody broke by
                // hand is still a file somebody wants back.
                ShowBroken(error, path);
                return;
            }

            Fill();
            Show(_sections[0]);
        }

        private void ShowMissing(string message, string path)
        {
            _document = null;

            MissingText.Text = message;
            MissingPath.Text = path ?? string.Empty;
            CreateButton.Visibility = string.IsNullOrWhiteSpace(_configPath)
                ? Visibility.Collapsed
                : Visibility.Visible;

            NavPanel.Visibility = Visibility.Collapsed;
            OnlyVisible(MissingPanel);
            Working(false);
            StatusLine.Text = string.Empty;
        }

        private void ShowBroken(string error, string path)
        {
            _document = null;

            MissingText.Text = "This configuration could not be opened.";
            MissingPath.Text = path + Environment.NewLine + Environment.NewLine + error;

            // Deliberately no "create from template" here - see Open().
            CreateButton.Visibility = Visibility.Collapsed;

            NavPanel.Visibility = Visibility.Collapsed;
            OnlyVisible(MissingPanel);
            Working(false);
            StatusLine.Text = "Fix the file by hand, then reopen this window.";
        }

        private void OnCreateFromTemplate(object sender, RoutedEventArgs e)
        {
            _document = ConfigDocument.FromTemplate(_configPath, out string note);

            if (_document == null)
            {
                StatusLine.Text = note ?? "The template could not be loaded.";
                return;
            }

            NavPanel.Visibility = Visibility.Visible;
            Fill();
            Show(_sections[0]);

            // Nothing is on disk yet: this is a document in memory until Save, so backing
            // out of a mistake costs nothing.
            StatusLine.Text = note == null
                ? "Started from the template. Nothing is written until you save."
                : note;
        }

        private void OnChooseProject(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                   new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description =
                    "The TIA project folder - the one holding " + ConfigPaths.Folder +
                    ". Picking " + ConfigPaths.Folder + " itself works too.";

                if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

                NavPanel.Visibility = Visibility.Visible;
                Open(Resolve(dialog.SelectedPath));
            }
        }

        private static string Resolve(string chosen) => ConfigLocation.Resolve(chosen);

        // ----------------------------------------------------------------- binding

        private void Fill()
        {
            _loading = true;
            try
            {
                SelectCoreSource(_document.Get("metadata.coreSource"));
                VersionBox.Text = _document.Get("metadata.version") ?? string.Empty;
                AuthorBox.Text = _document.Get("metadata.author") ?? string.Empty;
                DescriptionBox.Text = _document.Get("metadata.description") ?? string.Empty;

                LocalRepositoryBox.Text = _document.Get("coreLocalRepositoryConfig.repository") ?? string.Empty;
                LocalFolderBox.Text = _document.Get("coreLocalRepositoryConfig.folder") ?? string.Empty;
                LocalDependencyBox.Text = _document.Get("coreLocalRepositoryConfig.dependencyFile") ?? string.Empty;

                ApiUrlBox.Text = _document.Get("coreRemoteRepositoryConfig.apiUrl") ?? string.Empty;
                OwnerBox.Text = _document.Get("coreRemoteRepositoryConfig.owner") ?? string.Empty;
                RemoteRepositoryBox.Text = _document.Get("coreRemoteRepositoryConfig.repository") ?? string.Empty;
                BranchBox.Text = _document.Get("coreRemoteRepositoryConfig.branch") ?? string.Empty;
                RemoteFolderBox.Text = _document.Get("coreRemoteRepositoryConfig.folder") ?? string.Empty;
                RemoteDependencyBox.Text = _document.Get("coreRemoteRepositoryConfig.dependencyFile") ?? string.Empty;

                LoadToken();
            }
            finally
            {
                _loading = false;
            }

            FillCodingStyle();
            FillHierarchy();
            Emphasise();
            Revalidate();
        }

        private void LoadToken()
        {
            _savedToken = DotEnv.Get(TokenVariable) ?? string.Empty;
            _tokenChanged = false;

            TokenBox.Password = _savedToken;
            TokenPlain.Text = _savedToken;

            string stored = _document.Get("coreRemoteRepositoryConfig.token");

            if (!string.IsNullOrWhiteSpace(stored) && !Variables.IsReference(stored))
            {
                // The file was written by hand with the secret in it. Worth saying loudly:
                // config.json lives inside the TIA project, and TIA projects are versioned.
                TokenNote.Text =
                    "This file holds a token literally, not as " + TokenReference + ". Saving " +
                    "will move it to the .env and leave the reference behind.";
                return;
            }

            TokenNote.Text = _savedToken.Length == 0
                ? "Empty means a public repository. Anything typed here is written to " +
                  (Core.InstallPaths.EnvFile ?? "the per-user .env") + ", never to config.json."
                : "Kept in " + (Core.InstallPaths.EnvFile ?? "the per-user .env") +
                  ". config.json only carries " + TokenReference + ".";
        }

        /// <summary>
        /// Shows what the file says. A value outside the set - a typo by hand - is added to
        /// the list and shown as it is, rather than left unselected: the next edit anywhere
        /// on the form writes the selection back, and an empty selection would have turned
        /// that typo into "no repository" without a word, where the validator now names it.
        /// </summary>
        private void SelectCoreSource(string stored)
        {
            string shown = stored ?? NoCoreSource;

            if (!CoreSourceBox.Items.Contains(shown)) CoreSourceBox.Items.Add(shown);
            CoreSourceBox.SelectedItem = shown;
        }

        private void Collect()
        {
            string coreSource = CoreSourceBox.SelectedItem as string;

            // Written as an explicit null, never removed: absent reads the same to Core, but
            // null in the file says somebody chose it.
            if (coreSource == null || coreSource == NoCoreSource) _document.SetNull("metadata.coreSource");
            else _document.Set("metadata.coreSource", coreSource);

            _document.Set("metadata.version", VersionBox.Text.Trim());
            _document.Set("metadata.author", AuthorBox.Text.Trim());
            _document.Set("metadata.description", DescriptionBox.Text);

            _document.Set("coreLocalRepositoryConfig.repository", LocalRepositoryBox.Text.Trim());
            _document.Set("coreLocalRepositoryConfig.folder", LocalFolderBox.Text.Trim());
            _document.Set("coreLocalRepositoryConfig.dependencyFile", LocalDependencyBox.Text.Trim());

            _document.Set("coreRemoteRepositoryConfig.apiUrl", ApiUrlBox.Text.Trim());
            _document.Set("coreRemoteRepositoryConfig.owner", OwnerBox.Text.Trim());
            _document.Set("coreRemoteRepositoryConfig.repository", RemoteRepositoryBox.Text.Trim());
            _document.Set("coreRemoteRepositoryConfig.branch", BranchBox.Text.Trim());
            _document.Set("coreRemoteRepositoryConfig.folder", RemoteFolderBox.Text.Trim());
            _document.Set("coreRemoteRepositoryConfig.dependencyFile", RemoteDependencyBox.Text.Trim());

            // The reference, never the secret - and only when the remote section is in use
            // at all, so a purely local configuration does not grow a token key it will
            // never read.
            if (_document.Has("coreRemoteRepositoryConfig"))
                _document.Set("coreRemoteRepositoryConfig.token", TokenReference);
        }

        private void OnFieldChanged(object sender, TextChangedEventArgs e) => Touched();

        private void OnCoreSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            Touched();
            Emphasise();
        }

        private void Touched()
        {
            if (_loading || _document == null) return;

            Collect();
            Revalidate();
        }

        /// <summary>
        /// Dims every repository section coreSource does not select - both of them when it
        /// selects none. Dimmed rather than hidden, and still editable: a project often
        /// carries both and switches between them, and hiding one would make it look lost.
        /// </summary>
        private void Emphasise()
        {
            string selected = CoreSourceBox.SelectedItem as string;

            bool local = MetadataValidator.Local.Equals(selected, StringComparison.Ordinal);
            bool remote = MetadataValidator.Remote.Equals(selected, StringComparison.Ordinal);

            LocalTitle.Opacity = local ? 1.0 : 0.45;
            LocalGrid.Opacity = local ? 1.0 : 0.45;
            RemoteTitle.Opacity = remote ? 1.0 : 0.45;
            RemoteGrid.Opacity = remote ? 1.0 : 0.45;
        }

        // ----------------------------------------------------------------- the token

        private string CurrentToken() =>
            TokenReveal.IsChecked == true ? TokenPlain.Text : TokenBox.Password;

        private void OnTokenChanged(object sender, RoutedEventArgs e)
        {
            if (_loading) return;

            _tokenChanged = true;
            Revalidate();
        }

        /// <summary>
        /// Swap which twin is showing. Same pattern as the PLC password: a PasswordBox
        /// cannot reveal what it holds and its Password is not a dependency property, so
        /// the only way is two controls with one always collapsed. The text moves across
        /// first, then the focus and the caret - without that last part the operator is
        /// left typing into a field that is no longer there.
        /// </summary>
        private void OnTokenRevealChanged(object sender, RoutedEventArgs e)
        {
            if (TokenReveal.IsChecked == true)
            {
                _loading = true;
                try { TokenPlain.Text = TokenBox.Password; }
                finally { _loading = false; }

                TokenBox.Visibility = Visibility.Collapsed;
                TokenPlain.Visibility = Visibility.Visible;
                TokenPlain.Focus();
                TokenPlain.CaretIndex = TokenPlain.Text.Length;
            }
            else
            {
                _loading = true;
                try { TokenBox.Password = TokenPlain.Text; }
                finally { _loading = false; }

                TokenPlain.Visibility = Visibility.Collapsed;
                TokenBox.Visibility = Visibility.Visible;
                TokenBox.Focus();
            }
        }

        // ----------------------------------------------------------------- coding style

        private CodingStyleEditor _style;
        private readonly ObservableCollection<AppliesSection> _applies =
            new ObservableCollection<AppliesSection>();

        private JObject _rule;

        private void FillCodingStyle()
        {
            _style = new CodingStyleEditor(_document);

            _loading = true;
            try
            {
                string chosen = RuleList.SelectedItem as string;

                RuleList.ItemsSource = null;
                RuleList.ItemsSource = _style.RuleIds();

                // Keep the operator where they were: rebuilding the list after an edit
                // would otherwise throw them back to the top every keystroke.
                if (chosen != null && _style.RuleIds().Contains(chosen)) RuleList.SelectedItem = chosen;
                else if (RuleList.Items.Count > 0) RuleList.SelectedIndex = 0;

                FillApplies();
            }
            finally
            {
                _loading = false;
            }

            ShowRule(RuleList.SelectedItem as string);
        }

        private void FillApplies()
        {
            IReadOnlyList<string> catalogue = _style.RuleIds();

            _applies.Clear();

            foreach (Applies definition in CodingStyleEditor.Sections)
            {
                AppliesSection section = new AppliesSection(definition);

                foreach (JObject entry in _style.Objects(definition.Key).OfType<JObject>())
                    section.Add(entry, catalogue, OnStyleEdited);

                // Also for a section with no rows at all, so "Add type" starts out right.
                section.Refresh();

                _applies.Add(section);
            }

            AppliesList.ItemsSource = _applies;
        }

        /// <summary>A tick or a type changed: the document already has it, so just revalidate.</summary>
        private void OnStyleEdited()
        {
            if (_loading) return;

            Revalidate();
        }

        private void ShowRule(string id)
        {
            _rule = id == null ? null : _style.Rule(id);

            bool any = _rule != null;
            RuleDetail.IsEnabled = any;
            RemoveRuleButton.IsEnabled = any;

            _loading = true;
            try
            {
                RuleIdBox.Text = any ? id : string.Empty;
                RuleRegexBox.Text = any ? _rule["regex"]?.Value<string>() ?? string.Empty : string.Empty;
                RuleDescriptionBox.Text = any ? CodingStyleEditor.DescriptionsOf(_rule) : string.Empty;
            }
            finally
            {
                _loading = false;
            }

            ShowRegexState();
        }

        private void OnRuleSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;

            ShowRule(RuleList.SelectedItem as string);
        }

        private void OnAddRule(object sender, RoutedEventArgs e)
        {
            if (_style == null) return;

            JObject added = _style.AddRule();
            string id = added["id"].Value<string>();

            FillCodingStyle();
            RuleList.SelectedItem = id;
            ShowRule(id);

            RuleIdBox.Focus();
            RuleIdBox.SelectAll();

            Revalidate();
        }

        private void OnRemoveRule(object sender, RoutedEventArgs e)
        {
            string id = RuleList.SelectedItem as string;
            if (_style == null || id == null) return;

            // The references go with it. A rule removed but still implemented somewhere is
            // exactly the dangling reference this section exists to prevent.
            _style.RemoveRule(id);

            FillCodingStyle();
            Revalidate();
        }

        /// <summary>
        /// Renaming is committed on leaving the field, not per keystroke: an id is a key,
        /// and rewriting every reference on the way from "type" to "typeName" would churn
        /// the document through a dozen half-typed names.
        /// </summary>
        private void OnRuleIdCommitted(object sender, RoutedEventArgs e)
        {
            if (_loading || _style == null || _rule == null) return;

            string oldId = _rule["id"]?.Value<string>();
            string wanted = RuleIdBox.Text.Trim();

            if (wanted.Length == 0 || string.Equals(wanted, oldId, StringComparison.Ordinal))
            {
                _loading = true;
                try { RuleIdBox.Text = oldId ?? string.Empty; }
                finally { _loading = false; }
                return;
            }

            string used = _style.RenameRule(oldId, wanted);

            FillCodingStyle();
            RuleList.SelectedItem = used;
            ShowRule(used);

            StatusLine.Text = string.Equals(used, wanted, StringComparison.Ordinal)
                ? StatusLine.Text
                : "'" + wanted + "' was already taken, so the rule is called '" + used + "'.";

            Revalidate();
        }

        private void OnRuleIdKey(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) OnRuleIdCommitted(sender, e);
        }

        private void OnRuleRegexChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _rule == null) return;

            _rule["regex"] = RuleRegexBox.Text;

            ShowRegexState();
            Revalidate();
        }

        private void OnRuleSampleChanged(object sender, TextChangedEventArgs e) => ShowRegexState();

        private void OnRuleDescriptionChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _rule == null) return;

            CodingStyleEditor.SetDescriptions(_rule, RuleDescriptionBox.Text);
            Revalidate();
        }

        private void ShowRegexState()
        {
            string pattern = RuleRegexBox.Text;
            string problem = CodingStyleEditor.RegexProblem(pattern);

            if (_rule == null)
            {
                RegexState.Text = string.Empty;
                SampleState.Text = string.Empty;
                return;
            }

            RegexState.Text = problem == null ? "compiles" : problem;
            RegexState.Foreground = problem == null ? Ok : Bad;

            bool? matches = CodingStyleEditor.Matches(pattern, RuleSampleBox.Text);

            if (matches == null)
            {
                SampleState.Text = string.Empty;
                return;
            }

            SampleState.Text = matches.Value ? "matches" : "does not match";
            SampleState.Foreground = matches.Value ? Ok : Bad;
        }

        private static readonly System.Windows.Media.Brush Ok =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x7F, 0xC9, 0x8A));

        private static readonly System.Windows.Media.Brush Bad =
            new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xE2, 0x6D, 0x6D));

        private void OnCodingTab(object sender, RoutedEventArgs e)
        {
            if (RulesArea == null || AppliesArea == null) return;

            bool rules = RulesTab.IsChecked == true;

            RulesArea.Visibility = rules ? Visibility.Visible : Visibility.Collapsed;
            AppliesArea.Visibility = rules ? Visibility.Collapsed : Visibility.Visible;
        }

        /// <summary>
        /// Offers the types this section still has free. Only the free ones: a second row
        /// for a type that already has one says nothing the first does not, and only raises
        /// the question of which of the two counts.
        /// </summary>
        private void OnAddType(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            AppliesSection section = button?.Tag as AppliesSection;

            if (section == null || _style == null || !section.CanAdd) return;

            // Nothing to choose between, so do not make them choose.
            if (section.FreeTypes.Count == 1)
            {
                AddType(section, section.FreeTypes[0]);
                return;
            }

            ContextMenu menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            foreach (string type in section.FreeTypes)
            {
                string chosen = type;

                // TextBlock rather than a string header, for the same reason the tick boxes
                // use one: WPF would read an underscore as a keyboard accelerator.
                MenuItem item = new MenuItem { Header = new TextBlock { Text = type } };
                item.Click += (s, args) => AddType(section, chosen);

                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void AddType(AppliesSection section, string type)
        {
            JObject entry = _style.AddObject(section.Definition.Key, type);

            section.Add(entry, _style.RuleIds(), OnStyleEdited);
            Revalidate();
        }

        /// <summary>
        /// Offers the rules this type does not implement yet — the same shape as "Add
        /// type", so it is learnt once. Building the menu from the catalogue is also what
        /// keeps a dangling reference from being created here in the first place.
        /// </summary>
        private void OnAddRuleToType(object sender, RoutedEventArgs e)
        {
            Button button = sender as Button;
            TypeRow row = button?.Tag as TypeRow;

            if (row == null || !row.CanAddRule) return;

            if (row.AvailableRules.Count == 1)
            {
                row.AddRule(row.AvailableRules[0]);
                Revalidate();
                return;
            }

            ContextMenu menu = new ContextMenu
            {
                PlacementTarget = button,
                Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
            };

            foreach (string id in row.AvailableRules)
            {
                string chosen = id;

                // TextBlock rather than a string header: a rule id like data_container
                // would otherwise lose its underscores to accelerator parsing.
                MenuItem item = new MenuItem { Header = new TextBlock { Text = id } };
                item.Click += (s, args) => { row.AddRule(chosen); Revalidate(); };

                menu.Items.Add(item);
            }

            menu.IsOpen = true;
        }

        private void OnRemoveRuleFromType(object sender, RoutedEventArgs e)
        {
            RuleTag tag = (sender as FrameworkElement)?.Tag as RuleTag;
            if (tag == null) return;

            // The row it belongs to is whichever holds this exact tag.
            TypeRow row = _applies.SelectMany(section => section.Rows)
                                  .FirstOrDefault(candidate => candidate.Implemented.Contains(tag));

            row?.RemoveRule(tag);
            Revalidate();
        }

        private void OnRemoveType(object sender, RoutedEventArgs e)
        {
            TypeRow row = (sender as FrameworkElement)?.Tag as TypeRow;
            if (row == null) return;

            foreach (AppliesSection section in _applies)
            {
                if (!section.Rows.Contains(row)) continue;

                // The section owns both halves: the row and the entry behind it.
                section.Remove(row);
                break;
            }

            Revalidate();
        }

        // ----------------------------------------------------------------- hierarchy

        private HierarchyEditor _hierarchy;
        private GroupTree _tree;
        private GroupNode _group;

        private void FillHierarchy()
        {
            _hierarchy = new HierarchyEditor(_document);
            ShowConcern();
        }

        /// <summary>Which of the seven trees the single TreeView is showing.</summary>
        private void ShowConcern()
        {
            if (_hierarchy == null) return;

            bool units = UnitsTab.IsChecked == true;

            UnitsBar.Visibility = units ? Visibility.Visible : Visibility.Collapsed;

            if (units)
            {
                UnitsToggle.Content = _hierarchy.HasUnits ? "Remove section" : "Add section";
                UnitsTabs.Visibility = _hierarchy.HasUnits ? Visibility.Visible : Visibility.Collapsed;
            }

            JArray array =
                !units ? _hierarchy.Groups(SelectedConcern())
                : _hierarchy.HasUnits ? _hierarchy.UnitGroups(SelectedUnitConcern())
                : null;

            // The optional section is simply absent: nothing to show, and nothing to edit
            // until it is added.
            if (array == null)
            {
                _tree = null;
                GroupsTree.ItemsSource = null;
                GroupsTree.IsEnabled = false;
                ShowGroup(null);
                return;
            }

            GroupsTree.IsEnabled = true;

            _loading = true;
            try
            {
                _tree = new GroupTree(array, OnHierarchyEdited);
                GroupsTree.ItemsSource = _tree.Roots;
            }
            finally
            {
                _loading = false;
            }

            ShowGroup(null);
        }

        private string SelectedConcern()
        {
            if (TechnologyTab.IsChecked == true) return "technologyObjects";
            if (TagTablesTab.IsChecked == true) return "tagTables";
            if (TypesTab.IsChecked == true) return "types";

            return "blocks";
        }

        private string SelectedUnitConcern()
        {
            if (UnitTagTablesTab.IsChecked == true) return "tagTables";
            if (UnitTypesTab.IsChecked == true) return "types";

            return "blocks";
        }

        private void OnConcernChanged(object sender, RoutedEventArgs e)
        {
            if (UnitsBar == null) return;

            ShowConcern();
        }

        private void OnUnitConcernChanged(object sender, RoutedEventArgs e)
        {
            if (UnitsBar == null || _hierarchy == null) return;

            ShowConcern();
        }

        /// <summary>
        /// Creates the software-unit section with all three of its lists, or removes it
        /// whole. Half a section is worse than none: the contract requires every list once
        /// the section exists.
        /// </summary>
        private void OnToggleUnits(object sender, RoutedEventArgs e)
        {
            if (_hierarchy == null) return;

            if (_hierarchy.HasUnits)
            {
                // This throws away folders somebody built, so it asks first - the only
                // confirmation in the window, and it earns it.
                MessageBoxResult answer = MessageBox.Show(
                    this,
                    "Remove the software unit section and every folder in it?",
                    "Config editor",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (answer != MessageBoxResult.Yes) return;

                _hierarchy.RemoveUnits();
            }
            else
            {
                _hierarchy.AddUnits();
            }

            ShowConcern();
            Revalidate();
        }

        private void OnHierarchyEdited()
        {
            if (_loading) return;

            Revalidate();
        }

        private void OnGroupSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (_loading) return;

            ShowGroup(GroupsTree.SelectedItem as GroupNode);
        }

        private void ShowGroup(GroupNode node)
        {
            _group = node;

            bool any = node != null;
            GroupDetail.IsEnabled = any;
            RemoveGroupButton.IsEnabled = any;
            AddChildButton.IsEnabled = any;

            _loading = true;
            try
            {
                GroupNameBox.Text = any ? node.Name : string.Empty;
            }
            finally
            {
                _loading = false;
            }

            ShowGroupWarning();
        }

        private void OnGroupNameChanged(object sender, TextChangedEventArgs e)
        {
            if (_loading || _group == null || _tree == null) return;

            _group.Name = GroupNameBox.Text;

            // Recomputed on every keystroke rather than at save: a duplicate marked on the
            // node while typing is a rule the operator meets at the moment they can act on
            // it.
            _tree.MarkDuplicates();

            ShowGroupWarning();
            Revalidate();
        }

        private void ShowGroupWarning()
        {
            if (_group == null)
            {
                GroupWarning.Text = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(_group.Name))
                GroupWarning.Text = "A folder needs a name.";
            else if (_group.IsDuplicate)
                GroupWarning.Text = "Another folder beside this one is already called that.";
            else
                GroupWarning.Text = string.Empty;
        }

        private void OnAddRootGroup(object sender, RoutedEventArgs e) => Added(_tree?.AddRoot());

        private void OnAddChildGroup(object sender, RoutedEventArgs e) =>
            Added(_tree?.AddChild(GroupsTree.SelectedItem as GroupNode));

        private void Added(GroupNode node)
        {
            if (node == null) return;

            ShowGroup(node);

            // Straight into the name box with it selected: a folder called "New group" is
            // never what anybody wanted, so the next keystroke should replace it.
            GroupNameBox.Focus();
            GroupNameBox.SelectAll();

            Revalidate();
        }

        private void OnRemoveGroup(object sender, RoutedEventArgs e)
        {
            GroupNode node = GroupsTree.SelectedItem as GroupNode;
            if (node == null || _tree == null) return;

            _tree.Remove(node);
            ShowGroup(null);
            Revalidate();
        }

        // ----------------------------------------------------------------- validation

        private void Revalidate()
        {
            if (_document == null) return;

            ValidationResult structural = _document.Validate();
            ValidationResult environmental = _document.ValidateEnvironment();

            foreach (Section section in _sections)
                section.HasIssues = structural.Issues.Any(issue => section.Owns(issue.Path));

            SaveButton.IsEnabled = structural.IsValid;
            RevertButton.IsEnabled = _document.IsDirty || _tokenChanged;

            StatusLine.Text = Summarise(structural, environmental);
        }

        private string Summarise(ValidationResult structural, ValidationResult environmental)
        {
            if (!structural.IsValid)
            {
                ValidationIssue first = structural.Issues[0];

                return structural.Issues.Count == 1
                    ? "Cannot save: " + first
                    : "Cannot save: " + structural.Issues.Count + " problems. First: " + first;
            }

            List<string> parts = new List<string>();

            if (_document.IsDirty || _tokenChanged) parts.Add("Unsaved changes.");

            // Environmental problems are reported and never block. A configuration prepared
            // here for another station is not wrong because a drive is not mapped on this
            // one.
            if (!environmental.IsValid)
            {
                parts.Add(environmental.Issues.Count == 1
                    ? "Warning: " + environmental.Issues[0]
                    : "Warnings: " + environmental.Issues.Count + ". First: " + environmental.Issues[0]);
            }

            if (parts.Count == 0) parts.Add("No problems.");

            return string.Join("  ", parts);
        }

        // ----------------------------------------------------------------- commands

        private void OnSave(object sender, RoutedEventArgs e)
        {
            if (_document == null) return;

            ValidationResult structural = _document.Validate();

            if (!structural.IsValid)
            {
                StatusLine.Text = "Cannot save while there are problems.";
                return;
            }

            // The secret first. If the .env cannot be written there is no point leaving a
            // config.json behind that references a variable nobody set.
            if (_tokenChanged)
            {
                string problem = DotEnv.Set(TokenVariable, CurrentToken());

                if (problem != null)
                {
                    StatusLine.Text = problem;
                    return;
                }

                _savedToken = CurrentToken();
                _tokenChanged = false;
            }

            string error = _document.Save(out string note);

            StatusLine.Text = error ?? ("Saved to " + _document.Path + (note == null ? string.Empty : "  " + note));
            Revalidate();
        }

        private void OnRevert(object sender, RoutedEventArgs e)
        {
            if (_document == null) return;

            if (File.Exists(_configPath))
            {
                Open(_configPath);
                StatusLine.Text = "Reverted to what is on disk.";
                return;
            }

            // Never saved: reverting means going back to the template it started from.
            OnCreateFromTemplate(sender, e);
            StatusLine.Text = "Reverted to the template.";
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_document == null || (!_document.IsDirty && !_tokenChanged)) return;

            MessageBoxResult answer = MessageBox.Show(
                this,
                "There are unsaved changes. Close anyway?",
                "Config editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (answer != MessageBoxResult.Yes) e.Cancel = true;
        }

        // ----------------------------------------------------------------- navigation

        private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
        {
            Section section = Nav.SelectedItem as Section;
            if (section != null) Show(section);
        }

        private void Show(Section section)
        {
            if (!ReferenceEquals(Nav.SelectedItem, section)) Nav.SelectedItem = section;

            if (!section.Editable)
            {
                LaterTitle.Text = section.Name;
                OnlyVisible(LaterPanel);
                return;
            }

            if (section.Name == "Metadata") OnlyVisible(MetadataPanel);
            else if (section.Name == "Repository") OnlyVisible(RepositoryPanel);
            else if (section.Name == "Hierarchy") OnlyVisible(HierarchyPanel);
            else OnlyVisible(CodingStylePanel);
        }

        private void OnlyVisible(UIElement panel)
        {
            foreach (UIElement candidate in new UIElement[]
                     { MetadataPanel, RepositoryPanel, HierarchyPanel, CodingStylePanel,
                       LaterPanel, MissingPanel })
            {
                candidate.Visibility = ReferenceEquals(candidate, panel)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        private void Working(bool editable)
        {
            SaveButton.IsEnabled = editable;
            RevertButton.IsEnabled = editable;
        }

        private void OnBrowseLocalRepository(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.FolderBrowserDialog dialog =
                   new System.Windows.Forms.FolderBrowserDialog())
            {
                dialog.Description = "The local core repository";
                dialog.SelectedPath = LocalRepositoryBox.Text.Trim();

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    LocalRepositoryBox.Text = dialog.SelectedPath;
            }
        }
    }

    /// <summary>
    /// One entry in the left-hand navigation, and which part of the document it answers
    /// for - which is how a problem lands a dot next to the section that owns it.
    /// </summary>
    public sealed class Section : INotifyPropertyChanged
    {
        private bool _hasIssues;

        public Section(string name, params string[] prefixes)
        {
            Name = name;
            Prefixes = prefixes;
        }

        public string Name { get; }

        public string[] Prefixes { get; }

        /// <summary>False while a section is still edited by hand rather than here.</summary>
        public bool Editable { get; set; } = true;

        public bool HasIssues
        {
            get => _hasIssues;
            set
            {
                if (_hasIssues == value) return;

                _hasIssues = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasIssues)));
            }
        }

        public bool Owns(string path) =>
            Prefixes.Any(prefix =>
                path != null &&
                (path.Equals(prefix, StringComparison.Ordinal) ||
                 path.StartsWith(prefix + ".", StringComparison.Ordinal) ||
                 path.StartsWith(prefix + "[", StringComparison.Ordinal)));

        public override string ToString() => Name;

        public event PropertyChangedEventHandler PropertyChanged;
    }
}
