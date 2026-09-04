# CLAUDE.md — tia-portal-addins

Working instructions for this repo. The full technical documentation (Siemens DLL paths, Publisher mechanics, API surface, gotchas) lives in the README imported at the bottom — **do not duplicate any of it here**.

## Language

- **All documentation, code, comments and commit messages: English.**
- **Conversation with the user: Spanish.**

## Rules not to break

- **V20 — never add `Siemens.Engineering.dll`** to a project that already references `Siemens.Engineering.AddIn.dll`. The latter already carries the full object model (2269 types, including `HW.*` and `SW.Blocks.*`). Referencing both makes `IEngineeringObject` ambiguous and forces `extern alias`. Only reconsider if a specific type actually fails to compile.
- **V21 — the model is inverted**: `Siemens.Engineering.AddIn.Base.dll` holds only 71 infrastructure types, so `Siemens.Engineering.Base.dll` **must** also be referenced for `TiaPortal`, `Project`, `IEngineeringObject` and `NotificationIcon`. Nothing is duplicated there, so no `extern alias` is ever needed. `PlcBlock` lives in `Siemens.Engineering.Step7.dll`; there is no `Siemens.Engineering.Hmi.dll`.
- **The V21 Publisher is in `V21\`, not `V21\net48\`** — it cannot be derived from the assembly path the way it can in V20.
- **`PlatformTarget` = `x64`** in every project TIA loads into its process.
- **`<Private>False</Private>`** on every reference to a Siemens assembly.
- **Siemens DLL paths parameterized** through `$(SiemensPublicApi)`, never hardcoded in each `HintPath`.
- Classes TIA instantiates by reflection (`AddInProvider`, `AddInController`) must be **`public`**. If they are `internal` the build succeeds and the Add-In never appears — a silent failure. Adapters are `internal sealed`.
- **Where a type goes is decided by its dependencies**, per the three-layer rule below. Both `Core` and `AddIn.Shared` stay **AnyCPU**; only the version projects are `x64`.
- **Adapters convert Siemens types into primitives or DTOs before crossing a layer.** `Project` stays in the version project; the action receives `project?.Name`.
- **Never name a namespace `AddIn.Core`.** Inside `namespace AddIn`, the identifier `Core` would then resolve to `AddIn.Core` before the global `Core`, breaking every qualified reference with a misleading "type does not exist" error.
- Any assembly beyond the Add-In's own must be declared in `Config.xml` under **`AdditionalAssemblies`**, or TIA throws `FileNotFoundException` at runtime even though everything built and packaged cleanly.
- Assets live once in `assets/`, grouped **by feature**, and are embedded **in `AddIn.Shared`** as `EmbeddedResource` (never `Content`: the `.addin` only carries assemblies). Use a glob, not a list.
- **The loader and the assets cannot be separated.** Embedded resources are scoped to the assembly that carries them, and `Assets` resolves against `typeof(Assets).Assembly`. Moving `Assets.cs` without moving the `EmbeddedResource` glob makes every lookup return `null` — **silently**, because `Open` returns `null` by design so callers degrade.
- **Never hardcode a resource-name prefix.** Match on the tail of the name — a stale prefix compiles fine and returns `null` at runtime inside TIA.
- **`AddIn.Shared.Assets.Open(path)` returns a `Stream`, never an image type.** The lookup is worth sharing; the type is not. `Adapters/Icons` materialises a `System.Drawing.Icon` for the TIA menu, and a WPF consumer would build an `ImageSource` from the same bytes. Keeping the lookup free of `System.Drawing` is what allows both.

## Naming convention (settled 2026-08-29)

| Item | `Core` | `AddIn.Shared` | `AddIn.V20` | `AddIn.V21` |
|---|---|---|---|---|
| Project / folder | `Core` | `AddIn.Shared` | `AddIn.V20` | `AddIn.V21` |
| `AssemblyName` | `PLC-Framework.Core` | `PLC-Framework.AddIn.Shared` | `PLC-Framework.V20` | `PLC-Framework.V21` |
| `RootNamespace` | `Core` | `AddIn.Shared` | `AddIn` | `AddIn` |

The namespace says which layer a type is in: `AddIn.Shared.*` is version-agnostic, `AddIn.*` is version-specific.

**PascalCase throughout**, TIA version suffixes included (`V20`, `V21`). The version is not repeated in the namespace because the project name already carries it.

`AddIn` is spelled with a capital `I` everywhere — project, folder, namespace and class names — matching Siemens' own `Siemens.Engineering.AddIn`.

Two things stay lowercase, and both are deliberate:

- the repo and solution file name, `tia-portal-addins`;
- the **`.addin` package extension**, which is what the Publisher requires.

> This replaces an earlier all-lowercase convention. Anything still written as `core`, `addin` or `.v20` is a leftover, not a deliberate choice.

Hyphens are legal in an `AssemblyName` and illegal in a C# namespace.

## Architecture decisions already made

Do not relitigate without new information:

1. **Three layers, decided by dependency set** — see below.
2. **Two Add-In projects**: `AddIn.V20` (valid V17–V20) and `AddIn.V21`. V21 breaks binary compatibility and splits the assemblies.
3. **Satellites are separate `.exe` files, and cannot be otherwise.** Verified: the Publisher **silently drops** any `.exe` listed under `AdditionalAssemblies` — no error, no warning, the package is just built without it. The filter is purely by extension (the same PE renamed to `.dll` is packaged), but that is no workaround: the part lands in `LocalAssemblyCache/` and TIA loads it as an assembly inside its own process, never as a file on disk. Siemens' own `Siemens.Engineering.AddIn.Utilities.Process` wrapper plus `ProcessStartPermission` confirm launching an external executable is the sanctioned path.
4. **Install location** — `%ProgramData%\PLC-Framework\`, exposed by `Core.InstallPaths`, holding `.env` and a single `tools\` folder for **every** executable shipped, satellites included. **Per machine, not per user.** **Not** next to the `.addin`: `UserAddIns` is per TIA version (V20 and V21 would each need a copy) and belongs to Siemens. **Not** derived from `Assembly.Location` either: TIA loads the Add-In out of the package, so that path cannot be trusted. `PLC_FRAMEWORK_HOME` overrides it for staging. Installing needs elevation once; reading does not.
   - **"Satellite" is a role, not a location.** A satellite is a WPF app the Add-In launches from the menu; it lives in `tools\` next to any command-line helper. Splitting the folder by role was tried and reverted — it only invited arguments about which half a new executable belonged in.
5. **Add-In ↔ satellite communication**: JSON snapshot through a temp file when data at open time is enough. Two live options exist: Siemens' `Process` wrapper supports **redirected stdin/stdout** with `OutputDataReceived` and `Exited`, which is simpler than named pipes and needs no `IPC` project. Reach for named pipes only if stdio proves insufficient. **Prefer the snapshot** until hitting a real limitation.
6. **Avoiding duplicates**: each satellite takes a named `Mutex` at startup.
7. **No Siemens API exposes the Add-In's own path or the running TIA version.** Checked `TiaPortal` and the whole `Siemens.Engineering.AddIn` root namespace. Anything needing a location must derive it from a well-known folder.

### The three layers — this decides where every new type goes

A type's layer is decided by **what it depends on**, not by who happens to call it today:

| Layer | May depend on | Holds |
|---|---|---|
| `Core` | only what **every** consumer needs | `Config` model + loader, `DependencyGraph` model, `InstallPaths`, `Product` |
| `AddIn.Shared` | `Core` + host types that are **not** Siemens | the Add-In's use cases (`Actions/`), its ports (`ITiaNotifier`, `IGroupNode`, `HierarchyTargets`, `Icons`), and the embedded `assets\` with their `Assets` loader |
| `UI.Shared` | `Core` + WPF | brand resources (`BrandLogo.xaml`, `Theme.xaml`) and, later, shared windows and the single-instance guard |
| `AddIn.V20` / `.V21` | anything, including Siemens | `AddInProvider`, `AddInController`, `Adapters/` implementing the ports |
| `Satellite.<Name>` / `Tool.<Name>` | `Core`, plus `UI.Shared` when it has a window | one executable each |

Verified from the compiled assemblies:

```
PLC-Framework.Core          -> mscorlib, System.Core, System.Runtime.Serialization
PLC-Framework.AddIn.Shared  -> + PLC-Framework.Core, System.Drawing
PLC-Framework.UI.Shared     -> WPF only (today it is pure XAML, so it emits almost nothing)
PLC-Framework.V20           -> + Siemens.Engineering.AddIn
```

**`UI.Shared` is named after the concern, not the consumer.** Anything with a window wants the logo and the palette, whether it is a satellite the Add-In launches or a command-line tool that shows a dialog. Naming it `Satellite.Shared` would have broken the rule above in the name itself. `AddIn.Shared` keeps its consumer-shaped name because its contents genuinely are Add-In vocabulary.

A useful consequence: **if a project references `UI.Shared`, it has a GUI.** The dependency states what a name prefix only suggests.

Consequences worth remembering:

- **`System.Drawing` must not reach `Core`.** `Core` is loaded inside TIA Portal's process, and its dependencies are the **intersection** of its consumers' needs, never the union. With `assets\` now in `AddIn.Shared` nothing in `Core` could pull it in — keep it that way.
- **Ports live with the layer whose vocabulary they speak.** `IGroupNode` talks about PLC group trees, so it belongs to `AddIn.Shared`, not `Core` — even though it has no Siemens reference.
- **`assets\` moved from `Core` to `AddIn.Shared` on 2026-09-04.** `Assets` had exactly one caller, `Adapters/Icons`, and no consumer outside the Add-Ins. What would move it back is a satellite or a tool needing an asset **at runtime**: the brand files are consumed at build time instead — `favicon.svg` was hand-converted into `UI.Shared/Resources/BrandLogo.xaml`, `favicon.ico` is the satellites' `ApplicationIcon` — and neither goes through the loader. Moving it back means moving the glob with it.
- Check the layering with the assembly metadata (`GetReferencedAssemblies`), not by reading `using` statements.

### Sharing code between `AddIn.V20` and `AddIn.V21`

Three mechanisms, each for a different case:

| Situation | Mechanism |
|---|---|
| Code that **diverges** between versions | port in `AddIn.Shared` + one thin adapter per version |
| Identical code with **no** Siemens dependency | put it in `AddIn.Shared` — one binary serves both |
| Identical code **with** Siemens dependencies | must be compiled twice; a shared assembly is impossible |

That last row is not a preference: `Siemens.Engineering.AddIn` (V20) and `Siemens.Engineering.AddIn.Base` (V21) are **different assembly names with different public key tokens**, and V21 does not ship the V20 one. A shared binary would bind to one identity and fail to load in the other host. Source linking is the only option there; today `AddInProvider.cs` and `TiaGroupNode.cs` are simply duplicated instead.

`#if V20 / #if V21` remains rejected: it degrades fast and makes menu code unreadable.

### Shipping `Core` — settled, do not relitigate

`Core` and `AddIn.Shared` travel inside each `.addin` via `AdditionalAssemblies` in `Config.xml` — **one entry per assembly; transitive project references are not packaged automatically**. **Do not propose merging the DLLs** with ILRepack or Costura.Fody: the `.addin` is already a single deployable file, `AdditionalAssemblies` is the vendor-supported mechanism, and the satellites will need `Core` as an assembly with one identity anyway.

## Status (2026-08-29)

- [x] `Core`, `AddIn.V20` and `AddIn.V21` created, all in the `.slnx`.
- [x] Both Add-Ins **validated end to end**: build → `.addin` containing `Core` → load and run correctly in TIA Portal V20 / V21 on the VM.
- [x] `AddIn.Shared` created: the Add-In's version-agnostic layer. Ports `ITiaNotifier` / `IGroupNode` implemented per version in `AddIn.VXX/Adapters/`.
- [x] `ConfigLoader` reading the real `config.json`, and `CreateProjectHierarchyAction` migrated from the old project — the four duplicated recursive walks collapsed into one, exercised with fakes and **no TIA installed**.
- [x] Icons working end to end: `assets/` by feature → embedded by glob → `Adapters/Icons.cs` → `AddActionItemWithIcon`, verified in TIA on the VM.
- [x] `config.json` and `core.json` models complete in `Core.Config` / `Core.DependencyGraph`, cross-checked key by key against the real files **and** against the generator's own models in `code/tools/dependency_graph_builder`.
- [ ] `IPC`, satellites

### Known V20/V21 divergence

The menu and provider API is identical across versions, but the two are **not source-compatible**. Found so far: the message box. V20 uses `tiaPortal.GetMessageBox()` returning `MessageBox`; V21 removed that extension and uses `tiaPortal.GetService<MessageBoxProvider>()` (which can return `null`). That single line is now the only difference between the two `TiaNotifier.cs` files — every new divergence should be pushed behind a `Core` port the same way.

### `config.json` pipeline — designed, not yet built

`config.json` has **two producers** (a future WPF satellite with a UI, and the user editing by hand) and **one consumer** (the Add-In). Decisions taken:

- **DTOs stay permissive**, validation is a separate pass. A serializer that throws stops at the first problem and loses the rest; a validator reports all of them with their location.
- **Validation lives in `Core` and runs twice**: in the satellite before saving, in the Add-In after loading. Validating only in the UI is useless — hand-editing bypasses it.
- **Scoped per concern, not per document.** Each action reads only its section, so no section is globally required; "required" belongs to the concern. The satellite runs the composite validator, an action runs only its own.
- **Structural vs environmental** validation kept apart: closed value sets, required fields, internal references and regex-that-compile are pure; checking a path exists or a `${VAR}` resolves touches disk and belongs in a separate method.
- `coreSource` is `local | remote`, and it decides which repository section is required.
- The `.env` lookup is environment-specific (Add-In vs satellite resolve it differently) → it becomes a `Core` port, with `${VAR}` expansion pure in `Core`.
- A **JSON Schema** is the highest-leverage addition for the hand-edit path: it validates while the user types, before any of the above runs.

### Pending

- [ ] `Config.Load` — the loader. `Stream` overload as the primitive, `path` as convenience
- [ ] Structural validator per concern, then the environmental one
- [ ] `config.schema.json` referenced from the file itself via `$schema`
- [ ] `.env` reader over `InstallPaths.EnvFile` + `${VAR}` expansion, both in `Core`. The `ISecretLookup` port is probably unnecessary now: with a deterministic path, the Add-In and the satellites read the same file with the same code
- [ ] Verify on the VM what `Assembly.GetExecutingAssembly().Location` returns for a loaded `.addin`. No longer blocking anything, but worth knowing — the old project assumed `UserAddIns` and may well get an empty string
- [ ] **Decide**: migrate the rest of the domain logic from `add-in-for-tia-portal` into `Core`, or leave it aside. Deferred on 2026-08-23
- [ ] Automate deployment of the `.addin` to the VM (currently a manual copy)
- [ ] Try the TIA Add-in Tester and/or `Siemens.Engineering.AddIn.DebugStarter.exe` to shorten the test cycle
- [ ] Decide how many satellites there will be and whether they need live TIA data or just a snapshot

---

@README.md
