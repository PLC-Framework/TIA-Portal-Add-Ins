using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

using Core.Config;
using Core.Config.Validation;
using Core.Secrets;

using Satellite.ConfigEditor.Document;
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

            _request = request;

            _sections.Add(new Section("Metadata", "metadata"));
            _sections.Add(new Section("Repository", "coreRemoteRepositoryConfig", "coreLocalRepositoryConfig"));
            _sections.Add(new Section("Hierarchy", "projectConfig.hierarchy") { Editable = false });
            _sections.Add(new Section("Coding style", "projectConfig.codingStyle") { Editable = false });

            Nav.ItemsSource = _sections;

            CoreSourceBox.Items.Add(MetadataValidator.Local);
            CoreSourceBox.Items.Add(MetadataValidator.Remote);

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
                CoreSourceBox.SelectedItem = _document.Get("metadata.coreSource");
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

        private void Collect()
        {
            _document.Set("metadata.coreSource", CoreSourceBox.SelectedItem as string);
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
        /// Dims the repository section coreSource does not select. Dimmed rather than
        /// hidden, and still editable: a project often carries both and switches between
        /// them, and hiding the other one would make it look lost.
        /// </summary>
        private void Emphasise()
        {
            bool remote = MetadataValidator.Remote.Equals(CoreSourceBox.SelectedItem as string,
                                                          StringComparison.Ordinal);

            LocalTitle.Opacity = remote ? 0.45 : 1.0;
            LocalGrid.Opacity = remote ? 0.45 : 1.0;
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

            string error = _document.Save();

            StatusLine.Text = error ?? "Saved to " + _document.Path;
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

            OnlyVisible(section.Name == "Metadata" ? MetadataPanel : RepositoryPanel);
        }

        private void OnlyVisible(UIElement panel)
        {
            foreach (UIElement candidate in new UIElement[]
                     { MetadataPanel, RepositoryPanel, LaterPanel, MissingPanel })
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
