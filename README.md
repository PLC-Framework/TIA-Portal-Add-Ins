# tia-portal-addins

A **TIA Portal Add-In** (Siemens Openness) plus the **satellite apps** with UI that the Add-In launches on events.

- Solution: `tia-portal-addins.slnx` — the new XML format, not the classic `.sln`
- Target framework: **.NET Framework 4.8** for everything touching TIA Portal / Openness, WPF included

Six projects exist today: `Core`, `AddIn.Shared` and `UI.Shared` as the shared layers, the two Add-Ins `AddIn.V20` and `AddIn.V21`, and the first satellite, `Satellite.About`. Both Add-Ins are validated on the VM against their respective TIA Portal versions. The satellite is built and verified here, by running the real binary; it has **not** yet been launched from inside TIA.

## Working environment: two machines

| Machine | Role | TIA Portal |
|---|---|---|
| Development PC | write code, build, produce the `.addin` | **not** installed, and not needed |
| VM | debugging and testing: load the Add-In in TIA | V20 and V21 installed |

The full cycle up to the `.addin` package closes on the development PC: references resolve against the DLLs in `.lib\`, not against a TIA installation, and the Publisher does not need the product installed either.

The **VM** does require TIA Portal and the user to belong to the local **"Siemens TIA Openness"** group — without it the Add-In will not even show up.

### Requirements

- **.NET SDK** (verified with 10.0.400) and the **.NET Framework 4.8 targeting pack** → `dotnet build` compiles `net48` without Visual Studio.
- For Visual Studio: the **".NET desktop development"** workload plus the **".NET Framework 4.8 targeting pack"** individual component.

> The official Siemens `.vsix` (Add-In project template) is **not used** in this repo: it generates a classic-style project with a single API version hardcoded, and two projects with different references are needed here. A hand-written SDK-style `.csproj` is shorter and more flexible.

## Layout

```
tia-portal-addins.slnx
├── assets/                   shared binary assets, grouped by feature (see below)
├── .siemens/                 reflected reference for the Openness API (see below)
└── src/
    ├── Core/                 (net48, AnyCPU) — model, no host types at all          ← EXISTS
    ├── AddIn.Shared/         (net48, AnyCPU) — the Add-In layer, NO Siemens          ← EXISTS
    ├── UI.Shared/            (net48, WPF) — brand resources, shared windows          ← EXISTS
    ├── S7PlcWebserverApi/    (net48, AnyCPU) — JSON-RPC client for a CPU's webserver ← EXISTS
    ├── AddIn.V20/            (net48, x64) — references PublicAPI\V20.addIn (V17–V20) ← EXISTS
    ├── AddIn.V21/            (net48, x64) — references PublicAPI\V21\net48           ← EXISTS
    ├── Satellite.About/      (net48, WPF) — the About window                         ← EXISTS
    ├── Satellite.DataBlockSnapshot/  (net48, WPF) — captures a DB to .xlsx           ← EXISTS
    ├── Satellite.<Name>/     (net48, WPF) — a UI app the Add-In launches
    └── Tool.<Name>/          (net48) — a command-line helper
```

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
├── InstallPaths.cs           %ProgramData%\PLC-Framework — .env and tools\
├── Config/                   config.json loader + model under Model/
└── DependencyGraph/          core.json model (DependencyGraph, Node, Edge, Report)
```

```
src/AddIn.Shared/
├── AddIn.Shared.csproj       SDK-style net48, AnyCPU, references Core, embeds assets\
├── Assets.cs                 embedded assets → Stream, no image type
├── Actions/                  use cases: AboutAction, CreateProjectHierarchyAction, and
│                             HelloWorldAction, kept but no longer wired into the menu
└── Adapters/                 ports: ITiaNotifier, IGroupNode, IProcessLauncher,
                              HierarchyTargets, plus Icons (assets → System.Drawing.Icon)
```

```
src/UI.Shared/
├── UI.Shared.csproj          SDK-style net48, UseWPF, references Core
├── SingleInstance.cs         named Mutex + bring the running window to the front
└── Resources/
    ├── BrandLogo.xaml        favicon.svg converted to a vector DrawingImage
    └── Theme.xaml            the brand palette as Colors and Brushes
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
│   └── ProcessLauncher.cs    IProcessLauncher over Siemens' Process wrapper
└── Config.xml                PackageConfiguration for the Publisher
```

### Naming convention

| Item | `Core` | `AddIn.Shared` | `AddIn.V20` | `AddIn.V21` |
|---|---|---|---|---|
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
|---|---|---|
| `Core` | only what **every** consumer needs | `mscorlib`, `System`, `System.Core`, `System.Runtime.Serialization` |
| `AddIn.Shared` | `Core` + host types that are **not** Siemens | `+ System.Drawing` |
| `UI.Shared` | `Core` + WPF | WPF only |
| `S7PlcWebserverApi` | the network, and nothing of ours | `System.Net.Http`, `Newtonsoft.Json` |
| `AddIn.VXX` | anything, Siemens included | `+ Siemens.Engineering.AddIn` |
| `Satellite.<Name>` / `Tool.<Name>` | `Core`, plus `UI.Shared` when it has a window | |

That third column is read from the compiled assemblies, not from the `using` statements — it is the only check that cannot drift.

**Shared projects are named after the concern, not the consumer.** `UI.Shared` holds what anything with a window needs — the logo, the palette — regardless of whether that thing is a satellite or a command-line tool that shows a dialog. Calling it `Satellite.Shared` would have broken the rule above in the name itself. `AddIn.Shared` keeps a consumer-shaped name because its contents genuinely are Add-In vocabulary: a TIA notification, a PLC group tree.

A useful consequence: **if a project references `UI.Shared`, it has a GUI.** The dependency states what a name prefix only suggests.

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

The payoff is measurable: the two `TiaNotifier.cs` files differ in exactly one line — the one documented under *Known V20 / V21 divergences* — and everything else either is identical or lives one layer down.

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
|---|---|
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

## Siemens dependencies

The assemblies live **outside the repo**, under `E:\PlcFramework\.lib\Siemens\PublicAPI\`, with **one folder per version**:

| Folder | Contents |
|---|---|
| `V17\`, `V18\`, `V19\`, `V20\` | `Siemens.Engineering.dll`, `Siemens.Engineering.Hmi.dll` |
| `V17.AddIn\`, `V18.AddIn\`, `V19.AddIn\` | `Siemens.Engineering.AddIn.dll`, `.AddIn.Permissions.dll`, `.AddIn.Utilities.dll`, `Siemens.Engineering.Hmi.dll` |
| `V20.addIn\` | the above **plus `Siemens.Engineering.AddIn.Publisher.exe` and `.AddIn.DebugStarter.exe`** (note the lowercase `a` in the folder name) |
| `V21\` | `Siemens.Engineering.AddIn.Publisher.exe` and its `.xsd` |
| `V21\net48\` | the split V21 assemblies |
| `.doc\TIA-Openness\TIA Add-in Tester\` | TIA Add-in Tester v1.1.6557.1192 (entry 109783096) — tests the Add-In without opening TIA Portal |

Other tools, under `E:\PlcFramework\.lib\Siemens\Support\TIA_Portal_Add-In_Tools\`:

| Folder | Contents |
|---|---|
| `Development\` | `.nupkg` + `.vsix` — the official VS template for Add-Ins (TIA V18+) |
| `Trusted_Add-Ins_Certification_Tool\` | `Company_Trusted_Add-In_Certification_Tool.exe` — signs the `.addin` as trusted |

The path is parameterized in the `.csproj` through `$(SiemensPublicApi)`, so it can be overridden without editing the file:

```
dotnet build src\AddIn.V20\AddIn.V20.csproj -p:SiemensPublicApi=D:\some\other\path
```

### The reflected reference in `.siemens\`

Two self-contained HTML pages, generated from the assemblies themselves rather than from documentation, and kept in the repo precisely because the assemblies cannot be:

| File | Covers |
|---|---|
| `tia-v20-object-model.html` | the V20 object model as `Siemens.Engineering.AddIn.dll` declares it — the spine from `TiaPortal` down to a block group, the system/user group pattern, software units, and the Add-In surface |
| `tia-v21-assembly-split.html` | which of the sixteen V21 assemblies declares what, what an Add-In has to reference, and every shape difference found against V20 |

They open in any browser and render their class diagrams from a pinned mermaid build. **Neither contains Siemens code** — only type names, member names and counts read with `GetExportedTypes()`, which is what lets them live in the repo when the DLLs they describe may not.

Both record where Siemens' own published object-model diagram is incomplete: it omits `ProjectBase` and `HardwareObject` entirely, and attributes their properties to `Project` and `Device` instead.

### V17–V20 vs V21

V17 through V20 share the same Openness API and are binary compatible. **V21 introduces breaking changes**: up to V20 the API is monolithic, whereas V21 splits it into **sixteen assemblies**. Read from `V21\net48\` with `GetExportedTypes()`:

| Assembly | Types | Carries |
|---|---:|---|
| `Base` | 1,382 | the whole object model: `TiaPortal`, `Project`, `Device`, `DeviceItem`, `Software`, `IEngineeringObject`, `NotificationIcon`, `ExclusiveAccess` |
| `WinCCUnified` | 536 | Unified HMI, including `HmiSoftware` |
| `Step7` | 228 | everything under `SW.*` — `PlcSoftware`, blocks, types, tags, software units |
| `AddIn.Base` | 71 | Add-In infrastructure only: providers, menus, `MessageBoxProvider` |
| `DCC` | 66 | drive control charts |
| `WinCC` | 65 | classic HMI, including `HmiTarget` |
| `Startdrive` | 64 | drive commissioning |
| `TeamcenterGateway` | 20 | PLM integration |
| `SafetyValidation` | 19 | safety validation reports |
| `Safety` | 18 | F-programs |
| `AddIn.Step7` | 10 | Add-In hooks specific to STEP 7 |
| `AddIn.Safety` | 6 | Add-In hooks specific to safety |
| `WinCC.Extension` | 4 | HMI extension points |
| `AddIn.Utilities` | 2 | `Process` and `ProcessStartInfo` |
| `AddIn.Permissions` | 2 | the permission attributes |
| `CFC` | 2 | continuous function charts |

**2,495 public types and not one name collision** — every full type name appears in exactly one assembly, which is the structural reason V21 never needs an `extern alias`. All sixteen are version `21.0.0.0` with public key token `29bfe5fdf4ba5d3b`.

**There is no `Siemens.Engineering.dll` in V21.** Hence the need for two separate projects.

## Building

```
dotnet build src\AddIn.V20\AddIn.V20.csproj
```

Output lands in `bin\Debug\net48\`:

- `PLC-Framework.V20.dll` — the compiled Add-In
- `PLC-Framework.Core.dll` — copied in by the `ProjectReference` (Siemens references are `Private=False`; project references are not — this one *must* be copied)
- `PLC-Framework.V20.addin` — the package TIA Portal actually loads, generated automatically, containing **both** DLLs
- `Config.xml` — the copy the Publisher consumes (see below)

### References

All references carry `<Private>False</Private>` because TIA resolves them from its own installation at runtime — copying them next to the Add-In breaks loading. **`PlatformTarget` must be `x64`**: TIA Portal is 64-bit only and the Add-In loads inside its process.

**The reference model is inverted between V20 and V21.** This is the single most disorienting difference between the two projects:

| | V20 | V21 |
|---|---|---|
| Add-In assembly | `Siemens.Engineering.AddIn.dll` — **2269 types**, carries the object model embedded | `Siemens.Engineering.AddIn.Base.dll` — **71 types**, Add-In infrastructure only |
| Object model (`TiaPortal`, `Project`, `IEngineeringObject`, `NotificationIcon`, `HW.*`) | inside the Add-In assembly | `Siemens.Engineering.Base.dll` — 1382 types |
| `SW.Blocks.PlcBlock` | inside the Add-In assembly | `Siemens.Engineering.Step7.dll` |
| HMI types | `Siemens.Engineering.Hmi.dll` | no such assembly — `WinCC.dll` / `WinCCUnified.dll` |
| References needed | **only** the Add-In assembly | **both** `AddIn.Base` and `Base` |
| `extern alias` | needed if `Siemens.Engineering.dll` is added | never needed |

`AddIn.V20` references: `Siemens.Engineering.AddIn` · `.AddIn.Permissions` · `.AddIn.Utilities` · `Siemens.Engineering.Hmi`

`AddIn.V21` references: `Siemens.Engineering.AddIn.Base` · `Siemens.Engineering.Base` · `.AddIn.Permissions` · `.AddIn.Utilities`

#### V20: do not add `Siemens.Engineering.dll`

Since `Siemens.Engineering.AddIn.dll` already carries the whole object model, referencing **both** assemblies breaks the build: they define distinct, incompatible copies of `Siemens.Engineering.IEngineeringObject`, which becomes ambiguous, forcing `extern alias TiaAddIn;` plus `using AddInEngineering = TiaAddIn::Siemens.Engineering;` with every menu generic typed over `AddInEngineering.IEngineeringObject`.

**All of that is avoidable**: do not add `Siemens.Engineering.dll` until a specific type actually fails to compile.

#### V21: the split is clean

`IEngineeringObject` exists exactly once, in `Siemens.Engineering.Base.dll`. Nothing is duplicated, so referencing both assemblies is not only safe but required, and the `extern alias` problem simply does not arise.

> **Two referenced assemblies are missing from `V21\net48\`.** `Siemens.Engineering.Contract` is referenced by **13 of the 16** — not only by `AddIn.Base` — and `Siemens.Engineering.ClientAdapter.Interfaces` by `Base`. Neither has blocked a build so far, because the compiler only needs a referenced assembly when one of its types appears in a signature the code actually touches. The failure to recognise is *"is defined in an assembly that is not referenced"*, and the fix is to copy the DLL out of a TIA V21 installation — Openness does not ship it.

## Packaging and deployment

### 1. The Publisher

`Siemens.Engineering.AddIn.Publisher.exe` turns the compiled `.dll` into the `.addin` TIA loads. The syntax uses **flags**, not positional arguments:

```
Siemens.Engineering.AddIn.Publisher.exe --configuration <Config.xml> --outfile <output.addin> --console
```

Flags: `--configuration/-f`, `--outfile/-o` (optional — defaults to an `.addin` with the same name and folder as the main assembly), `--certificatepassword/-p`, `--logfile/-l`, `--edition/-e`, `--verbose/-v`, `--console/-c`, `--pause/-x`, `--template/-t`, `--skipEngMemberCheck/-s`.

Each project uses the Publisher **for its own version**:

| Project | Path |
|---|---|
| `AddIn.V20` (V17–V20) | `.lib\Siemens\PublicAPI\V20.addIn\Siemens.Engineering.AddIn.Publisher.exe` |
| `AddIn.V21` | `.lib\Siemens\PublicAPI\V21\Siemens.Engineering.AddIn.Publisher.exe` — loose in `V21\`, **not** inside `V21\net48\` |

**Gotcha**: the Publisher resolves the config's `<Assembly>` path **relative to the config file's own location**, not to the working directory. The approach taken here is to copy `Config.xml` next to the DLL before invoking it:

```xml
<Target Name="PublishTiaAddIn" AfterTargets="Build" Condition="Exists('$(TiaPublisher)')">
  <Copy SourceFiles="$(MSBuildProjectDirectory)\Config.xml" DestinationFolder="$(TargetDir)" />
  <Exec Command="&quot;$(TiaPublisher)&quot; --configuration &quot;$(TargetDir)Config.xml&quot; --outfile &quot;$(TargetDir)$(TargetName).addin&quot; --console" />
</Target>
```

The `Condition` keeps a clone on a machine without the Siemens DLLs from breaking the whole build: the `.dll` still compiles and only packaging is skipped. `--console` dumps validation detail into the build output, so a schema failure names the offending element.

### 2. `Config.xml`

Every Add-In needs its own, validated against `Siemens.Engineering.AddIn.Publisher.xsd` (present in both `V20.addIn\` and `V21\`). Schema rules:

- The `xmlns` is **per version**: `http://www.siemens.com/automation/Openness/AddIn/Publisher/V20` (and `.../V21`). Copying the V20 one and only bumping the product version **does not work**.
- Required: `Product`, `FeatureAssembly` and `RequiredPermissions`. Inside the latter, `TIAPermissions` with either `TIA.ReadOnly` **or** `TIA.ReadWrite` — one, not both.
- Everything else is optional and **order does not matter** (`xs:all`).
- `<Product><Version>` must match `^(\d+\.)?(\d+\.)?(\d+\.)?(\d+)$` — no `1.0.0-beta`.
- `SecurityPermissions` is a **closed list of 22 specific elements**; inventing one makes the Publisher reject the file.

Request only the permissions actually used: every extra one is friction when signing the Add-In as trusted. Launching the satellite apps via `Process.Start` requires declaring `<Siemens.Engineering.AddIn.Permissions.ProcessStartPermission/>`.

#### Shipping `Core` inside the `.addin`

The package contains **only what `Config.xml` declares**. Project references are not enough, and they are not transitive either — every extra assembly needs its own entry:

```xml
<AdditionalAssemblies>
  <AssemblyInfo>
    <Assembly>PLC-Framework.Core.dll</Assembly>
  </AssemblyInfo>
  <AssemblyInfo>
    <Assembly>PLC-Framework.AddIn.Shared.dll</Assembly>
  </AssemblyInfo>
</AdditionalAssemblies>
```

`AdditionalAssemblies` accepts any number of `AssemblyInfo` entries, each with an `Assembly` and an optional `Pdb`. Because `Config.xml` is copied to `$(TargetDir)` and the project reference already put `core.dll` there, the plain file name resolves — the same mechanism as `FeatureAssembly`.

**Forgetting this fails late and confusingly**: the build succeeds, the Publisher does not complain, the `.addin` is produced, TIA loads it — and it throws a `FileNotFoundException` the first time the menu is built. Confirm it in the build output, which names every packaged assembly:

```
Packaging assembly 'plc-framework.v20, ... processorarchitecture=amd64'
Packaging assembly 'plc-framework.Core, ... processorarchitecture=msil'
```

That `msil` also confirms `Core` stayed AnyCPU while the Add-In is `amd64`; the two coexist in the same process without trouble.

> **Merging the two DLLs into one is deliberately not done.** ILRepack or Costura.Fody could do it, but the `.addin` is already a single deployable file, `AdditionalAssemblies` is the vendor-supported mechanism, and the satellite apps will need `Core` as an assembly with a single identity anyway. Merging would leave the same code compiled in two places.

### 3. Installing into TIA Portal

Copy the `.addin` to the `UserAddIns` folder of the matching TIA version, on the machine where TIA runs (here, the VM):

```
C:\Users\<user-name>\AppData\Roaming\Siemens\Automation\Portal V20\UserAddIns\
C:\Users\<user-name>\AppData\Roaming\Siemens\Automation\Portal V21\UserAddIns\
```

Three things that bite:

- It is **`UserAddIns`**, not `AddIns`.
- It is under **`AppData\Roaming`** (what `%AppData%` expands to), not `AppData\Local`.
- The folder does not exist until created by hand.

Each TIA version only looks at its own folder, so both Add-Ins coexist without interfering. The `.addin` already contains the DLL; nothing else needs copying.

### What a `.addin` actually is

An **OPC package** — a zip (`PK` magic bytes) with `_rels/.rels` and `[Content_Types].xml`, the same container family as `.docx`. Its parts, read from a real build:

```
EngineeringVersion
PLC-FRAMEWORK.V21,%20VERSION=1.0.0.0,...          the FeatureAssembly
LocalAssemblyCache/PLC-FRAMEWORK.CORE,...         one part per AdditionalAssemblies entry
Product/Id · Product/ProductName
Multiuser/DisplayState
DevToolsInfo/ProjectTemplate
Permissions/Required/Tia/TIA.ReadWrite
Permissions/Required/Security/<one part per permission>
Meta/TimeStampUTC · Version · Description · Location · PublisherTarget
```

Useful consequences:

- Renaming the file is safe; the identity TIA uses comes from `Product/Id`, not the file name.
- Additional assemblies land under `LocalAssemblyCache/`, which is a quick way to confirm `Core` really travelled.
- **There is no icon part, and the Publisher schema has no icon element** — no `icon`, `image`, `logo` or `thumbnail` anywhere in either `.xsd`. A `.addin` cannot carry its own icon. What Explorer shows comes from the `.addin` file-type association that TIA's installer registers, which is per extension, not per file.
- Branding inside TIA therefore goes on the **menu entries** (`AddActionItemWithIcon`) and on `<Product><Name>` / `<Description>`. The submenu root takes no icon: `ContextMenuAddInRoot` exposes only `Items` and `DefaultLabelText`.
- **Do not post-process the zip.** TIA validates package integrity and distinguishes trusted / unsigned / revoked-tampered; editing the package lands in the third.

Unsigned, TIA loads it but it must be enabled manually. Optionally sign it with `Company_Trusted_Add-In_Certification_Tool.exe` to mark it trusted — TIA distinguishes three levels: trusted, unsigned/invalid, and revoked/tampered.

### A `.addin` cannot carry an executable

Tested, because it fails in the worst possible way. An `.exe` listed under `AdditionalAssemblies` is **silently dropped**: the Publisher reports `SUCCEEDED`, lists only the other assemblies, and produces a package without it.

```
Packaging assembly 'plc-framework.v20 ...'
Packaging assembly 'plc-framework.core ...'
Packaging assembly 'plc-framework.addin.shared ...'
 --> S U C C E E D E D <--          the .exe is simply not there
```

The filter is purely by extension — the very same PE file renamed to `.dll` gets packaged — but that is not a workaround worth taking. The part lands under `LocalAssemblyCache/`, and TIA loads it as an assembly **inside its own process**; it never becomes a file on disk that could be started.

Launching an external executable is the sanctioned path, and Siemens equips it: `Siemens.Engineering.AddIn.Utilities` ships a full mirror of `System.Diagnostics.Process` — `Start(fileName, arguments)`, a 20-property `ProcessStartInfo`, `RedirectStandardInput/Output/Error`, `OutputDataReceived`, `Exited`, `WaitForExit`, even `Start(fileName, userName, password, domain)` — and `ProcessStartPermission` exists in the closed permission list for exactly this.

That redirection is worth remembering: it is a ready-made IPC channel between the Add-In and a satellite, simpler than named pipes.

### The five locations, at a glance

Everything the framework writes or reads on a station lands in one of five places. The rule
that decides which is short: **`%ProgramData%` is what the installer puts there;
`%LOCALAPPDATA%` is what the applications write.** Getting that backwards is the failure
that passes every test on a developer's machine — where you are an administrator — and
fails at a customer's, where you are not.

| Location | Scope | In code | Elevation |
|---|---|---|---|
| `%ProgramData%\PLC-Framework\` | per **machine** | `Core.InstallPaths.Root` | once, to install |
| `%LOCALAPPDATA%\PLC-Framework\` | per **user** | — no constant yet | none |
| `%AppData%\Siemens\Automation\Portal V20\UserAddIns\` | per user, per TIA version | — Siemens' own | none |
| `%AppData%\Siemens\Automation\Portal V21\UserAddIns\` | per user, per TIA version | — Siemens' own | none |
| `<TIA project>\.plc-framework\` | per **project** | `Core.Config.ConfigPaths` | none |

`PLC_FRAMEWORK_HOME` replaces the first row entirely, which is how a test run or the VM
points at a staging folder without installing anything.

**`%ProgramData%\PLC-Framework\`** — what was installed

| | |
|---|---|
| `tools\` | `InstallPaths.Tools`. **Every** executable shipped — satellites and command-line helpers alike — each with its DLLs, `.exe.config` included. Copied in as a whole build output, not as a lone `.exe` |

**`%LOCALAPPDATA%\PLC-Framework\`** — what the applications write

| | Written by | |
|---|---|---|
| `credentials.json` | `Satellite.DataBlockSnapshot`, only after a login the CPU accepted | web server user and password per project + PLC; the password under DPAPI `CurrentUser` |
| `.env` | `Satellite.ConfigEditor` | `GITHUB_TOKEN`. Here because the token is personal **and** because `%ProgramData%` is not writable |
| `config.template.json` | `Satellite.ConfigEditor`, when it is missing or does not parse | the template for a new `config.json`, written out from the embedded copy so it can be customised |

**`...\Portal V20\UserAddIns\`** and **`...\Portal V21\UserAddIns\`**

| | |
|---|---|
| `PLC-Framework.V20.addin` | carries `PLC-Framework.V20.dll` plus `Core.dll` and `AddIn.Shared.dll` |
| `PLC-Framework.V21.addin` | the same, with `PLC-Framework.V21.dll` |

**`<TIA project>\.plc-framework\`** — what belongs to the project

| | Written by | Read by |
|---|---|---|
| `config.json` | the user by hand, and `Satellite.ConfigEditor` | the Add-In |
| `exports\` | `Satellite.DataBlockSnapshot` → `<ip>-<DB>-snapshot-<timestamp>.xlsx` | the user |

**Not one secret lives here, and that is deliberate**: this folder is under version control,
with `.version-control\` sitting right beside it. It is why the PLC credentials live under
`%LOCALAPPDATA%` and why `config.json` carries the literal `${GITHUB_TOKEN}` rather than the
token.

### Where the satellites live

```
%ProgramData%\PLC-Framework\         ← Core.InstallPaths.Root
├── .env                             ← InstallPaths.EnvFile — MOVING, see below
└── tools\                           ← InstallPaths.Tools — every executable shipped
```

**Per machine, not per user**: one install serves every engineer who logs into the station, and both the V20 and V21 Add-Ins resolve the same path. Creating the folder needs administrator rights once; reading it afterwards does not — `%ProgramData%` grants `BUILTIN\Users` read and execute by default.

**One folder for every executable.** A split between `satellites\` and `tools\` was tried and reverted: the boundary blurred immediately, and it only raised the question of which half a new executable belonged in.

> **"Satellite" is a role, not a location.** It names a WPF app the Add-In launches from the menu — as opposed to a command-line helper. Both live in `tools\`. The word stays in project names (`Satellite.About`) and in the architecture decisions, because it describes what a thing *is*; the folder only says where it sits.

> **The `.env` is moving out of here, to `%LOCALAPPDATA%\PLC-Framework\.env`** (decided
> 2026-09-08 — see *The `config.json` contract*). The line above used to read that a
> station-wide `.env` was the right call for a shared team credential, "and a personal token
> would belong somewhere per-user instead". The only thing in it turned out to be
> `GITHUB_TOKEN`, which is exactly that personal token — and `%ProgramData%` grants ordinary
> users read and execute but **not write**, so the config editor could not save here anyway.
> `tools\` stays: an installed executable genuinely is per machine.

`PLC_FRAMEWORK_HOME` overrides the root, which is how you point a test run — or the VM — at a staging folder without installing or needing elevation.

Not next to the `.addin`. `UserAddIns` is **per TIA version**, so V20 and V21 would each need their own copy of a satellite that is really per machine, and it is Siemens' directory rather than ours. Not derived from `Assembly.Location` either: TIA loads the Add-In out of the package, so that path cannot be relied on.

`Environment.GetFolderPath(CommonApplicationData)` is deterministic and identical for every consumer, and needs no discovery. Note it is **`CommonApplicationData`, not `LocalApplicationData`** — `%ProgramData%`, not `%AppData%`. Copying a satellite into the latter is the easiest way to get a "not installed" error out of an install that looks correct.

No Siemens API offers an alternative: neither `TiaPortal` nor the `Siemens.Engineering.AddIn` namespace exposes the Add-In's own path or the running TIA version.

### Installing a satellite

Copy the project's **whole build output** into `tools\`, not just the executable:

| File | |
|---|---|
| `PLC-Framework.Satellite.About.exe` | the app |
| `PLC-Framework.Core.dll` | `Product.Title`, used by the window title and the mutex name |
| `PLC-Framework.UI.Shared.dll` | `BrandLogo.xaml`, `Theme.xaml`, `SingleInstance` |
| `PLC-Framework.Satellite.About.exe.config` | 174 bytes pinning .NET Framework 4.8 |

The three `.pdb` files are optional: they only add line numbers to stack traces, which is worth having while testing on the VM and not afterwards.

```
robocopy "src\Satellite.About\bin\Debug\net48" "C:\ProgramData\PLC-Framework\tools" /E
```

Creating `C:\ProgramData\PLC-Framework\` needs elevation once. To test without it, point `PLC_FRAMEWORK_HOME` at any folder holding a `tools\`, and **restart TIA Portal afterwards** — a running process does not see an environment variable created after it started.

### Partial trust, and what it forbids

**TIA runs an Add-In in a restricted sandbox.** That is not a footnote: it decides what the
Add-In half of the framework may do, and it fails at run time with no hint at compile time.

It surfaced as a `System.Security.SecurityException` out of
`DataContractJsonSerializer.WriteObject`:

```
The data contract type 'AddIn.Shared.Actions.DataBlockSnapshotAction+HandoffPayload'
is not serializable in partial trust because it is not public.
```

The types were `internal`, nested inside the action. Serialization in partial trust needs
a **visible** type — public, and public all the way out of any nesting. Moving them to
top-level public types in their own file fixed it.

The lesson generalises past serialization: **anything reflective is likelier to be denied
inside TIA than outside it**, and the failure arrives as a crash report from the field
rather than a red squiggle. It can be reproduced here, though, which is much cheaper than a
round trip to the VM:

```csharp
PermissionSet permissions = new PermissionSet(PermissionState.None);
permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
AppDomain sandbox = AppDomain.CreateDomain("partial-trust", null, setup, permissions);
```

That is the tightest partial trust there is, so code that survives it survives TIA.

> **Building fails while TIA has the Add-In loaded.** If the VM reaches `bin\Debug\net48`
> through a shared folder, the Publisher cannot overwrite the `.addin` and the build stops
> with `MSB3073`. The Restart Manager names `vmware-vmx.exe` as the owner. Close TIA, or
> copy the package somewhere else before loading it.

### Reading a CPU's addresses out of the project

The satellite cannot ask TIA anything, so the Add-In gathers the addresses and hands them
over. Two things about that walk are not obvious, and both are verified on the VM:

**The interface is not on the item that owns the software.** A CPU's network interface is a
separate child device item — "PROFINET interface_1" and the like — so the whole device has
to be walked. Asking `GetService<NetworkInterface>()` on the item that carries the
`PlcSoftware` finds nothing.

**The IP is an attribute of the node, not a typed property.** `HW.Address` is a different
thing entirely — an I/O address, with a start and a length — and reaching for it here is
the obvious wrong turn. The path is:

```csharp
DeviceItem.GetService<NetworkInterface>()   // per device item, walking the whole device
    .Nodes                                   // NodeComposition
    .GetAttribute("Address")                 // confirmed: the attribute is called "Address"
```

Nodes that are not IP — a PROFIBUS node's address is a number like `2` — are filtered out
by shape, so the drop-down only offers things worth pointing a browser at.

### How the Add-In launches one

`AboutAction` is the worked example and the shape generalises:

```csharp
string path = InstallPaths.Tool(ExecutableName);
if (!File.Exists(path)) { /* notifier.Error naming the missing path */ return; }

string error = launcher.Start(path);
if (error != null) { /* notifier.Error with the reason */ }
```

Three things there are deliberate:

- **The "not installed" check sits in the action, not in the adapter.** It is the expected failure, and it deserves a message naming the missing path rather than whatever the process API happens to say.
- **`IProcessLauncher.Start` returns the failure as a `string`, not as an exception**, so the Add-In reports it through `ITiaNotifier` like every other problem.
- **`ExecutableName` is the satellite's `AssemblyName`.** Renaming that project breaks this at runtime rather than at compile time: the two sit in different layers on purpose, so nothing binds them at build time.

The adapter wraps Siemens' `Process` — what `ProcessStartPermission` authorises — and is duplicated per version for the reason given under *Known V20 / V21 divergences*.

## The Add-In API

Verified by reflection over `V20.addIn\Siemens.Engineering.AddIn.dll` and `V21\net48\Siemens.Engineering.AddIn.Base.dll`. **These signatures are identical in both versions** — only the assembly they live in changes:

| Member | Actual signature |
|---|---|
| `ProjectTreeAddInProvider` | `abstract`, **parameterless** constructor. TIA instantiates the derived class by looking for a constructor taking `TiaPortal` and injects it |
| `GetContextMenuAddIns()` | **`protected virtual`** (not `abstract`), returns `IEnumerable<ContextMenuAddIn>` |
| `ContextMenuAddIn` | constructor `(string displayName)`; override `protected override void BuildContextMenuItems(ContextMenuAddInRoot)` |
| `ContextMenuAddInRoot.Items` | a `ChildItemFactory` |
| `ChildItemFactory` | `AddActionItem<T>(string, OnClickDelegate)` **`where T : IEngineeringObject`**, plus `WithIcon` / `WithCheckBox` / `WithRadioButton` variants, 2- and 3-generic-type versions, and `AddSubmenu(string)` |
| `MenuSelectionProvider<T>` | **declares no members of its own**; inherits from the non-generic base |
| `MenuSelectionProvider` (base) | `public IEnumerable<object> GetSelection()` · `public IEnumerable<TRequested> GetSelection<TRequested>()` · `internal int GetSelectedObjectCount` |
| `NotificationIcon` | lives in **`Siemens.Engineering`**, NOT in `Siemens.Engineering.AddIn.Menu` |

### Practical consequences

- **`AddInProvider` and `AddInController` must be `public`.** TIA instantiates them by reflection from outside the assembly. If they are `internal` the build still succeeds and the Add-In simply **does not appear** in TIA — a silent failure.
- Because `GetContextMenuAddIns()` is `virtual` rather than `abstract`, getting the signature wrong **produces no compile error**: the menus just never show up.
- `GetSelection()` returns `IEnumerable<object>` **even though the provider is typed** as `MenuSelectionProvider<Project>`. Prefer the generic overload, which avoids the cast:

  ```csharp
  Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
  ```

  Use `FirstOrDefault()` rather than `First()`: an empty selection makes `First()` throw. In some contexts TIA also invokes the delegate with a `null` provider.
- The generic type argument of `AddActionItem<T>` decides **which tree node the entry appears on**: `Project` = root node, `DeviceItem` = a PLC, `PlcBlock` = a block.

> Sample code using `AddInBase` / `SessionInitialize` that circulates online **is not the real API**. The entry points are concrete providers: `ProjectTreeAddInProvider`, `ProjectLibraryTreeAddInProvider`.

### Known V20 / V21 divergences

Despite the identical menu and provider surface, the two versions are not source-compatible. Divergences found so far:

**Message box.** V21 removed the `GetMessageBox()` extension on `TiaPortal` and renamed the type:

| | V20 | V21 |
|---|---|---|
| Type | `Siemens.Engineering.AddIn.MessageBox` | `Siemens.Engineering.AddIn.MessageBoxProvider` |
| How to obtain | `tiaPortal.GetMessageBox()` | `tiaPortal.GetService<MessageBoxProvider>()` |

```csharp
// V21
_tiaPortal.GetService<MessageBoxProvider>()
          .ShowNotification(NotificationIcon.Information, caption, message);
```

`TiaPortal` implements `IEngineeringServiceProvider`, which exposes `T GetService<T>() where T : IEngineeringService`; `MessageBoxProvider` implements `IEngineeringService`. Both `ShowNotification(NotificationIcon, string, string)` and a four-argument overload taking a `detailedMessage` are available, plus `ShowConfirmation(...)` with `ConfirmationIcon` / `ConfirmationChoices` / `ConfirmationResult`.

`GetService<T>()` returns `null` when the service is unavailable — worth guarding in real actions.

This is precisely the kind of difference that justifies a port in `AddIn.Shared` — `ITiaNotifier.Info(caption, message)` — implemented once per version, so the rest of the code never learns that the difference exists.

**`ProjectBase` lost four properties and gained one.** Compared declared property by declared property, seven of the eight spine types are identical across versions — `TiaPortal`, `HardwareObject`, `DeviceItem`, `SoftwareContainer`, `PlcSoftware`, `PlcBlockGroup` and `PlcUnitBase` all keep the same surface *and* the same base class. `ProjectBase` is the exception:

| Property | In V21 |
|---|---|
| `Graphics` | **removed** — `MultiLingualGraphic` and its composition exist nowhere in the sixteen assemblies |
| `PlantViews` | **removed** — likewise, no `PlantView` and no `PlantViewComposition` |
| `IsSimulationDuringBlockCompilationEnabled` | **removed** |
| `IsVirtualPlcDuringBlockCompilationEnabled` | **removed** |
| `TextCategories` | **added** — returns `TextCategoryComposition`, declared in `Base` |

These are removals rather than relocations: the types themselves are gone. Code touching `project.Graphics` or `project.PlantViews` fails to compile against V21, which is the good outcome; the bad one is a V20 Add-In still shipping those calls and nobody noticing until someone opens V21.

**Assembly identity, where the source is identical.** This one is easy to miss, because there is nothing to see in the code. `Siemens.Engineering.AddIn.Utilities` exposes the very same `Process` wrapper in both versions — same namespace, same type, same members, verified by reflection — but the assembly is signed with a **different public key token**:

| | V20 | V21 |
|---|---|---|
| Assembly name | `Siemens.Engineering.AddIn.Utilities` | `Siemens.Engineering.AddIn.Utilities` |
| Public key token | `65b871d8372d6a8f` | `29bfe5fdf4ba5d3b` |

So even source-identical code cannot be compiled once: a single binary would bind to one identity and fail to load in the other host. That is why `Adapters/ProcessLauncher.cs` exists twice, byte for byte, behind `IProcessLauncher`. Nothing in either file hints at the reason, which is why it is written down here.

The wrapper itself delegates to `System.Diagnostics.Process` — it holds an `m_InternalProcess` field — so its `Dispose` releases the handle without touching the process that was started, and a fire-and-forget launch can safely use a `using`.

## The CPU web server API

Nothing to do with Openness: this is the JSON-RPC service an S7-1200 / S7-1500 exposes on
its own web server, and it is how the satellites read live values. Everything below was
measured against two real CPUs — an S7-1500 at `Api.Version 2.00906` and an S7-1200 G2 at
`Api.Version 6` — not taken from documentation.

Single endpoint, `https://<ip>/api/jsonrpc`. `Api.Login` returns a token that rides in the
`X-Auth-Token` header. The certificate is self-signed and TLS 1.2 is required, so both
settings belong on the `HttpClientHandler` and **not** on `ServicePointManager`, whose
equivalents would change every connection the process makes.

### Batching: the finding that mattered

`PlcProgram.Read` documents a list in `var`. **Both CPUs reject it** with
`-32602 Invalid Params`. The reference Python application tries only that, gives up, and
falls back to one HTTP request per variable.

But JSON-RPC 2.0's own batching — an *array of envelopes* in one POST — works on both:

| Reading 54 variables | |
|---|---|
| one request per variable | 4,671 ms |
| one batched POST | **150 ms** |

Thirty-one times. Extrapolated to a block of 5,000 variables that is seven minutes against
fourteen seconds. **Results are matched by request `id`, never by position**: the
specification lets a server answer a batch in any order, and both CPUs happening to keep
the order is not something to build on. A batch also carries a per-entry `error`, so one
unreadable variable no longer poisons the other forty-nine.

### Browse is not a cheap call, and batching does not help it

On the S7-1200 G2, `PlcProgram.Browse` costs a **flat ~92 ms per call whatever the batch
size** — 1 call or 800, the per-call cost does not move. The work is CPU-side, not round
trips. The only remedy is fewer calls.

Hence the array optimisation: **every element of an S7 array has the same type, so only
the first is browsed and its subtree is copied onto the rest.** The first and last element
are both browsed and their members compared before copying, on every array of every run —
the guarantee comes from the language, but a capture is worth no more than its weakest
assumption.

| Browsing a 1,614-variable block | S7-1500 | S7-1200 G2 (8,910 vars) |
|---|---|---|
| depth-first, one call per node | 7,767 ms | 96,405 ms |
| batched per tree level | 1,611 ms | 96,405 ms |
| plus array-template copying | **182 ms** | **887 ms** |

### Reading scales with the block, not with the CPU

| | S7-1500 | S7-1200 G2 |
|---|---|---|
| 500 variables | 2.88 ms/var | 3.65 ms/var |
| 1,614 variables | 2.94 ms/var | 3.63 ms/var |
| a block of ~590 variables present on both | 1,908 ms | 2,231 ms |

Flat per variable, and the two CPUs are within 26 % of each other on the same block. A
capture taking 33 s instead of 5 s is a bigger block, not a slower processor. Raising the
read batch from 50 to 400 buys 10 %, all of it before 100, so 50 stays — a smaller batch
is a smaller thing to lose when a POST fails.

### What the API returns, and what it refuses

The default mode hands back **decoded, natively typed JSON** — no hex, no strings for
everything:

```
time    0            real  6.785        bool  false
int     15  /  -1    udint 422690608    string "sin valor"
```

`mode: "raw"` returns the S7 memory image instead, big-endian: `6.785` becomes
`[64,217,30,184]`, and a STRING arrives as `[max, length, chars..., padding]`. Useful only
for a type the CPU cannot convert itself.

Two refusals are worth knowing, because they shape the walk:

| Call | Answer |
|---|---|
| browse or read an array without an index | `203 Invalid array index` |
| read a whole struct, or a DTL, in one call | `204 Unsupported address` |

The second one closes off the obvious optimisation: there is no reading a struct in one
request, so the walk has to reach every leaf. The first is why arrays are checked **before**
`has_children` — an array of structs reports children too, and descending into it without
an index is an error rather than a descent.

### What the two CPUs do not share

| | S7-1500 | S7-1200 G2 |
|---|---|---|
| `Api.Version` | 2.00906 | 6 |
| Methods exposed | 31 | 82 |
| Extra families | — | `Modules.*`, `Failsafe.*`, `Technology.*`, `Plc.ReadCpuType`, `Plc.ReadSystemTime` |

None of the extra methods offers a bulk read: JSON-RPC batching is the best available on
both.

## Capturing a data block

`Satellite.DataBlockSnapshot` writes one workbook per block,
`<ip>-<DB>-snapshot-<timestamp>.xlsx`, into `<TIA project>\.plc-framework\exports\` by
default. The rules it follows are all about the file being trustworthy later:

- **Every variable that was browsed has a row**, carrying its value or the reason it could
  not be read. The reference application drops the failures, which leaves a partial
  capture looking complete.
- **A file appears only when the block read completely.** An incomplete read is reported in
  the window and leaves nothing behind to be mistaken for a good capture.
- **Cells are typed.** A number arrives as a number and a bool as a bool, which is the
  reason to write `.xlsx` at all rather than let Excel guess at a CSV.
- **Numbers are written invariant**, because the format stores a numeric cell as an
  invariant string and lets the reader's locale display it. `6,785` in that slot produces a
  file no Excel reads as a number, including a Spanish one.
- **An empty string is written as an empty cell of string type**, not as an absent cell —
  otherwise "this setting is empty" and "this value could not be read" look identical.
- **`xml:space="preserve"`**, so a setting whose value ends in a blank keeps it.
- A second sheet, `Info`, records the PLC, the block, the start and end of the read, the
  variable and failure counts, and whether the browse was truncated. **A capture is a sweep,
  not an instant**, so both ends of the window are recorded rather than one timestamp that
  would imply otherwise.

Verified with the OpenXML SDK's own validator — zero errors — and by reading the package
XML back: 990 numeric cells, 499 boolean, 126 string, none omitted.

### Remembering the web server credentials

Capturing settings across a plant means launching this window many times in an afternoon,
and the CPU's web server wants a user and a password every time. They are therefore
remembered — but where, and under what rule, is the whole of the design:

```
%LOCALAPPDATA%\PLC-Framework\credentials.json
```

```jsonc
{
  "entries": [
    { "project":  "E:\\proyectos\\Planta1",
      "plc":      "PLC_1",
      "address":  "192.168.0.20",
      "user":     "webclient",
      "password": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…" }   // DPAPI, CurrentUser
  ]
}
```

**Keyed by project directory plus PLC name, because one TIA project routinely holds ten
CPUs and each has its own user and password.** Any design keyed by project alone — a pair
in `.env`, a pair in `config.json` — has ten CPUs overwriting each other, and two open
projects overwriting each other again. Both keys already arrive in the handoff, so the
Add-In needed no change. The device name is the key rather than the address because a CPU
can be readdressed and has several addresses anyway; the address is the fallback for a
window started by hand, where no project handed a name over.

**Not inside the TIA project.** That was the first instinct — next to the exports, in
`.plc-framework\` — and it is wrong for one reason: TIA projects are under version control,
`.version-control\` sitting right beside it, so a credential file there reaches a commit or
a zipped copy eventually. Per user, outside the project, is what makes that impossible.

**The password is DPAPI-protected at `CurrentUser` scope**, with application entropy so a
protected blob from some other program cannot be pasted in and decrypted. State the
consequence plainly: **the file does not travel.** Another user, or the same project on
another station, gets nothing back and the credentials are typed once more. That is the
price of not having a shared key — and a key baked into the executable would not be
encryption but obfuscation, which is worse than plaintext because it looks safe.

**Nothing is stored until the CPU has accepted the credentials.** `CaptureRunner.Run` takes
an `authenticated` callback and invokes it on the line after `client.Login()`, so a wrong
password never reaches disk and never has to be removed from it. The runner itself knows
nothing about credential storage; it only reports that the login went through.

A *Remember* checkbox sits on the same row as the user and the password, ticked already
when something is stored for that CPU; its tooltip carries the full sentence the label has
no room for. Unticking it and capturing **forgets** the entry, which is what unticking
means. It starts unticked on a CPU never captured before: storing a password nobody asked
to store is not a good default. The timeout moved down to a row of its own, right-aligned —
it is a setting one touches once, and giving it a column beside the credentials was what
squeezed the two fields that matter.

**The store never throws.** A corrupt file, a blob written by another Windows user, a
missing folder — every one of them degrades to "nothing remembered" and the window opens
normally. A credential cache that breaks the application it exists to smooth would be worse
than no cache.

### Showing the password

An eye inside the password field reveals it, and that matters more once credentials are
remembered rather than typed: when a password arrives pre-filled from the store, revealing
it is the only way to check *which* one came back.

WPF gives no help here. `PasswordBox` cannot display what it holds, and `Password` is not
even a dependency property, so there is nothing to bind a "reveal" flag to. The shape that
works is **twin controls in one grid cell** — the `PasswordBox` and a plain `TextBox` — with
one of the two always `Collapsed`:

```csharp
private string CurrentPassword() =>
    RevealButton.IsChecked == true ? PasswordPlain.Text : PasswordBox.Password;
```

Three details are what make it feel like one control rather than two:

- **The text is carried across on every toggle**, in both directions, so editing while
  revealed and then hiding does not lose the change.
- **Focus and caret follow.** Without that, the operator carries on typing into a control
  that is no longer on screen, which is indistinguishable from a dead keyboard.
- **Everything else asks `CurrentPassword()`**, never either control directly, so no caller
  has to know the value lives in two places.

The icon is drawn in XAML — two paths and an ellipse — rather than loaded from an asset: an
`.ico` would have to be embedded, resolved and themed for sixteen pixels' worth of picture.
It shows a **struck-through eye while the password is visible**, stating what is true now
rather than what clicking would do.

Exercised through UI Automation against the running binary: the plain field is absent from
the accessibility tree while collapsed, revealing shows the password the store handed back,
and a value typed while revealed survives the round trip out to the `PasswordBox` and back.

### Theming a WPF field: Setters cannot reach hover and focus

Worth knowing before the next satellite repeats it. A `Style` full of `Setter`s gets a
`TextBox` most of the way onto a dark palette — background, foreground, caret, selection —
and then the control **repaints its own border Windows-blue on hover and on focus**, from
brushes hardcoded inside the stock template. No `Setter` reaches those two states, because
they are template triggers rather than properties.

Replacing the `ControlTemplate` is the only fix, and one template serves every field:

```xml
<ControlTemplate x:Key="FieldChrome" TargetType="Control">
```

`TargetType="Control"` rather than `TextBox` is what lets the `PasswordBox` share it: both
derive from `TextBoxBase`, which finds its editing surface by the name `PART_ContentHost`,
and every `TemplateBinding` needed is to a property `Control` already declares. The focused
field is then marked in the brand colour — the same brush the text selection already uses.

Two traps came out of doing it:

- **An implicit `TextBox` style reaches inside other templates.** An editable `ComboBox`
  builds its edit area from a real `TextBox` named `PART_EditableTextBox`, so it inherits
  the implicit style — padding included. Against the combo's fixed height that padding
  clipped the text. `Padding="0"` on that part fixes it.
- **For the same reason, the disabled trigger must not repaint the background.** That part
  is transparent by design, and a solid colour there puts a patch inside the drop-down
  whenever the form is busy. Dimming the border and the text says "disabled" perfectly
  well.

Verified by driving the real window through UI Automation — focus set per control, the
pointer parked off-window so hover could not be confused with focus, and a capture started
so the whole form could be seen disabled.

## The `config.json` contract

Settled 2026-09-08, ahead of building the validator and `Satellite.ConfigEditor`. This is
the authority on what the file must contain; the model under `Core/Config/Model/` is
deliberately **permissive** and enforces none of it, because a serializer that throws stops
at the first problem and loses the rest.

Two kinds of check, kept apart because only one of them touches the disk:

| | |
|---|---|
| **Structural** | required fields, closed value sets, internal references, uniqueness, regex that compile. Pure, and safe to run inside TIA Portal |
| **Environmental** | a path exists, a `${VAR}` resolves, a repository is reachable. Touches disk and network, and belongs in its own pass |

"Required" is scoped **per concern**, not per document: an action reads only its own section
and validates only that, while the editor runs the whole set before saving.

### `metadata`

| Field | Required | Type | Description |
|---|---|---|---|
| `metadata` | **Yes** | object | Holds `coreSource`, which decides the rest of the file |
| `metadata.coreSource` | **Yes** | string | Closed set: `local` \| `remote`. Selects which repository section is required |
| `metadata.version` | No | string | `v<n>.<n>` with two to four components of up to two digits — `v2.0`, `v1.12.3`, `v9.9.9.9` |
| `metadata.author` | No | string | Informative. Not validated |
| `metadata.description` | No | string | Informative. Not validated |

### `coreRemoteRepositoryConfig` — required only when `coreSource = remote`

| Field | Required | Type | Description |
|---|---|---|---|
| `apiUrl` | **Yes** | string | GitHub API endpoint. Must be an absolute URI |
| `owner` | **Yes** | string | Repository owner |
| `repository` | **Yes** | string | Repository name |
| `branch` | **Yes** | string | Branch. The template ships `main`; empty is an error |
| `folder` | **Yes** | string | Path inside the repository down to the core |
| `dependencyFile` | **Yes** | string | Dependency graph file name, today `core.json` |
| `token` | No | string | **Always the literal `${GITHUB_TOKEN}`** — see below. Absent or empty means a public repository |

### `coreLocalRepositoryConfig` — required only when `coreSource = local`

| Field | Required | Type | Description |
|---|---|---|---|
| `repository` | **Yes** | string | Local path. That it exists on disk is *environmental*, checked separately |
| `folder` | **Yes** | string | Path inside the repository down to the core |
| `dependencyFile` | **Yes** | string | Dependency graph file name |

### `projectConfig`

| Field | Required | Type | Description |
|---|---|---|---|
| `projectConfig` | **Yes** | object | What the Add-In applies to the TIA project |
| `projectConfig.hierarchy` | **Yes** | object | The folder tree to create |
| `projectConfig.codingStyle` | **Yes** | object | Naming rules, and which objects they apply to |

### `projectConfig.hierarchy`

Every list is **required but may be empty**. `[]` says "this concern has no folders"; a
missing key says nothing at all, and the difference is worth keeping.

| Field | Required | Type | Description |
|---|---|---|---|
| `blocks` | **Yes** | array of `Group` | Folders under Program blocks. May be `[]` |
| `technologyObjects` | **Yes** | array of `Group` | Folders under Technology objects. May be `[]` |
| `tagTables` | **Yes** | array of `Group` | Folders under PLC tags. May be `[]` |
| `types` | **Yes** | array of `Group` | Folders under PLC data types. May be `[]` |
| `softwareUnits` | No | object | Applied inside **every** software unit. Only the S7-1500 has them; on an S7-1200 the section is ignored |
| `softwareUnits.blocks` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |
| `softwareUnits.tagTables` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |
| `softwareUnits.types` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |

There is deliberately **no `technologyObjects` under `softwareUnits`**: a software unit
exposes blocks, tag tables and types only. Nothing in this section creates units — they are
named by the user after the plant's architecture, so the config describes only what goes
*inside* one.

### `Group` — recursive

| Field | Required | Type | Description |
|---|---|---|---|
| `name` | **Yes** | string | Folder name. Not empty. **Unique among siblings of the same parent**; the same name in a different branch is legal |
| `groups` | No | array of `Group` | Sub-folders. Same rules, all the way down |

### `projectConfig.codingStyle`

Same rule as the hierarchy: required, possibly empty.

| Field | Required | Type | Description |
|---|---|---|---|
| `rules` | **Yes** | array of `Rule` | The catalogue of naming rules. May be `[]` |
| `blocks` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `OB`, `ArrayDB`, `GlobalDB`, `InstanceDB`, `FC`, `FB` |
| `technologyObjects` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `TechnologicalInstanceDB` |
| `tagTables` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `PlcTagTable` |
| `types` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `PlcStruct` |
| `alarmTextLists` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `AlarmTexts` |

### `Rule`

| Field | Required | Type | Description |
|---|---|---|---|
| `id` | **Yes** | string | **Unique across the whole file**. This is what `implements` points at |
| `regex` | **Yes** | string | Naming pattern. **Must compile** — checked structurally, since a pattern that cannot compile is a broken file, not a broken environment |
| `descriptions` | No | array of string | Human-readable explanation of the rule |

### `PlcTypeObject`

| Field | Required | Type | Description |
|---|---|---|---|
| `type` | **Yes** | string | TIA object type. Closed set, listed per section above |
| `implements` | **Yes** | array of string | Rules this type accepts. Not empty, and **every id must exist in `rules`** — an internal reference, so a typo is caught rather than silently ignored |

### The token never lands in `config.json`

`config.json` lives inside the TIA project, and TIA projects are under version control. So
the file **always** carries the literal `${GITHUB_TOKEN}` and never the secret itself. The
editor shows the field masked with a show/hide eye — the same twin-control pattern as the
PLC password — and what the operator types goes to the `.env`, never to the JSON. Reading
the field means reading the `.env` back.

**The `.env` moves to `%LOCALAPPDATA%\PLC-Framework\.env`, per user.** A GitHub token is
personal, not a station-wide credential, and `%ProgramData%` grants ordinary users read and
execute but **not write** — so an editor writing there would fail on a real workstation
while working perfectly on a developer's own machine. Per user it is writable by the person
who owns the token, and two engineers sharing a station stop overwriting each other.

> This supersedes the earlier placement under `%ProgramData%\PLC-Framework\.env`, described
> under *Where the satellites live*. `%ProgramData%` keeps `tools\`, which genuinely is per
> machine. `InstallPaths.EnvFile` still points at the old location and has to follow.

The same reasoning caught a second file. `config.template.json` was first placed in
`tools\`, with the editor writing the embedded copy out there when it was missing — which
would have failed on any station where the engineer is not an administrator. It moves to
`%LOCALAPPDATA%\PLC-Framework\` too, and the general rule is now written down under
*The five locations, at a glance*: **`%ProgramData%` is what the installer puts there,
`%LOCALAPPDATA%` is what the applications write.**

## Prior reference project

`E:\PlcFramework\add-in-for-tia-portal\` holds an earlier Add-In with its own git repo. It is not part of this solution. Its value today is the **domain logic**, not the scaffolding — the csproj, the `Config.xml` and the Publisher cycle are solved better here:

| Folder | Contents |
|---|---|
| `Util\` | `Logger`, `DotEnv`, `EnvVar`, `Report`, `ActionContext`, `Constants` |
| `UserApp\` | domain models (`Project`, `Group`, `Hierarchy`, `Rule`, `Rules`, `CodingStyle`, `Metadata`) |
| `RemoteRepository\` | GitHub client and dependency graph |
| `Actions\` | project hierarchy, coding style, version checks, GitHub connection |

**Do not treat its code or its README as verified truth.** Known problems:

- Its namespaces are `PLC-Framework.TiaAddIn` — hyphenated, which is **illegal in a C# namespace**. It does not compile as-is.
- Its README claims both `Siemens.Engineering.dll` **and** `.AddIn.dll` are needed (with `extern alias`) to use `HW.*` / `SW.Blocks.*`. That is false — see above.
- Its `.csproj` mixes absolute and relative `HintPath` values, and one points at a nonexistent path.

## Licensing

`E:\PlcFramework\.lib\Siemens\` contains Siemens-proprietary binaries requiring an Openness license. They live outside this repository and **must never be committed or redistributed**.
