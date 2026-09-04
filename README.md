# tia-portal-addins

A **TIA Portal Add-In** (Siemens Openness) plus the **satellite apps** with UI that the Add-In launches on events.

- Solution: `tia-portal-addins.slnx` — the new XML format, not the classic `.sln`
- Target framework: **.NET Framework 4.8** for everything touching TIA Portal / Openness, WPF included

Three projects exist today: `Core`, plus the two Add-Ins `AddIn.V20` and `AddIn.V21`. Both Add-Ins are validated on the VM against their respective TIA Portal versions, each a hello world that shows a notification from a context-menu entry on the project root node. They serve as validated scaffolding to build on.

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
└── src/
    ├── Core/                 (net48, AnyCPU) — model, no host types at all          ← EXISTS
    ├── AddIn.Shared/         (net48, AnyCPU) — the Add-In layer, NO Siemens          ← EXISTS
    ├── AddIn.V20/            (net48, x64) — references PublicAPI\V20.addIn (V17–V20) ← EXISTS
    ├── AddIn.V21/            (net48, x64) — references PublicAPI\V21\net48           ← EXISTS
    ├── IPC/                  (net48) — Add-In ↔ satellite contract (named pipes)
    ├── Satellite.Shared/     (net48, WPF) — shared styles and controls
    └── Satellite.<Name>/     (net48, WPF) — one project per satellite app
```

```
src/Core/
├── Core.csproj               SDK-style net48, AnyCPU, embeds assets\
├── Product.cs                literals shared by every consumer
├── Assets.cs                 embedded assets → Stream, host-agnostic
├── InstallPaths.cs           %ProgramData%\PLC-Framework — .env and tools\
├── Config/                   config.json loader + model under Model/
└── DependencyGraph/          core.json model (DependencyGraph, Node, Edge, Report)
```

```
src/AddIn.Shared/
├── AddIn.Shared.csproj       SDK-style net48, AnyCPU, references Core
├── Actions/                  use cases: HelloWorldAction, CreateProjectHierarchyAction
└── Adapters/                 ports: ITiaNotifier, IGroupNode, HierarchyTargets
                              plus Icons (embedded assets → System.Drawing.Icon)
```

Both version projects have the same shape:

```
src/AddIn.VXX/
├── AddIn.VXX.csproj          SDK-style net48 x64, Siemens references + Publisher target
├── AddInProvider.cs          ProjectTreeAddInProvider — entry point
├── AddInController.cs        ContextMenuAddIn — menu wiring
├── Adapters/
│   ├── TiaNotifier.cs        ITiaNotifier against this version's message box
│   └── TiaGroupNode.cs       IGroupNode over this version's group compositions
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
 │            └── use cases (Actions/) + the ports they need
 └── model, config loader, assets — what the satellites will also use
```

| Layer | May depend on | Actual references |
|---|---|---|
| `Core` | only what **every** consumer needs, satellites included | `mscorlib`, `System.Core`, `System.Runtime.Serialization` |
| `AddIn.Shared` | `Core` + host types that are **not** Siemens | `+ System.Drawing` |
| `AddIn.VXX` | anything, Siemens included | `+ Siemens.Engineering.AddIn` |

That third column is read from the compiled assemblies, not from the `using` statements — it is the only check that cannot drift.

**Why `System.Drawing` must not reach `Core`.** `Icons` turns an asset into a `System.Drawing.Icon`; a WPF satellite will want an `ImageSource` from the same bytes. If the first materialiser went into `Core`, symmetry would eventually drag `PresentationCore` and `WindowsBase` in with the second — and `Core` is loaded **inside TIA Portal's process**. `Core`'s dependencies are the *intersection* of what its consumers need, never the union.

**Ports live with the layer whose vocabulary they speak.** `ITiaNotifier` and `IGroupNode` carry no Siemens reference at all, yet they belong to `AddIn.Shared`: one is shaped like a TIA notification, the other talks about PLC group trees. Neither is vocabulary a satellite would use.

**The adapter converts Siemens types before crossing a layer.** `Project` never leaves the version project:

```csharp
Project project = menuSelectionProvider?.GetSelection<Project>().FirstOrDefault();
HelloWorldAction.Execute(_notifier, project?.Name);
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
    └── favicon.ico
```

They are **embedded into `Core`** by a glob, so adding an asset is dropping a file — no `.csproj` edit anywhere:

```xml
<!-- Core.csproj -->
<EmbeddedResource Include="..\..\assets\**\*.*"
                  Link="Assets\%(RecursiveDir)%(Filename)%(Extension)" />
```

**`EmbeddedResource`, never `Content`.** The `.addin` package only carries assemblies, so a loose file would never reach TIA Portal. Embedded, the asset travels inside `PLC-Framework.Core.dll`, which is already listed under `AdditionalAssemblies` — so nothing extra to declare.

### Why they live in `Core`, and why the loader returns a `Stream`

Embedded resources are **scoped to the assembly that carries them**: `Assembly.GetManifestResourceNames()` only ever sees its own. So the loader has to sit in the same assembly as the assets. Putting both in `Core` means every consumer — the two Add-Ins today, the WPF satellites tomorrow — gets them just by referencing `Core`.

What must **not** be shared is the type:

```csharp
public static Stream Open(string path)     // Core.Assets
```

| Host | Materialises it as |
|---|---|
| `AddIn.V20` / `AddIn.V21` | `new Icon(stream)` → `System.Drawing.Icon`, what the TIA menu API takes |
| WPF satellite | `IconBitmapDecoder(stream, …).Frames[0]` → `ImageSource` |

Both were verified against the same embedded `favicon.ico`. Returning a `Stream` is also what keeps `System.Drawing` out of `Core`.

The action names the asset next to the behaviour that uses it, and the materialiser resolves it:

```csharp
// AddIn.Shared.Actions
public const string IconPath = "Brand/favicon.ico";
```

```csharp
// AddIn.Shared.Adapters — Icons.Get(path) → System.Drawing.Icon, with a cache
```

### Two traps

**Never hardcode the resource-name prefix.** MSBuild derives names as `<RootNamespace>.Assets.<Feature>.<file>`, e.g. `Core.Assets.Brand.favicon.ico`. A literal prefix does not fail at compile time when it stops matching — `GetManifestResourceStream` just returns `null` and the icon blows up at runtime inside TIA. Match on the **tail** instead, so renaming the root namespace or the link path cannot break it.

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

### V17–V20 vs V21

V17 through V20 share the same Openness API and are binary compatible. **V21 introduces breaking changes**: up to V20 the API is monolithic (`Siemens.Engineering.dll`, `Siemens.Engineering.AddIn.dll`), whereas in V21 the assemblies are split apart (`Siemens.Engineering.Base.dll`, `.Step7.dll`, `.WinCC.dll`, `.WinCCUnified.dll`, `.Safety.dll`, `.CFC.dll`, `.DCC.dll`, `.Startdrive.dll`, `.TeamcenterGateway.dll`, and on the Add-In side `Siemens.Engineering.AddIn.Base.dll` / `.Step7.dll` / `.Safety.dll`).

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

> Watch out: `Siemens.Engineering.AddIn.Base.dll` declares a dependency on `Siemens.Engineering.Contract`, and that DLL is **not present** in the local `V21\net48\` copy. It has not blocked anything so far, but a *"is defined in an assembly that is not referenced"* error would mean it needs copying from a TIA V21 installation.

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

### Where the satellites live

```
%ProgramData%\PLC-Framework\         ← Core.InstallPaths.Root
├── .env                             ← InstallPaths.EnvFile
└── tools\                           ← InstallPaths.Tools — every executable shipped
```

**Per machine, not per user**: one install serves every engineer who logs into the station, and both the V20 and V21 Add-Ins resolve the same path. Creating the folder needs administrator rights once; reading it afterwards does not — `%ProgramData%` grants `BUILTIN\Users` read and execute by default.

**One folder for every executable.** A split between `satellites\` and `tools\` was tried and reverted: the boundary blurred immediately, and it only raised the question of which half a new executable belonged in.

> **"Satellite" is a role, not a location.** It names a WPF app the Add-In launches from the menu — as opposed to a command-line helper. Both live in `tools\`. The word stays in project names (`Satellite.About`, `Satellite.Shared`) and in the architecture decisions, because it describes what a thing *is*; the folder only says where it sits.

> The `.env` is therefore readable by **every user of the station**. That is the right call for a shared team credential; a personal token would belong somewhere per-user instead.

`PLC_FRAMEWORK_HOME` overrides the root, which is how you point a test run — or the VM — at a staging folder without installing or needing elevation.

Not next to the `.addin`. `UserAddIns` is **per TIA version**, so V20 and V21 would each need their own copy of a satellite that is really per machine, and it is Siemens' directory rather than ours. Not derived from `Assembly.Location` either: TIA loads the Add-In out of the package, so that path cannot be relied on.

`Environment.GetFolderPath(LocalApplicationData)` is deterministic, identical for every consumer, needs no discovery and no elevation. `PLC_FRAMEWORK_HOME` overrides the root for staging or for a test run on the VM.

No Siemens API offers an alternative: neither `TiaPortal` nor the `Siemens.Engineering.AddIn` namespace exposes the Add-In's own path or the running TIA version.

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
