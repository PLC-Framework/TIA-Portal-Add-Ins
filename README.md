# tia-portal-addins

A **TIA Portal Add-In** (Siemens Openness) plus the **satellite apps** with UI that the Add-In launches on events.

- Solution: `tia-portal-addins.slnx` — the new XML format, not the classic `.sln`
- Target framework: **.NET Framework 4.8** for everything touching TIA Portal / Openness, WPF included

The current Add-In (`addin.v20`) is a hello world already validated in TIA Portal V20: a context-menu entry on the project root node that shows a notification. It serves as validated scaffolding to build on.

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
└── src/
    ├── Core/                  (net48) — models and interfaces, NO Siemens references
    ├── addin.v20/             (net48) — references PublicAPI\V20.addIn (valid V17–V20)   ← EXISTS
    ├── addin.v21/             (net48) — references PublicAPI\V21\net48
    ├── IPC/                   (net48) — Add-In ↔ satellite contract (named pipes)
    ├── Satellite.Shared/      (net48, WPF) — shared styles and controls
    └── Satellite.<Name>/      (net48, WPF) — one project per satellite app
```

```
src/addin.v20/
├── addin.v20.csproj      SDK-style net48 x64, Siemens references + Publisher target
├── AddInProvider.cs      ProjectTreeAddInProvider — entry point
├── AddInController.cs    ContextMenuAddIn — menu and action
└── Config.xml            PackageConfiguration for the Publisher
```

### Naming convention

| Item | Value in `addin.v20` | Rationale |
|---|---|---|
| Project / folder | `addin.v20` | lowercase |
| `AssemblyName` | `PLC-Framework.v20` | the output is part of PLC-Framework; hyphens are legal in an assembly name |
| `RootNamespace` | `addin` | repeating the version inside `addin.v20` is redundant |

There is no collision once `addin.v21` exists with the same `addin` namespace: they are separate assemblies that nothing references together — TIA loads one or the other depending on its version.

The `AssemblyName` **must match** the `<Assembly>` element in `Config.xml` (`PLC-Framework.v20.dll`). What TIA displays in the menu does not depend on it: that comes from the `string` passed to the `ContextMenuAddIn` constructor and from `<Product><Name>` in `Config.xml`.

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
dotnet build src\addin.v20\addin.v20.csproj -p:SiemensPublicApi=D:\some\other\path
```

### V17–V20 vs V21

V17 through V20 share the same Openness API and are binary compatible. **V21 introduces breaking changes**: up to V20 the API is monolithic (`Siemens.Engineering.dll`, `Siemens.Engineering.AddIn.dll`), whereas in V21 the assemblies are split apart (`Siemens.Engineering.Base.dll`, `.Step7.dll`, `.WinCC.dll`, `.WinCCUnified.dll`, `.Safety.dll`, `.CFC.dll`, `.DCC.dll`, `.Startdrive.dll`, `.TeamcenterGateway.dll`, and on the Add-In side `Siemens.Engineering.AddIn.Base.dll` / `.Step7.dll` / `.Safety.dll`).

**There is no `Siemens.Engineering.dll` in V21.** Hence the need for two separate projects.

## Building

```
dotnet build src\addin.v20\addin.v20.csproj
```

Output lands in `bin\Debug\net48\`:

- `PLC-Framework.v20.dll` — the compiled Add-In
- `PLC-Framework.v20.addin` — the package TIA Portal actually loads, generated automatically
- `Config.xml` — the copy the Publisher consumes (see below)

### References

Only four, all with `<Private>False</Private>` because TIA resolves them from its own installation at runtime — copying them next to the Add-In breaks loading:

`Siemens.Engineering.AddIn` · `.AddIn.Permissions` · `.AddIn.Utilities` · `Siemens.Engineering.Hmi`

**`PlatformTarget` must be `x64`**: TIA Portal V20 is 64-bit only and the Add-In loads inside its process.

#### `Siemens.Engineering.dll` is not needed

`Siemens.Engineering.AddIn.dll` (V20) exposes **2269 public types** and carries the full engineering object model embedded: `TiaPortal`, `Project`, `IEngineeringObject`, and also `Siemens.Engineering.HW.DeviceItem` and `Siemens.Engineering.SW.Blocks.PlcBlock`.

This matters because referencing **both** assemblies does break the build: they define distinct, incompatible copies of `Siemens.Engineering.IEngineeringObject`, which becomes ambiguous, forcing `extern alias TiaAddIn;` plus `using AddInEngineering = TiaAddIn::Siemens.Engineering;` with every menu generic typed over `AddInEngineering.IEngineeringObject`.

**All of that is avoidable**: do not add `Siemens.Engineering.dll` until a specific type actually fails to compile.

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
| `addin.v20` (V17–V20) | `.lib\Siemens\PublicAPI\V20.addIn\Siemens.Engineering.AddIn.Publisher.exe` |
| `addin.v21` | `.lib\Siemens\PublicAPI\V21\Siemens.Engineering.AddIn.Publisher.exe` — loose in `V21\`, **not** inside `V21\net48\` |

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

### 3. Installing into TIA Portal

Copy the `.addin` to:

```
%AppData%\Siemens\Automation\Portal VXX\UserAddIns
```

It is **`UserAddIns`**, not `AddIns`. The folder does not exist until created by hand. The `.addin` already contains the DLL; nothing else needs copying.

Unsigned, TIA loads it but it must be enabled manually. Optionally sign it with `Company_Trusted_Add-In_Certification_Tool.exe` to mark it trusted — TIA distinguishes three levels: trusted, unsigned/invalid, and revoked/tampered.

## The Add-In API

Verified by reflection over `V20.addIn\Siemens.Engineering.AddIn.dll`:

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
