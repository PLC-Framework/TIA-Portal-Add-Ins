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
- **TIA runs an Add-In in partial trust.** Confirmed on the VM by a `System.Security.SecurityException` out of `DataContractJsonSerializer.WriteObject`: *"the data contract type ... is not serializable in partial trust because it is not public"*. Any type a serializer touches must be **visible** — public, and public all the way out if nested. The sandbox is real and it is tight; when something works here and fails there, suspect trust before suspecting logic. A restricted `AppDomain` (`PermissionSet` with only `SecurityPermissionFlag.Execution`) reproduces it without TIA.
- Classes TIA instantiates by reflection (`AddInProvider`, `AddInController`) must be **`public`**. If they are `internal` the build succeeds and the Add-In never appears — a silent failure. Adapters are `internal sealed`.
- **Where a type goes is decided by its dependencies**, per the three-layer rule below. Both `Core` and `AddIn.Shared` stay **AnyCPU**; only the version projects are `x64`.
- **Adapters convert Siemens types into primitives or DTOs before crossing a layer.** `Project` stays in the version project; the action receives `project?.Name`.
- **Never name a namespace `AddIn.Core`.** Inside `namespace AddIn`, the identifier `Core` would then resolve to `AddIn.Core` before the global `Core`, breaking every qualified reference with a misleading "type does not exist" error.
- Any assembly beyond the Add-In's own must be declared in `Config.xml` under **`AdditionalAssemblies`**, or TIA throws `FileNotFoundException` at runtime even though everything built and packaged cleanly.
- Assets live once in `assets/`, grouped **by feature**, and are embedded **in `AddIn.Shared`** as `EmbeddedResource` (never `Content`: the `.addin` only carries assemblies). Use a glob, not a list.
- **The loader and the assets cannot be separated.** Embedded resources are scoped to the assembly that carries them, and `Assets` resolves against `typeof(Assets).Assembly`. Moving `Assets.cs` without moving the `EmbeddedResource` glob makes every lookup return `null` — **silently**, because `Open` returns `null` by design so callers degrade.
- **Never hardcode a resource-name prefix.** Match on the tail of the name — a stale prefix compiles fine and returns `null` at runtime inside TIA.
- **Theming a WPF input control takes a `ControlTemplate`, not `Setter`s.** Background, foreground, caret and selection are properties; **hover and focus are template triggers painting from hardcoded brushes**, so a styled field still flashes Windows-blue when touched. One `ControlTemplate` with `TargetType="Control"` serves `TextBox` and `PasswordBox` alike — both are `TextBoxBase`, which locates its editing surface by the name `PART_ContentHost`. Two consequences bite: an implicit `TextBox` style also lands on an editable `ComboBox`'s `PART_EditableTextBox` (so its padding must be zeroed against the combo's fixed height), and that same part is transparent by design, so a disabled trigger must dim the border and text but **never** repaint the background.
- **`AddIn.Shared.Assets.Open(path)` returns a `Stream`, never an image type.** The lookup is worth sharing; the type is not. `Adapters/Icons` materialises a `System.Drawing.Icon` for the TIA menu, and a WPF consumer would build an `ImageSource` from the same bytes. Keeping the lookup free of `System.Drawing` is what allows both.

## Naming convention (settled 2026-08-29)

| Project / folder | `AssemblyName` | `RootNamespace` |
|---|---|---|
| `Core` | `PLC-Framework.Core` | `Core` |
| `AddIn.Shared` | `PLC-Framework.AddIn.Shared` | `AddIn.Shared` |
| `UI.Shared` | `PLC-Framework.UI.Shared` | `UI.Shared` |
| `S7PlcWebserverApi` | `PLC-Framework.S7PlcWebserverApi` | `S7PlcWebserverApi` |
| `AddIn.V20` | `PLC-Framework.V20` | `AddIn` |
| `AddIn.V21` | `PLC-Framework.V21` | `AddIn` |
| `Satellite.<Name>` | `PLC-Framework.Satellite.<Name>` | `Satellite.<Name>` |

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
5. **Add-In → satellite handoff: a JSON document over stdin** (chosen 2026-09-04). Siemens' `Process` wrapper exposes `RedirectStandardInput`, so the Add-In writes the payload straight into the child and no file is ever created — nothing to clean up, no permissions question, no stale handoff from a crashed run. **Verified end to end on the VM (2026-09-04): the redirection does survive the permission sandbox.** The satellite still reads the payload behind a small abstraction, and the fallback — a `%TEMP%` file whose path arrives as an argument — stays wired as reserve rather than as a bet. Not the TIA project folder: those are under version control, and tying the handoff to project writability makes a read-only project fail before the window even opens.
6. **Avoiding duplicates**: each satellite takes a named `Mutex` at startup — **except `Satellite.DataBlockSnapshot`**, which may run several times at once. Each of its runs is a job with its own PLC, its own blocks and its own destination; a single instance would either refuse the second launch or silently discard the selection that came with it. The rule stands for satellites that show *state*; it does not for one that performs a *job*.
7. **No Siemens API exposes the Add-In's own path or the running TIA version.** Checked `TiaPortal` and the whole `Siemens.Engineering.AddIn` root namespace. Anything needing a location must derive it from a well-known folder.
8. **Satellites stay multi-file and stay on `net48` — for now.** A single-file `.exe` was considered on 2026-09-04 and deferred. ILRepack / ILMerge is ruled out outright: `App.xaml` merges its dictionaries by **pack URI, which carries the assembly name**, so merging breaks resource resolution at runtime with no build error. Costura.Fody does work and stays available. Worth knowing: **nothing binds a satellite to `net48`** — that constraint comes from Openness, which a satellite never touches — so multi-targeting `Core` and `UI.Shared` would give `PublishSingleFile` for free, at the price of a .NET runtime on the station. The real cost is the manual copy, not the file count, and the deployment automation already on the pending list fixes that for the `.addin` and the satellite at once.

### The three layers — this decides where every new type goes

A type's layer is decided by **what it depends on**, not by who happens to call it today:

| Layer | May depend on | Holds |
|---|---|---|
| `Core` | only what **every** consumer needs | `Config` model + loader + **validators**, `Secrets` (`.env`, `${VAR}`), `DependencyGraph` model, `InstallPaths`, `Product` |
| `AddIn.Shared` | `Core` + host types that are **not** Siemens | the Add-In's use cases (`Actions/`), its ports (`ITiaNotifier`, `IGroupNode`, `IProcessLauncher`, `HierarchyTargets`, `Icons`), and the embedded `assets\` with their `Assets` loader |
| `UI.Shared` | `Core` + WPF | brand resources (`BrandLogo.xaml`, `Theme.xaml`) and, later, shared windows and the single-instance guard |
| `S7PlcWebserverApi` | the network, and nothing of ours | the JSON-RPC client for a CPU's web server: `PlcClient`, `PlcVariable`, `PlcValue`, `PlcLimits` |
| `AddIn.V20` / `.V21` | anything, including Siemens | `AddInProvider`, `AddInController`, `Adapters/` implementing the ports |
| `Satellite.<Name>` / `Tool.<Name>` | `Core`, plus `UI.Shared` when it has a window | one executable each |

Verified from the compiled assemblies:

```
PLC-Framework.Core                -> mscorlib, System, System.Core, System.Runtime.Serialization
PLC-Framework.AddIn.Shared        -> + PLC-Framework.Core, System.Drawing
PLC-Framework.UI.Shared           -> WPF only (today it is pure XAML, so it emits almost nothing)
PLC-Framework.S7PlcWebserverApi   -> System.Net.Http, Newtonsoft.Json  (no reference to Core)
PLC-Framework.V20                 -> + Siemens.Engineering.AddIn
```

**`S7PlcWebserverApi` deliberately does not reference `Core`.** It knows nothing about `Product`, `InstallPaths` or the config model, and keeping it that way is what lets it be exercised from PowerShell against a real CPU without dragging the rest of the framework in — which is how every one of its behaviours was verified. It is also why it must never go **into** `Core`: it pulls `System.Net.Http`, and `Core` is loaded inside TIA Portal's process.

**`UI.Shared` is named after the concern, not the consumer.** Anything with a window wants the logo and the palette, whether it is a satellite the Add-In launches or a command-line tool that shows a dialog. Naming it `Satellite.Shared` would have broken the rule above in the name itself. `AddIn.Shared` keeps its consumer-shaped name because its contents genuinely are Add-In vocabulary.

A useful consequence: **if a project references `UI.Shared`, it has a GUI.** The dependency states what a name prefix only suggests.

Consequences worth remembering:

- **`System.Drawing` must not reach `Core`.** `Core` is loaded inside TIA Portal's process, and its dependencies are the **intersection** of its consumers' needs, never the union. With `assets\` now in `AddIn.Shared` nothing in `Core` could pull it in — keep it that way.
- **Ports live with the layer whose vocabulary they speak.** `IGroupNode` talks about PLC group trees, so it belongs to `AddIn.Shared`, not `Core` — even though it has no Siemens reference.
- **`assets\` moved from `Core` to `AddIn.Shared` on 2026-09-04, because the transformation only ever serves the Add-Ins.** `Assets` sat in `Core` on the prediction that a WPF satellite would build an `ImageSource` from the same `.ico` stream. `Satellite.About` disproved it: it takes the logo from `UI.Shared/Resources/BrandLogo.xaml` as a vector for both the window and `Window.Icon`, and its executable icon is an `ApplicationIcon` resolved at build time — two uses of the same brand asset, neither through the loader. Turning a `.ico` into an image at runtime is TIA menu vocabulary, because `AddActionItemWithIcon` takes a `System.Drawing.Icon`; WPF wants vectors or Win32 resources. Moving it back means moving the glob with it, and would need a windowed app that genuinely reads an arbitrary asset at runtime — even then a XAML resource in `UI.Shared` is the likelier answer.
- Check the layering with the assembly metadata (`GetReferencedAssemblies`), not by reading `using` statements.

### `Satellite.DataBlockSnapshot` — settled 2026-09-04

Captures the current value of every variable in the selected data blocks, over the CPU's
web server, and writes them to a workbook. **It is for settings and configuration that do
not change**, not for live process values — which is what makes a sweep lasting tens of
seconds an acceptable way to take a "snapshot".

- **Reliability beats speed, and they are mostly the same axis.** The read window *is* a
  data-quality property; shortening it was never about impatience.
- **Every variable that was browsed gets a row**, holding its value or the reason it could
  not be read. The Python app this was ported from drops the failures, which turns a
  partial capture into one that looks complete — the worst possible outcome here.
- **One file per data block**, named `<ip>-<DB>-snapshot-<timestamp>.xlsx`. A collision
  gets ` (n)` appended rather than overwriting.
- **A file is written only when the block read completely.** An incomplete read is
  reported in the window, with its reason, and leaves nothing on disk to be mistaken for
  a good capture.
- **Destination defaults to `<TIA project>\.plc-framework\exports\`**, editable. The
  project directory arrives in the handoff; `Core.Config.ConfigPaths` owns the literals so
  the Add-In and the satellite cannot drift apart. Check the folder exists and is writable
  *before* reading, not after thirty seconds of work.
- **Credentials are remembered per Windows user, keyed by project directory + PLC name,**
  in `%LOCALAPPDATA%\PLC-Framework\credentials.json`, with the password protected by DPAPI
  at `CurrentUser` scope. Written **only after the CPU has accepted them**, so a typo is
  never stored. The PLC address is an editable combo box filled from the project's own
  addresses. See the credential note below.
- **One PLC per instance**; several blocks of it per run. Several PLCs means several
  windows, which decision 6 now allows.
- **The blocks are only the ones the Add-In passed.** The satellite does not offer to
  browse for more, even though one call to the program root would list them.
- **Existence is always checked before capturing.** The block names come from the TIA
  project, and the operator may point the window at a different CPU.
- **Cancel acts between blocks.** Fine-grained cancellation would mean threading a
  `CancellationToken` through `BrowseDb` and `Read`; not worth it while a block is seconds.
- **The request timeout is a field in the window**, not a constant.
- Running with no handoff at all must keep working, with everything typed by hand. That
  mode is how every layer below the window was verified without TIA installed.

#### Remembering credentials — settled 2026-09-07

A capture session is an hour of launches, and retyping a web server password per launch is
the friction that made this necessary. Four designs were rejected before this one, and the
reasons are worth keeping because each looks reasonable until one fact lands on it:

| Rejected | Why |
|---|---|
| Windows Credential Manager | a second store to manage, and nothing else in the framework uses it |
| `.env` with `PLC_USER` / `PLC_PASSWORD` | **one TIA project holds N CPUs, each with its own user and password.** Two open projects, or ten CPUs in one, and a single pair is overwritten |
| the same pair in `config.json` | same defect, and `config.json` is versioned |
| reusing one window for several captures | decision 6 deliberately allows several instances; one window per PLC is the point |

**The mechanism came from the operator, and it is the better half of the design: the
credentials are learned from use, not written by hand.** What changed was the location.
The first proposal put `tmp.json` inside the TIA project, at
`.plc-framework\tmp\` — but a TIA project is under version control (`.version-control\`
sits right next to `.plc-framework\`), so a credential file there reaches a commit or a
zipped copy sooner or later. Hence:

```
%LOCALAPPDATA%\PLC-Framework\credentials.json     per user, never versioned
```

- **Keyed by project directory + PLC name.** Both already arrive in the handoff, so the
  Add-In needed no change at all. The device name is the key rather than the address
  because a CPU can be readdressed and has several addresses anyway; the address is the
  fallback for a window started by hand, where no project handed a name over.
- **The password is DPAPI-protected at `CurrentUser` scope**, with application entropy so a
  blob from another program cannot be dropped in and decrypted. The blunt consequence:
  **it does not travel.** Another user, or another machine, gets nothing back and types the
  credentials again. That is the price of not having a shared key, and **a key baked into
  the `.exe` is not encryption but obfuscation — worse than plaintext, because it looks
  safe.**
- **Saved only after `client.Login()` succeeds**, never on clicking Capture. `CaptureRunner`
  takes an `authenticated` callback it invokes on the line after the login and nowhere
  else; the runner stays ignorant of credential storage. A wrong password is therefore
  never persisted, and never has to be un-persisted.
- **A *Remember* checkbox next to the password**, pre-ticked when something is already
  stored for that CPU. Unticking it and capturing **forgets** the stored entry — that is
  what unticking means. It starts unticked on a CPU never captured before: storing a
  password nobody asked to store is not a default worth having.
- **Nothing in the store throws.** A corrupt file, a blob written by another user, a
  missing directory: all return "nothing remembered" and the window opens normally. A
  credential cache that breaks the application it exists to smooth is worse than none.
- **The fields stay editable and show exactly what was loaded**, so the window looks the
  same as if the operator had typed it.
- **An eye reveals the password.** `PasswordBox` cannot show what it holds and its
  `Password` is not a dependency property, so the only way is a twin `TextBox` in the same
  cell with one of the two always collapsed; the eye swaps them and carries the text
  across, then moves the focus and the caret so typing does not fall into a field that is
  no longer on screen. Everything else asks `CurrentPassword()` rather than either control,
  so no caller has to know the value lives in two places. The icon shows a **struck-through
  eye while the password is on screen** — it states what is true now, not what clicking
  would do. Worth remembering when a credential is remembered rather than typed: revealing
  it is the only way to check *which* password came back.

This leaves `.env` and `${VAR}` expansion untouched as pending work — they are still what
`${GITHUB_TOKEN}` in `config.json` needs, and that one genuinely is a shared team secret.

### `Satellite.ConfigEditor` — decided 2026-09-08, not yet built

Edits `config.json` with a UI. Launched from the menu by `ConfigEditorAction`, labelled
**"Config. Editor"**. The contract it enforces is the README table; these are the decisions
about the application itself:

- **Missing `.plc-framework\` or missing `config.json` is a normal state, not an error.**
  The window says so and offers to create one, folders included.
- **The template is `config.template.json`, embedded *and* on disk at
  `%LOCALAPPDATA%\PLC-Framework\`.** The file on disk wins when it is present and parses;
  otherwise the embedded copy is used **and written out**, so a station repairs itself and
  the operator gets something to customise. Seeded from the `config.json` in `.example\`.
  **Per user, not `tools\`** — that was the first plan and it carries the same defect as the
  old `.env`: `tools\` lives under `%ProgramData%`, where ordinary users have read and
  execute but **not write**, so the self-repair would work on a developer's machine and
  fail on a real station. Anything this satellite *writes* goes to the per-user folder;
  `%ProgramData%` is for what the installer puts there.
- **One instance per running TIA Portal**, and the satellite works that out **by itself**:
  it reads its own parent process, which is TIA because `ProcessLauncher` starts it with
  `UseShellExecute=false`. **The Add-In cannot supply the PID** — measured in the restricted
  `AppDomain`, `Process.GetCurrentProcess()` throws `SecurityException` under partial trust,
  for `Id` and `ProcessName` alike, and `AddIn.Utilities` holds only `Process` and
  `ProcessStartInfo`, neither of which offers it. Reading the parent costs ~170 ms once at
  startup, cannot be forged by editing the handoff, and degrades correctly when started by
  hand: no TIA parent, no tie. The mutex name hashes it — `SingleInstance.Claim` builds
  `Local\<Product>.<appId>`, and a backslash **separates the mutex namespace**, so no raw
  path can go in there.
- **`Newtonsoft.Json` for reading and writing**, in the satellite only — `Core` keeps
  `DataContractJsonSerializer`, which is fine for loading but wrong for saving: it does not
  indent, it orders members its own way, and a read-write round trip **drops every key the
  model does not know**. This file is meant to be hand-edited, so edits are surgical over
  the JSON tree, leaving untouched anything the editor did not change.
- **The GitHub token never enters `config.json`.** The file always carries the literal
  `${GITHUB_TOKEN}`; the secret goes to the `.env`, which **moves to
  `%LOCALAPPDATA%\PLC-Framework\.env`** for two reasons — the token is personal, and
  `%ProgramData%` is not writable by ordinary users, so an editor saving there would work
  here and fail on a real station. The field is masked with the same show/hide twin-control
  as the PLC password.
- **The validator comes first.** It is `Core`'s, this satellite is its first consumer, and
  the Add-In needs it too.

### Sharing code between `AddIn.V20` and `AddIn.V21`

Three mechanisms, each for a different case:

| Situation | Mechanism |
|---|---|
| Code that **diverges** between versions | port in `AddIn.Shared` + one thin adapter per version |
| Identical code with **no** Siemens dependency | put it in `AddIn.Shared` — one binary serves both |
| Identical code **with** Siemens dependencies | must be compiled twice; a shared assembly is impossible |

That last row is not a preference: `Siemens.Engineering.AddIn` (V20) and `Siemens.Engineering.AddIn.Base` (V21) are **different assembly names with different public key tokens**, and V21 does not ship the V20 one. A shared binary would bind to one identity and fail to load in the other host. Source linking is the only option there; today `AddInProvider.cs` and `TiaGroupNode.cs` are simply duplicated instead.

**`Siemens.Engineering.AddIn.Utilities` is the subtler case, and the one worth remembering.** Its `Process` wrapper has an identical public surface in both versions — same namespace, same type, same members — but the assembly carries a **different public key token** in each: `65b871d8372d6a8f` in V20, `29bfe5fdf4ba5d3b` in V21. Source-identical code still cannot be compiled once. That is why `Adapters/ProcessLauncher.cs` is duplicated behind `IProcessLauncher`: the two files are byte-identical and nothing in them hints at why, so the reason lives here.

`#if V20 / #if V21` remains rejected: it degrades fast and makes menu code unreadable.

### Shipping `Core` — settled, do not relitigate

`Core` and `AddIn.Shared` travel inside each `.addin` via `AdditionalAssemblies` in `Config.xml` — **one entry per assembly; transitive project references are not packaged automatically**. **Do not propose merging the DLLs** with ILRepack or Costura.Fody: the `.addin` is already a single deployable file, `AdditionalAssemblies` is the vendor-supported mechanism, and the satellites will need `Core` as an assembly with one identity anyway.

## Status (2026-09-04)

- [x] `Core`, `AddIn.V20` and `AddIn.V21` created, all in the `.slnx`.
- [x] Both Add-Ins **validated end to end**: build → `.addin` containing `Core` → load and run correctly in TIA Portal V20 / V21 on the VM.
- [x] `AddIn.Shared` created: the Add-In's version-agnostic layer. Ports `ITiaNotifier` / `IGroupNode` / `IProcessLauncher` implemented per version in `AddIn.VXX/Adapters/`.
- [x] `ConfigLoader` reading the real `config.json`, and `CreateProjectHierarchyAction` migrated from the old project — the four duplicated recursive walks collapsed into one, exercised with fakes and **no TIA installed**.
- [x] Icons working end to end: `assets/` by feature → embedded by glob → `Adapters/Icons.cs` → `AddActionItemWithIcon`, verified in TIA on the VM.
- [x] `config.json` and `core.json` models complete in `Core.Config` / `Core.DependencyGraph`, cross-checked key by key against the real files **and** against the generator's own models in `code/tools/dependency_graph_builder`.
- [x] `UI.Shared` created: `BrandLogo.xaml` (the SVG as a vector `DrawingImage`), `Theme.xaml`, and the `SingleInstance` guard.
- [x] **First satellite complete**: `Satellite.About` — window, single-instance guard and `ApplicationIcon`, each verified by running the real binary here, not by inspecting XAML.
- [x] `AboutAction` launches it from the menu through `IProcessLauncher`. All four paths exercised with fakes, and **run from the menu inside TIA on the VM**.
- [x] `S7PlcWebserverApi` created and **validated against two real CPUs** — an S7-1500 and an S7-1200 G2 — reading a data block of 8,910 variables with zero failures. Session, browse with array expansion, and batched reads.
- [x] The XLSX exporter in `Satellite.DataBlockSnapshot`, checked with the OpenXML SDK's own validator (0 errors) and by inspecting the package XML: typed cells, invariant numbers, and no omitted cell that could pass for an unread value.
- [x] `Satellite.DataBlockSnapshot` complete: window, handoff, capture and export. Its layers were each verified against two real CPUs from PowerShell, without TIA.
- [x] **The whole chain validated in TIA on the VM (2026-09-04)**: menu entry on a multiple selection of data blocks → `TiaPlcSelection` gathers the CPU and its addresses → JSON over the child's standard input → the satellite opens with everything filled in. This is what proves `RedirectStandardInput` survives partial trust.
- [x] **Credentials remembered per user and per CPU** (2026-09-07), so an hour of captures
      is not an hour of typing. Exercised here: ten CPUs of one project each keeping their
      own pair, the same CPU name in two projects not colliding, re-saving not duplicating,
      passwords with quotes and newlines surviving the round trip, a corrupt file and a
      foreign DPAPI blob both degrading to "nothing remembered", and the window itself
      opening pre-filled with the box ticked. **Not yet verified against a CPU that
      *rejects* the credentials** — both test PLCs were off the network that day, so only
      the connection-failure path was seen to skip the save. Same code path either way, but
      it deserves one run when the network is back.
- [ ] `IPC` — not started, and now unlikely ever to be: decision 5 settles the handoff on stdio.

### The first NuGet dependencies (2026-09-04)

The repo went without packages until the web API client. Two were taken, both confined to
projects that never load inside TIA Portal:

| Package | Where | Why |
|---|---|---|
| `Newtonsoft.Json` 13.0.4 | `S7PlcWebserverApi` | the JSON-RPC `result` is a different shape per method and a variable's `value` arrives as bool, integer, real or string. `DataContractJsonSerializer`, which `Core` uses, is the wrong tool for a document whose type is only known at runtime |
| `DocumentFormat.OpenXml` 3.5.1 | `Satellite.DataBlockSnapshot` | writing a real `.xlsx` by hand is five XML parts in a zip whose only true test is whether Excel opens it. Microsoft's own SDK, MIT, and it ships a validator that can be run in the build loop |

**Neither may reach `Core`, `AddIn.Shared` or a version project.** What loads into TIA's
process stays on the framework's own assemblies.

### Known V20/V21 divergence

The menu and provider API is identical across versions, but the two are **not source-compatible**. Found so far: the message box. V20 uses `tiaPortal.GetMessageBox()` returning `MessageBox`; V21 removed that extension and uses `tiaPortal.GetService<MessageBoxProvider>()` (which can return `null`). That single line is now the only difference between the two `TiaNotifier.cs` files — every new divergence should be pushed behind a `Core` port the same way.

A different kind of divergence, and easier to miss because the source is identical, is the public key token of `Siemens.Engineering.AddIn.Utilities` — see *Sharing code between `AddIn.V20` and `AddIn.V21`* above.

**The class shapes barely moved.** Seven of the eight spine types are identical across versions, base class included; only `ProjectBase` differs, having **lost `Graphics`, `PlantViews` and the two block-compilation flags** and gained `TextCategories`. Those are removals, not relocations — `MultiLingualGraphic` and `PlantView` exist nowhere in V21. Any migrated V20 code touching them stops compiling.

**`.siemens\` holds the reflected reference** for both versions, generated from the assemblies with `GetExportedTypes()`. Consult it before asserting anything about the Openness API, and regenerate rather than hand-edit. It carries type names and counts only, no Siemens code, which is why it can be committed when the DLLs cannot.

### `config.json` pipeline — contract settled 2026-09-08, code not yet built

**The field-by-field contract lives in the README, under *The `config.json` contract*** —
required, type and meaning for every key, plus the closed sets, the internal references and
the uniqueness rules. It is the authority; do not restate it here and do not infer
requiredness from the model, which is permissive on purpose.

The shape of it, worth carrying in your head:

- **`metadata.coreSource` decides the file.** `local` or `remote`, and that selects which of
  the two repository sections is required. The other one is not validated at all.
- **Lists are required but may be empty.** `[]` says "no folders for this concern"; a
  missing key says nothing. Applies to all four `hierarchy` lists and all six of
  `codingStyle`.
- **Two internal references** are what a typo breaks silently, so both are checked:
  `implements` must name an existing `rules[].id`, and `rules[].id` is unique file-wide.
- **`Group.name` is unique among siblings only** — the same name in another branch is fine,
  because they are different folders.
- **A `regex` that does not compile is a structural error**, not an environmental one: it is
  a broken file, not a broken machine.

`config.json` has **two producers** (`Satellite.ConfigEditor`, and the user editing by hand)
and **one consumer** (the Add-In). Decisions taken:

- **DTOs stay permissive**, validation is a separate pass. A serializer that throws stops at the first problem and loses the rest; a validator reports all of them with their location.
- **Validation lives in `Core` and runs twice**: in the satellite before saving, in the Add-In after loading. Validating only in the UI is useless — hand-editing bypasses it.
- **Scoped per concern, not per document.** Each action reads only its section, so no section is globally required; "required" belongs to the concern. The satellite runs the composite validator, an action runs only its own.
- **Structural vs environmental** validation kept apart: closed value sets, required fields, internal references and regex-that-compile are pure; checking a path exists or a `${VAR}` resolves touches disk and belongs in a separate method.
- `coreSource` is `local | remote`, and it decides which repository section is required.
- The `.env` lookup is environment-specific (Add-In vs satellite resolve it differently) → it becomes a `Core` port, with `${VAR}` expansion pure in `Core`.
- A **JSON Schema** is the highest-leverage addition for the hand-edit path: it validates while the user types, before any of the above runs.

### Pending

- [ ] **Building while TIA has the Add-In loaded fails.** The Publisher cannot overwrite a `.addin` that the VM holds open through a shared folder — `vmware-vmx.exe` shows up as the owner. Harmless once understood, but it looks like a build error: close TIA, or stop deploying straight out of `bin\Debug\net48`
- [x] **Structural validator built** (2026-09-09), in `Core/Config/Validation/`: one
      validator per concern plus `ConfigValidator` for the whole document. Exercised against
      the real `config.json` in `.example\` — clean — and against thirty-odd deliberately
      broken variants. `System` joined `Core`'s references for `Regex` and `Uri`; harmless,
      but the layering table was updated rather than left to drift
- [x] **The environmental validator** (2026-09-09): local repository path, its folder and
      its dependency file, plus every `${VAR}` resolving. Separate pass, separate type — it
      touches disk. Deliberately does **not** reach the network
- [x] **`Core.InstallPaths` gained `UserRoot` and `UserFile(name)`** (2026-09-09), and
      `EnvFile` now hangs off them. `CredentialStore` stopped building the path itself.
      `PLC_FRAMEWORK_HOME` moves `Root` only, **not** `UserRoot` — verified — because the
      two answer different questions and the variable exists for elevation, which the
      per-user folder never needs
- [x] **`.env` reader + `${VAR}` expansion in `Core`** (2026-09-09), as `Core/Secrets/`.
      `ISecretLookup` was indeed unnecessary. `Variables` is pure and takes a lookup;
      `DotEnv` owns the file and writes it **surgically**, preserving comments and order
- [ ] `config.schema.json` referenced from the file itself via `$schema`
- [ ] Verify on the VM what `Assembly.GetExecutingAssembly().Location` returns for a loaded `.addin`. No longer blocking anything, but worth knowing — the old project assumed `UserAddIns` and may well get an empty string
- [ ] **Decide**: migrate the rest of the domain logic from `add-in-for-tia-portal` into `Core`, or leave it aside. Deferred on 2026-08-23
- [ ] Automate deployment to the VM — now two copies, the `.addin` into `UserAddIns\` and the satellite into `tools\`. Both are manual today, and this is the item that makes a single-file satellite unnecessary (decision 8)
- [ ] Try the TIA Add-in Tester and/or `Siemens.Engineering.AddIn.DebugStarter.exe` to shorten the test cycle
- [ ] Decide how many satellites there will be and whether they need live TIA data or just a snapshot

---

@README.md
