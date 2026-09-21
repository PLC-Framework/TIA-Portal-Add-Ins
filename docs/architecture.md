# Architecture

How the solution is laid out and why each type lives where it does. The short version: a type's layer is decided by **what it depends on**, not by who calls it today.

## Layout

```
TIA-Portal-Add-Ins.slnx
├── assets/                   shared binary assets, grouped by feature (see below)
├── docs/reference/           the Openness API and the CPU web server, as measured
└── src/
    ├── Core/                 (net48, AnyCPU) — model, no host types at all          ← EXISTS
    ├── AddIn.Shared/         (net48, AnyCPU) — the Add-In layer, NO Siemens          ← EXISTS
    ├── UI.Shared/            (net48, WPF) — brand resources, shared windows          ← EXISTS
    ├── S7PlcWebserverApi/    (net48, AnyCPU) — JSON-RPC client for a CPU's webserver ← EXISTS
    ├── GitHubApi/            (net48, AnyCPU) — REST client for a core kept in a repository ← EXISTS
    ├── AddIn.V20/            (net48, x64) — references PublicAPI\V20.addIn (V17–V20) ← EXISTS
    ├── AddIn.V21/            (net48, x64) — references PublicAPI\V21\net48           ← EXISTS
    ├── Satellite.About/      (net48, WPF) — the About window                         ← EXISTS
    ├── Satellite.DataBlockSnapshot/  (net48, WPF) — captures a DB to .xlsx           ← EXISTS
    ├── Satellite.ConfigEditor/       (net48, WPF) — edits config.json                ← EXISTS
    ├── Satellite.CodingStyleReport/  (net48, WPF) — shows a coding-style check       ← EXISTS
    ├── Satellite.CoreUpdater/        (net48, WPF, LIBRARY) — the window, its port, Compare\, Download\ ← EXISTS
    ├── Satellite.CoreUpdater.V20/    (net48, x64) — the same app, Openness V17–V20   ← EXISTS
    ├── Satellite.CoreUpdater.V21/    (net48, x64) — the same app, Openness V21       ← EXISTS
    ├── Satellite.<Name>/     (net48, WPF) — a UI app the Add-In launches
    └── Tool.<Name>/          (net48) — a command-line helper
```

> **One satellite, three projects, and it is the first of its kind.** `Satellite.CoreUpdater` is a **library**: it holds the window and an `ITiaSession` port, and references no Siemens assembly at all. The two executables beside it are thin — a `Main`, an assembly resolver and one adapter each — because Openness is `Siemens.Engineering` (token `d29ec89bac048f84`) in V17–V20 and `Siemens.Engineering.Base` (token `29bfe5fdf4ba5d3b`) in V21, so one binary cannot serve both. Read off the built output, which is where a claim like that should come from: the library references `mscorlib`, `PresentationFramework`, `System.Management` and WPF, and **not one Siemens assembly**.

```
src/S7PlcWebserverApi/
├── S7PlcWebserverApi.csproj  SDK-style net48, Newtonsoft.Json, System.Net.Http
├── PlcClient.cs              session, JSON-RPC transport, single and batched calls
├── PlcClient.Browse.cs       the recursive walk over a data block
├── PlcClient.Read.cs         batched reads with a per-variable fallback
├── PlcVariable.cs            a readable leaf: path, name, datatype, read_only
├── PlcValue.cs               a value, or the reason it could not be read
├── BrowserResult.cs          the leaves plus whether a limit was hit
├── PlcLimits.cs              runaway guards and batch sizes
└── PlcExceptions.cs          connection / auth / call failures kept apart
```

```
src/Satellite.DataBlockSnapshot/
├── Satellite.DataBlockSnapshot.csproj   references Core, UI.Shared, S7PlcWebserverApi
├── Credentials/
│   └── CredentialStore.cs    per-user, DPAPI-protected, keyed by project + PLC name
└── Export/
    ├── Snapshot.cs           one capture: rows, read window, truncation
    ├── XlsxExporter.cs       typed cells, one sheet of values plus one of provenance
    └── ValueText.cs          invariant rendering of a value into a cell
```

```
src/Core/
├── Core.csproj               SDK-style net48, AnyCPU
├── Product.cs                literals shared by every consumer
├── InstallPaths.cs           C:\Program Files\PLC-Framework\ and %LOCALAPPDATA%\...\ per user
├── Config/
│   ├── ConfigLoader.cs       config.json → Config, and why it could not be read
│   ├── ConfigPaths.cs        the .plc-framework\ literals both sides must agree on
│   ├── Model/                the DTOs — permissive on purpose, they enforce nothing
│   └── Validation/           one validator per concern, plus the two composites
│       ├── ValidationIssue.cs / ValidationResult.cs / Issues.cs
│       ├── MetadataValidator.cs · RepositoryValidator.cs
│       ├── HierarchyValidator.cs · CodingStyleValidator.cs
│       ├── ConfigValidator.cs        structural, whole document
│       └── EnvironmentValidator.cs   paths that exist, ${VAR} that resolve
├── Checks/                   the coding-style check, pure: handed names, returns rows
│   ├── CodingStyleChecker.cs names against the rules, with a match timeout
│   ├── SimaticMlInterface.cs an exported interface → members, each with its own name and its parents
│   ├── CheckedObject.cs · CheckedMembers.cs · CheckRow.cs · ObjectFamily.cs
│   └── StyleReport.cs        the report document the Add-In sends and the satellite reads
├── Exports/
│   └── ExportTree.cs         where an exported object goes: exports\ shaped like the project
├── Repo/                     the core a project is built on, and the workspace for it
│   ├── RepoPaths.cs          what lives inside .plc-framework\repo\
│   ├── Local/                a core that is a folder on this machine
│   │   ├── LocalSource.cs    where it is, resolved to paths
│   │   ├── LocalCopy.cs      mirrored into the project, removing what it no longer has
│   │   └── LocalCopyResult.cs   how many files, how many removed, what would not copy
│   ├── Remote/               a core that is a repository this machine has not got
│   │   ├── IRemoteCore.cs    the port: open it, list a folder, read one file
│   │   ├── RemoteCopy.cs     the same mirror over a wire, fetching only what changed
│   │   ├── RemoteCopyResult.cs  downloaded, kept, removed, and the commit it now holds
│   │   └── CoreOrigin.cs     which commit the copy is — repo\core.origin.json
│   ├── GitHub/
│   │   └── GitBlobSha.cs     git's own content hash, so the copy is its own index
│   ├── CoreCatalog.cs        a core that has been read: the graph, plus lookup by id and base
│   ├── CoreCatalogLoader.cs  core.json → CoreCatalog, and why it could not be read
│   ├── CoreValidator.cs      what is wrong with one that loaded — as ValidationIssues
│   ├── BlockMetadata.cs      the JSON a core block carries on its TITLE line
│   ├── SimaticMlTitle.cs     that title read back out of an export — the only way in V17-V20
│   ├── ProjectSurvey.cs      what a PLC holds, counted per kind and per language inside it
│   ├── MapFilter.cs          which kinds to map, and which languages inside each
│   ├── ProjectMap.cs         what the TIA project holds — repo\project.json
│   ├── ProjectMapFile.cs     that map, written indented and read back
│   ├── CoreRefresh.cs        config → repository → the copy → core.json, in one call
│   ├── CoreComparison.cs     that core held against that map — metadata only, nothing written
│   ├── DownloadPlan.cs       what a download would touch, and who else depends on it
│   ├── ImportReport.cs       what it actually did, object by object
│   ├── ConstantsWorkbook.cs  the core's .xlsx read back as objects, because Openness will not
│   └── SyncPlan.cs           what is in the wrong folder, where it belongs, and how a move went
├── Places.cs                 "*" for the general program, spelled once for every reader
├── Secrets/
│   ├── DotEnv.cs             the per-user .env: read, and written back surgically
│   └── Variables.cs          ${NAME} references — pure, given a lookup
└── DependencyGraph/          core.json model (DependencyGraph, Node, Edge, Report)
```

```
src/AddIn.Shared/
├── AddIn.Shared.csproj       SDK-style net48, AnyCPU, references Core, embeds assets\
├── Assets.cs                 embedded assets → Stream, no image type
├── Actions/                  use cases: AboutAction, ConfigEditorAction,
│                             DataBlockSnapshotAction, CreateProjectHierarchyAction,
│                             CheckCodingStyleAction, OpenProjectFolderAction,
│                             ExportObjectsAction, Selection (how a selection is worded),
│                             and HelloWorldAction, kept but no longer wired into the menu
│                             Handoff.cs holds the payload types — public and top level,
│                             because partial trust refuses to serialize anything else
└── Adapters/                 ports: ITiaNotifier, IGroupNode, IProcessLauncher, ITiaBusy
                              (TIA's own busy state, with cancellation), HierarchyTargets,
                              PlcSelection, ExportScratch (one run's tmp\ folder, given back
                              when the run ends), ExportItem + ExportOutcome (an object that
                              writes every format it has into a folder, and how many came out),
                              plus Icons (assets → System.Drawing.Icon)
```

```
src/UI.Shared/
├── UI.Shared.csproj          SDK-style net48, UseWPF, references Core
├── SingleInstance.cs         named Mutex + bring the running window to the front; PathKey hashes a path into a name
├── Controls/
│   ├── BrandHeader.xaml      logo, title, subtitle and an optional badge — one shape for all
│   └── StatusBar.xaml        the line along the bottom — there even when there is no status
└── Resources/
    ├── BrandLogo.xaml        favicon.svg converted to a vector DrawingImage
    ├── Theme.xaml            brand colours, dark surfaces and ink — every brush named
    └── Controls.xaml         the dark-theme control styles; merges Theme.xaml itself
```

> **Merging `Controls.xaml` is enough.** It pulls `Theme.xaml` in, so a consumer cannot get the order wrong or take half of it. Listing both would just load the palette twice. `Satellite.About` merges `Theme.xaml` alone, because it has no form.

> **Where a consumer merges them decides whether the XAML designer works.** Four satellites do it in `App.xaml`, which is also where Visual Studio reads the design-time `Application.Resources` from. `Satellite.CoreUpdater` has no `App.xaml` — that is what lets one library serve two executables — so its window merges them in its own `Window.Resources`, exactly as `BrandHeader.xaml` does one level down. Merging into `Application.Resources` from code works at run time and leaves the designer resolving nothing: every `{StaticResource}` comes back as `XDG-0001` on a window that compiles and runs, because WPF resolves them at load time rather than at compile time. Two consequences: a property on the `Window` **start tag** has to become a property element below `Window.Resources`, since an attribute is set before the parser reaches the dictionary; and the designer needs `UI.Shared` **built**, because it resolves `pack://application:,,,/PLC-Framework.UI.Shared;component/…` out of the compiled assembly.

```
src/Satellite.ConfigEditor/
├── Satellite.ConfigEditor.csproj   references Core, UI.Shared; Newtonsoft.Json
├── Resources/
│   └── config.template.json  embedded; a copy is written to the per-user folder on use
├── Startup/
│   └── ParentProcess.cs      which TIA launched this, read from the parent process
├── Handoff/                  EditorRequest + the stdin reader
├── Document/
│   ├── ConfigDocument.cs     the JSON tree, edited in place and validated through Core
│   └── ConfigTemplate.cs     the per-user template, with the embedded one behind it
├── App.xaml(.cs)             one instance per TIA Portal
└── MainWindow.xaml(.cs)      section navigation; Metadata and Repository so far
```

```
src/Satellite.CodingStyleReport/
├── Satellite.CodingStyleReport.csproj  references Core, UI.Shared; DocumentFormat.OpenXml
├── Handoff/HandoffReader.cs  stdin, or a path as the argument; parsed by Core's StyleReport
├── Report/
│   ├── ReportLine.cs         one report row as the table shows it
│   └── Outcomes.cs           an outcome's words, shared by the table and the workbook
├── Export/
│   ├── ReportWorkbook.cs     the report as .xlsx and back: Report, Rules and Info sheets
│   └── ReportFile.cs         file name, " (n)" on a collision, the folder checked first
├── App.xaml(.cs)             no instance guard; a .xlsx argument is imported, not read as a handoff
└── MainWindow.xaml(.cs)      the table, filters, export; import only when TIA sent nothing
```

```
src/Satellite.About/
├── Satellite.About.csproj    WinExe, UseWPF, ApplicationIcon from assets\
├── App.xaml                  merges the UI.Shared dictionaries by pack URI
├── App.xaml.cs               claims the single-instance mutex, then shows the window
├── MainWindow.xaml           logo, Product.Title and the tagline
└── MainWindow.xaml.cs
```

> `App.xaml` carries **no `StartupUri`**. The window is created in `OnStartup` instead, after the single-instance check — leaving `StartupUri` in place makes WPF create a *second* window once `OnStartup` returns, which looks exactly like a broken guard and is not.

Both version projects have the same shape:

```
src/AddIn.VXX/
├── AddIn.VXX.csproj          SDK-style net48 x64, Siemens references + Publisher target
├── AddInProvider.cs          ProjectTreeAddInProvider — entry point
├── AddInController.cs        ContextMenuAddIn — menu wiring
├── Adapters/
│   ├── TiaNotifier.cs        ITiaNotifier against this version's message box
│   ├── TiaGroupNode.cs       IGroupNode over this version's group compositions
│   ├── TiaBusy.cs            ITiaBusy over ExclusiveAccess: text, and a Cancel that is obeyed
│   ├── ProcessLauncher.cs    IProcessLauncher over Siemens' Process wrapper
│   ├── TiaProjectPlaces.cs   where a PLC is, and where an object sits inside one
│   ├── TiaCheckedObjects.cs  the walk the coding-style check reads
│   └── TiaExportObjects.cs   the walk the export writes
└── Config.xml                PackageConfiguration for the Publisher
```

### Naming convention

| Item | `Core` | `AddIn.Shared` | `AddIn.V20` | `AddIn.V21` |
| --- | --- | --- | --- | --- |
| Project / folder | `Core` | `AddIn.Shared` | `AddIn.V20` | `AddIn.V21` |
| `AssemblyName` | `PLC-Framework.Core` | `PLC-Framework.AddIn.Shared` | `PLC-Framework.V20` | `PLC-Framework.V21` |
| `RootNamespace` | `Core` | `AddIn.Shared` | `AddIn` | `AddIn` |

PascalCase throughout, TIA version suffixes included. Hyphens are legal in an `AssemblyName` and illegal in a C# namespace, which is why only the assembly carries the `PLC-Framework.` prefix.

The namespace tells you the layer: **`AddIn.Shared.*` is version-agnostic, `AddIn.*` is version-specific**. Both version projects sharing the `AddIn` namespace causes no collision — they are separate assemblies that nothing references together, and TIA loads one or the other.

> **Do not create a namespace called `AddIn.Core`.** Inside `namespace AddIn`, the identifier `Core` would then resolve to `AddIn.Core` before the global `Core` namespace, so `Core.SomeType` stops compiling and the failure reads as a missing type.

## Architecture: three layers

A type's layer is decided by **what it depends on**, not by who calls it today.

```
Core  ←  AddIn.Shared  ←  AddIn.V20 / AddIn.V21  ←  TIA Portal
 │            │                    │
 │            │                    └── Adapters/  implement the ports against one TIA version
 │            └── use cases (Actions/), the ports they need, and the embedded assets\
 └── model, config loader, install paths — what the satellites will also use
```

| Layer | May depend on | Actual references |
| --- | --- | --- |
| `Core` | only what**every** consumer needs | `mscorlib`, `System`, `System.Core`, `System.Runtime.Serialization`, `System.Xml`, `System.Xml.Linq`, `System.IO.Compression(.FileSystem)` |
| `AddIn.Shared` | `Core` + host types that are **not** Siemens | `+ PLC-Framework.Core`, `System.Drawing` |
| `UI.Shared` | `Core` + WPF | `mscorlib`, `System` — see below |
| `S7PlcWebserverApi` | the network, and nothing of ours | `Newtonsoft.Json`, `System.Net.Http` |
| `GitHubApi` | the network, and nothing of ours | `Newtonsoft.Json`, `System.Net.Http` |
| `AddIn.VXX` | anything, Siemens included | `+ Siemens.Engineering.AddIn` |
| `Satellite.<Name>` / `Tool.<Name>` | `Core`, plus `UI.Shared` when it has a window |  |
| `Satellite.<Name>.VXX` | the above **plus Openness**, as a *client* | `+ Siemens.Engineering` (V20) / `.Base` (V21) |

**That last row was added on 2026-09-16 and it is a real widening, not a clarification.** Until then no satellite touched Siemens at all, and the rule read as though none ever would. `Satellite.CoreUpdater` has to: it reads a PLC's program and will write to it, and handing that much through a JSON payload would be an Add-In doing the work with a window watching. Two things keep the widening narrow. **Only the `.VXX` executables may reference Openness** — the satellite's own project is a library that must not, which is checkable from the metadata rather than by intention. And **an Openness client runs in full trust**, so the constraints that shape the Add-In half — `Assembly.Location` throwing, serialization needing public types, the Publisher refusing a field that holds an engineering object — simply do not apply on this side. That is the point of moving the work here.

That third column is read from the compiled assemblies, not from the `using` statements — it is the only check that cannot drift.

**And it does surprise.** `UI.Shared` emits **no reference to `Core`** despite declaring one in its `.csproj`, and none to WPF either. The only thing it takes from `Core` is `Product.Title`, a `const string`, and a constant is **inlined at compile time** — the dependency vanishes from the metadata while the project reference stays necessary for the compiler and `Core.dll` stays necessary next to the executable. Its WPF content is XAML, compiled to BAML resources rather than to code, and `SingleInstance` is a `Mutex` plus two `DllImport`s. Neither is a defect to fix; both are the reason to read metadata instead of `using` lines.

**Shared projects are named after the concern, not the consumer.** `UI.Shared` holds what anything with a window needs — the logo, the palette — regardless of whether that thing is a satellite or a command-line tool that shows a dialog. Calling it `Satellite.Shared` would have broken the rule above in the name itself. `AddIn.Shared` keeps a consumer-shaped name because its contents genuinely are Add-In vocabulary: a TIA notification, a PLC group tree.

A useful consequence: **if a project references `UI.Shared`, it has a GUI.** The dependency states what a name prefix only suggests.

**`System.IO.Compression` reaches `Core` and `DocumentFormat.OpenXml` still may not**, which is the same rule rather than an exception to it. `Core.Repo.ConstantsWorkbook` reads the core's `.xlsx` enumerations because Openness refuses to import one, and a `.xlsx` is a zip holding two XML documents — both of which TIA Portal's own process already has, since `Core` is loaded into it. The SDK is a package, and a package is what must not travel there. Writing a workbook would be a different matter entirely, which is why the exports keep the SDK: five XML parts whose only real test is whether Excel opens the file.

**Why `System.Drawing` must not reach `Core`.** `Core` is loaded **inside TIA Portal's process**, and its dependencies are the *intersection* of what its consumers need, never the union. `Icons` turns an asset into a `System.Drawing.Icon` and a WPF consumer would want an `ImageSource` from the same bytes; putting either materialiser in `Core` would eventually drag `PresentationCore` and `WindowsBase` in behind the other. With `assets\` in `AddIn.Shared` nothing in `Core` pulls that way at all.

**Ports live with the layer whose vocabulary they speak.** `ITiaNotifier` and `IGroupNode` carry no Siemens reference at all, yet they belong to `AddIn.Shared`: one is shaped like a TIA notification, the other talks about PLC group trees. Neither is vocabulary a satellite would use.

**The adapter converts Siemens types before crossing a layer.** `DeviceItem` never leaves the version project — the action receives an `IGroupNode` tree instead:

```csharp
DeviceItem deviceItem = menuSelectionProvider?.GetSelection<DeviceItem>().FirstOrDefault();

CreateProjectHierarchyAction.Execute(
    _notifier,
    result.Config.ProjectConfig?.Hierarchy,
    TiaGroupNode.TargetsFor(deviceItem));
```

The payoff is measurable: the two `TiaNotifier.cs` files differ in exactly one line — the one documented under [*Known V20 / V21 divergences*](openness-notes.md) — and everything else either is identical or lives one layer down.

Adapters are `internal sealed`: TIA only instantiates `AddInProvider` and `AddInController` by reflection, so only those two need to be `public`. Types crossing an assembly boundary (`Icons`, the ports, the actions) are `public` because they must be.

The `AssemblyName` **must match** the `<Assembly>` element in `Config.xml` (`PLC-Framework.V20.dll` / `PLC-Framework.V21.dll`). What TIA displays in the menu does not depend on it: that comes from the `string` passed to the `ContextMenuAddIn` constructor and from `<Product><Name>` in `Config.xml`.

## Assets and icons

Binary assets live once, at the repo root, grouped **by feature** rather than by extension, so a feature can keep its `.ico`, `.svg` and anything else together:

```
assets/
├── AddIn/                    icons for the Add-In's context-menu entries
│   ├── coding-style.ico
│   ├── folder-hierarchy.ico
│   └── github.ico
└── Brand/
    ├── favicon.ico
    └── favicon.svg
```

They are **embedded into `AddIn.Shared`** by a glob, so adding an asset is dropping a file — no `.csproj` edit anywhere:

```xml
<!-- AddIn.Shared.csproj -->
<EmbeddedResource Include="..\..\assets\**\*.*"
                  Link="Assets\%(RecursiveDir)%(Filename)%(Extension)" />
```

**`EmbeddedResource`, never `Content`.** The `.addin` package only carries assemblies, so a loose file would never reach TIA Portal. Embedded, the asset travels inside `PLC-Framework.AddIn.Shared.dll`, which is already listed under `AdditionalAssemblies` — so nothing extra to declare.

### Why they live in `AddIn.Shared`, and why the loader returns a `Stream`

Embedded resources are **scoped to the assembly that carries them**: `Assembly.GetManifestResourceNames()` only ever sees its own, and `Assets` resolves against `typeof(Assets).Assembly`. **The loader and the assets cannot be separated** — move one without the other and every lookup returns `null`, silently, because `Open` returns `null` by design so callers can degrade.

They sat in `Core` first, and moved on 2026-09-04 **because the transformation only ever serves the Add-Ins**. `Core` was the right home only under a prediction — that a WPF satellite would build an `ImageSource` from the same `.ico` stream — and the first real satellite disproved it. `Satellite.About` uses the brand asset twice and reaches the loader neither time: the window and `Window.Icon` take `BrandLogo` from `UI.Shared` as a vector, and the executable's icon is an `ApplicationIcon` resolved at build time.

That is structural rather than accidental. Turning a `.ico` into an image at runtime is TIA menu vocabulary, because `AddActionItemWithIcon` takes a `System.Drawing.Icon`; a WPF app wants a vector resource or a Win32 one. Moving the assets back would take a windowed app that genuinely reads an arbitrary asset at runtime — and even then a XAML resource in `UI.Shared` is the likelier answer. The glob moves with them either way.

What must **not** be shared is the type:

```csharp
public static Stream Open(string path)     // AddIn.Shared.Assets
```

| Host | Materialises it as |
| --- | --- |
| `AddIn.V20` / `AddIn.V21` | `new Icon(stream)` → `System.Drawing.Icon`, what the TIA menu API takes |
| a WPF consumer | `IconBitmapDecoder(stream, …).Frames[0]` → `ImageSource` — verified to work, but **nothing does this**: see above |

Both were verified against the same embedded `favicon.ico`, which is why the loader returns a `Stream` and not an `Icon` — that is what keeps `System.Drawing` out of it. The second row survives as evidence that the split is sound, not as a path in use; a windowed app reaches for `UI.Shared` instead.

### The executables' icon does not go through the loader

`<ApplicationIcon>` is what Explorer, Alt-Tab and shortcuts show, and it is a **Win32 resource generated at build time**, so it needs a file on disk — an embedded resource arrives far too late. It therefore reads `assets\` directly:

```xml
<!-- Satellite.About.csproj -->
<ApplicationIcon>..\..\assets\Brand\favicon.ico</ApplicationIcon>
```

Same single file, different path to it. It coexists with `Window.Icon`, which is set in XAML from `BrandLogo` and is what the window and the taskbar show; drop that and WPF falls back to this one. One line per executable — `favicon.ico` carries frames from 16 to 128, so only Explorer's 256-pixel view has to upscale.

The action names the asset next to the behaviour that uses it, and the materialiser resolves it:

```csharp
// AddIn.Shared.Actions
public const string IconPath = "Brand/favicon.ico";
```

```csharp
// AddIn.Shared.Adapters — Icons.Get(path) → System.Drawing.Icon, with a cache
```

### Two traps

**Never hardcode the resource-name prefix.** MSBuild derives names as `<RootNamespace>.Assets.<Feature>.<file>`, e.g. `AddIn.Shared.Assets.Brand.favicon.ico`. A literal prefix does not fail at compile time when it stops matching — `GetManifestResourceStream` just returns `null` and the icon blows up at runtime inside TIA. Match on the **tail** instead, so renaming the root namespace or the link path cannot break it.

**Cache the icons.** `GetContextMenuAddIns()` returns a **new `AddInController` on every right-click**, so loading icons in the constructor re-reads every asset each time the menu opens. A static cache (misses included) fixes it.

Menu entries fall back to `AddActionItem` when `Icons.Get` returns `null`, so a missing asset costs an icon rather than the whole menu.

### The five locations, at a glance

Everything the framework writes or reads on a station lands in one of five places. The rule that decides which is short, and it is Windows' own: **`Program Files` is what the installer puts there; `%LOCALAPPDATA%` is what the applications write.** Getting that backwards is the failure that passes every test on a developer's machine — where you are an administrator — and fails at a customer's, where you are not.

| Location | Scope | In code | Elevation |
| --- | --- | --- | --- |
| `C:\Program Files\PLC-Framework\` | per**machine** | `Core.InstallPaths.Root` | once, to install |
| `%LOCALAPPDATA%\PLC-Framework\` | per**user** | `Core.InstallPaths.UserRoot` | none |
| `%AppData%\Siemens\Automation\Portal V2x\UserAddIns\` | per user, per TIA version | — Siemens' own | none |
| `C:\Program Files\Siemens\Automation\Portal V2x\AddIns\` | per**machine**, per TIA version | — Siemens' own | **yes** |
| `<TIA project>\.plc-framework\` | per**project** | `Core.Config.ConfigPaths` | none |

That is five rows for more than five places, because the two Add-In rows are one row each for two TIA versions — V20 and V21 never share a folder.

> **`%ProgramData%` was the first answer, and it was wrong** (corrected 2026-09-11). The reason on record — *"`%ProgramData%` grants ordinary users read and execute but not write"* — is simply false: its `Users` ACE carries `Write` with `ContainerInherit`, so every subfolder inherits it. Measured on a stock Windows 11: a standard user creates a folder there, and a file inside it, with no elevation at all. **A directory anyone can write to and everyone executes from is how a planted DLL gets loaded**, which is the reason binaries live in `Program Files` — where `Users` really do get read and execute and nothing more. Application whitelisting agrees: default AppLocker rules permit execution from `Program Files` and `Windows` and deny it elsewhere for standard users, so satellites under `%ProgramData%` would refuse to start on a locked-down station. The move cost nothing, because step 2 already had to run elevated for V20's Add-In folder.
>
> The decisions that had been built on the false premise — the `.env` and `config.template.json` moving to `%LOCALAPPDATA%` — still stand, on the corrected reason: a user may *create* a file under `%ProgramData%` but not modify one written by the installer or by another engineer, the folder is shared so two engineers overwrite each other, and the token is personal.

**The last two are the same choice as the first two, made by Siemens instead of by us**: one folder per user that anybody can write, one per machine that only an administrator can. **Which one a version uses is a property of that TIA installation, not of the project** — on the VM, V20 is machine-wide and V21 per-user — so `step2-deploy-vm.ps1` records the scope per version rather than per run. The package goes in exactly one of the two, and the script reports finding it in the other, because **TIA reads both and would load it twice**.

`PLC_FRAMEWORK_HOME` replaces the first row entirely, which is how a test run or the VM points at a staging folder without installing anything.

**`C:\Program Files\PLC-Framework\`** — what was installed

|  |  |
| --- | --- |
| `*.exe`, `*.dll` | `InstallPaths.Root`, flat. **Every** executable shipped — satellites and command-line helpers alike — each with its DLLs and `.exe.config`. Copied in as a whole build output, not as a lone `.exe` |

**No `tools\` subfolder.** It existed while the root was `%ProgramData%\PLC-Framework\` and might have shared space with data; under `Program Files` the folder holds nothing but binaries, so a level named after them said nothing the folder did not already say. `C:\Program Files\<Product>\app.exe` is the ordinary shape of an installed application.

**`%LOCALAPPDATA%\PLC-Framework\`** — what the applications write

|  | Written by |  |
| --- | --- | --- |
| `credentials.json` | `Satellite.DataBlockSnapshot`, only after a login the CPU accepted | web server user and password per project + PLC; the password under DPAPI `CurrentUser` |
| `.env` | `Satellite.ConfigEditor` | `GITHUB_TOKEN`. Here because the token is personal **and** because the install folder is not writable by the engineer who owns it |
| `config.template.json` | `Satellite.ConfigEditor`, when creating from the system template and no such file exists | the user template: offered beside the system one when a project has no `config.json`, written out from the embedded copy so it can be customised, and never overwritten |

**`...\Portal V2x\UserAddIns\`** or **`...\Portal V2x\AddIns\`** — one or the other, never both

|  |  |
| --- | --- |
| `PLC-Framework.V20.addin` | carries `PLC-Framework.V20.dll` plus `Core.dll` and `AddIn.Shared.dll` |
| `PLC-Framework.V21.addin` | the same, with `PLC-Framework.V21.dll` |

**`<TIA project>\.plc-framework\`** — what belongs to the project

|  | Written by | Versioned |
| --- | --- | --- |
| `config.json` | the user by hand, and `Satellite.ConfigEditor` | **yes** |
| `config.schema.json` | `Satellite.ConfigEditor` | **yes** — `config.json` points at it relatively |
| `.gitignore` | `Satellite.ConfigEditor`, only when absent | **yes** |
| `exports\` | `Satellite.DataBlockSnapshot` → `<ip>-<DB>-snapshot-<timestamp>.xlsx`, and the Add-In's export → a tree shaped like the project's, `<PLC>\Program blocks\03-ALL\_oc_seq2.xml` and every other format that block has beside it | no |
| `logs\` | what a run recorded about itself | no |
| `repo\` | the core update → `core\` the core as copied out of the repository, `project.json` what the TIA project holds, `tmp\` what a download is writing | no |
| `tmp\` | the coding-style check, while it reads an interface → `coding-style-<timestamp>\`, deleted when the run ends | no |
| `reports\` | `Satellite.CodingStyleReport`, on export → `<project>-coding-style-<timestamp>.xlsx` | no |

The five folder names live in `Core.Config.ConfigPaths` alongside `Folder` and `File`, and `ConfigPaths.FolderFor(projectDirectory, name)` resolves one; what goes *inside* `repo\` is `Core.Repo.RepoPaths`, because that folder has a layout of its own rather than being a place to drop files. **One of them has no writer yet, which is exactly when two components drift apart on a string** — the same reason the other literals are there. Nothing creates them: whoever writes the first file creates it then, so a project that never ran an action does not collect four empty folders, and git would not record them anyway.

**Not one secret lives here, and that is deliberate**: this folder is under version control, with `.version-control\` sitting right beside it. It is why the PLC credentials live under `%LOCALAPPDATA%` and why `config.json` carries the literal `${GITHUB_TOKEN}` rather than the token.

#### The `.gitignore` the editor leaves behind

Being under version control cuts both ways: no secrets may live here, and most of what the framework *writes* here must not be committed either. `Satellite.ConfigEditor` therefore drops a `.gitignore` in the folder, and it **ignores everything and re-admits what must be versioned** rather than listing what to skip:

```gitignore
*

!.gitignore
!config.json
!config.schema.json
```

- **A deny-list only knows today's folders.** `exports\`, `logs\`, `tmp\`, `reports\` — the next generated thing the framework writes would be committed by default, and nobody would notice until it was already in the history. For a folder whose purpose is generated output, that default is backwards. `reports\` proved the point before the ink was dry: it was named after this file was written, and needed no change to it.
- **The stake is higher than tidiness.** `exports\` holds workbooks of values read out of a live CPU: plant configuration, sitting one folder away from `.version-control\`. Two lines of allow-list are a cheap way never to have that conversation.
- **`config.schema.json` is re-admitted deliberately, and it is not optional.** `config.json` points at it by relative path, so a clone without it validates nothing and says nothing about why — the exact failure that ruled out referencing the schema by URL.
- **Written only when absent**, unlike the schema beside it. A team may add a line for their own tooling, and rewriting it every save would be the editor overruling them. The schema is derived and therefore ours to replace; this is a starting point and therefore theirs.

Checked against real `git` in a real repository: `git add .plc-framework` stages exactly those three files, while a snapshot workbook, a log, a `tmp\` file, the coding-style report and an invented file that does not exist yet are all ignored — that last one being the point of the allow-list. And across a second save, a hand-added line survives while a hand-edited `config.schema.json` is restored.

### Where the satellites live

```
C:\Program Files\PLC-Framework\      ← Core.InstallPaths.Root
├── PLC-Framework.Satellite.About.exe
├── PLC-Framework.Core.dll
└── ...                              every executable shipped, and its DLLs
```

**Per machine, not per user**: one install serves every engineer who logs into the station, and both the V20 and V21 Add-Ins resolve the same path. Creating it needs administrator rights once; reading it afterwards does not — `Program Files` grants `BUILTIN\Users` read and execute by default, and nothing more, which is the point.

**One flat folder for every executable.** A split between `satellites\` and `tools\` was tried and reverted: the boundary blurred immediately, and it only raised the question of which half a new executable belonged in. The surviving `tools\` level went too when the root moved to `Program Files` — a folder holding nothing but binaries does not need a subfolder named after them, and `C:\Program Files\<Product>\app.exe` is the ordinary shape of an installed application.

> **"Satellite" is a role, not a location.** It names a WPF app the Add-In launches from the menu — as opposed to a command-line helper. Both live in the same folder. The word stays in project names (`Satellite.About`) and in the architecture decisions, because it describes what a thing *is*; the folder only says where it sits.

> **The `.env` moved out of here** on 2026-09-09, to `%LOCALAPPDATA%\PLC-Framework\.env`. This section used to say a station-wide `.env` was right for a shared team credential, "and a personal token would belong somewhere per-user instead". The only thing in it turned out to be `GITHUB_TOKEN`, which is exactly that personal token, and the install folder is not writable by the engineer who owns it. `InstallPaths.EnvFile` now hangs off `UserRoot`. The executables stay: an installed binary genuinely is per machine.

`PLC_FRAMEWORK_HOME` overrides the root, which is how you point a test run — or the VM — at a staging folder without installing or needing elevation. **`step2-deploy-vm.ps1` honours it too**, by the same two rules `InstallPaths.Root` uses; an installer and an Add-In that disagreed about where the framework lives would produce a "not installed" error on an install that looks perfectly fine.

Not next to the `.addin`. `UserAddIns` is **per TIA version**, so V20 and V21 would each need their own copy of a satellite that is really per machine, and it is Siemens' directory rather than ours. Not derived from `Assembly.Location` either: TIA loads the Add-In out of the package, so that path cannot be relied on.

**The path is read from `ProgramW6432`, falling back to `SpecialFolder.ProgramFiles`** — not the other way round, and the reason is WOW64. `Core` is loaded into TIA Portal, which is x64, *and* into the satellites, which are AnyCPU. On a 32-bit host `SpecialFolder.ProgramFiles` answers `Program Files (x86)`, so the two halves of the framework would resolve **different installations** with nothing failing visibly. `ProgramW6432` is set by 64-bit Windows for processes of either bitness, and is absent on 32-bit Windows where the fallback is right. Checked: the satellites build as `ILOnly` with no `Preferred32Bit`, so today both halves agree — this closes the trap rather than fixes a live bug.

No Siemens API offers an alternative: neither `TiaPortal` nor the `Siemens.Engineering.AddIn` namespace exposes the Add-In's own path or the running TIA version.

### Theming a WPF field: Setters cannot reach hover and focus

**These styles live in `UI.Shared/Resources/Controls.xaml`.** They were written inside `Satellite.DataBlockSnapshot`'s own window and moved out on 2026-09-09, when `Satellite.ConfigEditor` turned out to need the same ~290 lines — which would have been the third copy of the palette and the second of the templates. Almost everything in there is an **implicit** style, so a plain `<TextBox/>` is already themed; only the choices a window has to make are keyed (`Quiet` / `Primary` for a button, `FieldLabel`, `Reveal`).

Two things changed in the move, both deliberate:

- **Every colour got a name.** `#454E5B`, `#6C7686`, `#39424F` and three more were literals inside triggers. That is exactly how a "disabled grey" becomes three slightly different greys, so they are now `FieldEdgeDisabled`, `InkDisabled`, `RowHover` and so on in `Theme.xaml`.
- **A button that removes something uses `Destructive`**, which is `Quiet` at rest and red under the pointer — so the click announces itself before it happens, and does so the same way wherever a remove button appears. It could not be derived from `Quiet` with `BasedOn`: the state that differs is a trigger *inside* the template, and `BasedOn` replaces the whole template or nothing.
- **Selectable rows are templated too, and for a reason that is easy to miss.** `ListBoxItem` and `TreeViewItem` paint their selected background from the **system** brushes: blue while focused, and a **pale box when not**, which on a dark form looks like a rendering fault rather than a selection — a first row that appears highlighted before anything has been clicked. Both now use the brand colour in either state, through a `MultiTrigger` on `IsSelectionActive`. Note `ListViewItem` derives from `ListBoxItem` but does **not** inherit its implicit style, because an implicit style matches the exact type.
- **The eye lost its tooltip.** It used to flip between "Show the password" and "Hide the password", which cannot survive a style that also serves a GitHub token — and the alternative, a wording vague enough for both, tells nobody anything. The window names its own field, and the struck-through eye already shows the state.

**The move was verified by pixel comparison, not by eye**, and the method is worth repeating because the naive version lies. Capturing with `CopyFromScreen` photographs whatever is on top at those coordinates — `SetForegroundWindow` is blocked when the caller does not already have focus — so it can silently capture another application. `PrintWindow` with `PW_RENDERFULLCONTENT` asks the window to draw itself, covered or not.

Even then the first comparison read 12 % of pixels different. The difference map showed every fill identical and only glyph and border **outlines** changed: a sub-pixel shift, because ClearType renders against the absolute screen position and the window had not centred on exactly the same coordinate. The control that settled it was capturing the *same build twice* — 12 % again — and then comparing the pre-refactor capture against that control run: **0.096 % of pixels, maximum delta 4.** Identical.

**`ListBox`, `ListView` and `TreeView` themselves went unstyled until 2026-09-16 and -17**, and the second of those had its evidence sitting in the repo all along: three windows each wrote `BorderThickness="0" Background="Transparent" Foreground="{StaticResource Ink}"` on every list and tree **by hand**, which is one decision made three times. Only the *rows* had ever been templated, so the fourth window that forgot the incantation got the stock chrome — a **white** background behind rows painted in `Ink`, light grey on white. All three are implicit now, `Transparent` because that is what all three windows had chosen; the inline copies still win and are simply redundant. Note `ListView` derives from `ListBox` and inherits none of its style, the same exact-type rule that forces three separate row styles.

**Scroll bars are themed in `Controls.xaml` too** (2026-09-13), and a `GridView` needed more than a style. Until then every list, tree and text box scrolled with the stock light-grey bar; the coding-style report, whose table scrolls both ways, made it impossible to miss. The implicit `ScrollBar` style — a thin track, a rounded thumb, no arrow buttons — reaches every window at once. **A `ListView` showing a `GridView` does not use the implicit `ScrollViewer`**: it asks for the style keyed `GridView.GridViewScrollViewerStyleKey`, whose stock template paints the corner between the two bars as a square outlined in a **literal** `White`. No resource can reach a literal, so `Controls.xaml` supplies that keyed style — the stock template, corner in the surface colour, with the header row kept in its own hidden-bar `ScrollViewer`, which is what keeps the headers moving with the columns. Verified by capture scrolled fully right, headers over their columns, and on the snapshot's block list, which uses the same template. The implicit `ScrollViewer` redefines `SystemColors.ControlBrushKey` for the ordinary corner, which does read a system brush; `SurfaceColor` exists as a `Color` in `Theme.xaml` for that, since a brush cannot be handed out under a second key.

**A button's size is a setter, not something each window decides** (2026-09-18). `Quiet` — and therefore `Primary` — and `Destructive` carry `MinHeight="28"` and `Padding="10,0"`, and no window sets padding on a button at all. They had been setting their own, and the four of them had drifted through **seven** vertical paddings for one kind of control. **`MinHeight` is what unifies them, not the padding**: padding decides a button's height only until something else does, so the same padding on a glyph and on a long label came out as two heights. **The horizontal padding is deliberately small** — most buttons here carry an explicit `Width`, and padding eats into it, a `✕` in a 32-pixel button having nothing to spare. **A button that stretches to a taller row is correct**: a `...` matching the folder box beside it is the point, and what the check asserts is that two buttons sharing a row never differ.

**A list, a tree or a table sits on `Surface`, painted by the `Border` around it**, with the control itself `Transparent`. That is the report's rows, the snapshot's block list and the core updater's two trees. A container with no background shows the window's gradient through and reads as a panel and a container at once. `Satellite.ConfigEditor` uses `Field` for all four of its containers and is left alone: there it is a system rather than a drift.

**Outcomes have named inks**: `InkGood`, `InkBad`, `InkWarn`. The first two are the values the config editor shows "compiles" and "does not match" in, still as literals in its code-behind.

Worth knowing before the next satellite repeats it. A `Style` full of `Setter`s gets a `TextBox` most of the way onto a dark palette — background, foreground, caret, selection — and then the control **repaints its own border Windows-blue on hover and on focus**, from brushes hardcoded inside the stock template. No `Setter` reaches those two states, because they are template triggers rather than properties.

Replacing the `ControlTemplate` is the only fix, and one template serves every field:

```xml
<ControlTemplate x:Key="FieldChrome" TargetType="Control">
```

`TargetType="Control"` rather than `TextBox` is what lets the `PasswordBox` share it: both derive from `TextBoxBase`, which finds its editing surface by the name `PART_ContentHost`, and every `TemplateBinding` needed is to a property `Control` already declares. The focused field is then marked in the brand colour — the same brush the text selection already uses.

Two traps came out of doing it:

- **An implicit `TextBox` style reaches inside other templates.** An editable `ComboBox` builds its edit area from a real `TextBox` named `PART_EditableTextBox`, so it inherits the implicit style — padding included. Against the combo's fixed height that padding clipped the text. `Padding="0"` on that part fixes it.
- **For the same reason, the disabled trigger must not repaint the background.** That part is transparent by design, and a solid colour there puts a patch inside the drop-down whenever the form is busy. Dimming the border and the text says "disabled" perfectly well.

Verified by driving the real window through UI Automation — focus set per control, the pointer parked off-window so hover could not be confused with focus, and a capture started so the whole form could be seen disabled.
