# CLAUDE.md — tia-portal-Addins

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
- `core` **references nothing from Siemens**, and stays **AnyCPU** (the `x64` constraint is the host process's, and the satellites will reference `core` too). If an `IEngineeringObject` seems to belong there, that type stays in the Add-In instead.
- **Adapters convert Siemens types into primitives or DTOs before crossing into `core`.** `Project` stays in the Add-In; `core` receives `project?.Name`.
- **Never name a namespace `Addin.core`.** Inside `namespace Addin`, `core` would resolve to `Addin.core` before the global `core`, breaking every qualified reference with a misleading "type does not exist" error. Adapters live in `Addin.Adapters`.
- Any assembly beyond the Add-In's own must be declared in `Config.xml` under **`AdditionalAssemblies`**, or TIA throws `FileNotFoundException` at runtime even though everything built and packaged cleanly.

## Naming convention (settled)

Projects `core` / `Addin.v20` / `Addin.v21` · `AssemblyName` `PLC-Framework.core` / `PLC-Framework.v20` / `PLC-Framework.v21` · `RootNamespace` `core` / `Addin` / `Addin`.

Lowercase, and the version is not repeated in the namespace because the project name already carries it. **Do not propose PascalCase**: this was a deliberate decision by the user, not an oversight.

Hyphens are legal in an `AssemblyName` and illegal in a C# namespace.

## Architecture decisions already made

Do not relitigate without new information:

1. **Layers**: `core` (no Siemens, holds the ports) → Add-In `Adapters/` (implement those ports per TIA version) → satellites. Ports go in `core` as interfaces; anything touching a Siemens type stays in the Add-In.
2. **Two Add-In projects**: `Addin.v20` (valid V17–V20) and `Addin.v21`. V21 breaks binary compatibility and splits the assemblies.
3. **Satellites**: standalone WPF apps launched with `Process.Start`.
4. **Add-In ↔ satellite communication**: JSON snapshot through a temp file when data at open time is enough; named pipes plus an `IPC` project only if live queries are required. **Prefer the snapshot** until hitting a real limitation.
5. **Avoiding duplicates**: each satellite takes a named `Mutex` at startup.

### Sharing code between `Addin.v20` and `Addin.v21`

**Settled**: a port in `core` plus one thin adapter per version. Applied to the message box and it worked — the divergence collapsed to a single line. No build tricks, no `#if`, no Shared Project. Keep using this for every new divergence.

`#if V20 / #if V21` remains rejected: it degrades fast and makes menu code unreadable.

### Shipping `core` — settled, do not relitigate

`core` travels inside each `.Addin` via `AdditionalAssemblies` in `Config.xml`. **Do not propose merging the DLLs** with ILRepack or Costura.Fody: the `.Addin` is already a single deployable file, `AdditionalAssemblies` is the vendor-supported mechanism, and the satellites will need `core` as an assembly with one identity anyway.

## Status (2026-08-23)

- [x] `core`, `Addin.v20` and `Addin.v21` created, all in the `.slnx`.
- [x] Both Add-Ins **validated end to end**: build → `.Addin` containing `core` → load and run correctly in TIA Portal V20 / V21 on the VM.
- [x] First port extracted: `INotifier`, implemented by `Addin.vXX/Adapters/TiaNotifier.cs`. `HelloWorldAction` lives in `core` and never sees a Siemens type.
- [ ] `IPC`, satellites

### Known V20/V21 divergence

The menu and provider API is identical across versions, but the two are **not source-compatible**. Found so far: the message box. V20 uses `tiaPortal.GetMessageBox()` returning `MessageBox`; V21 removed that extension and uses `tiaPortal.GetService<MessageBoxProvider>()` (which can return `null`). That single line is now the only difference between the two `TiaNotifier.cs` files — every new divergence should be pushed behind a `core` port the same way.

### Pending

- [ ] **Decide**: migrate the domain logic from `add-in-for-tia-portal` into `core`, or leave it aside. Deferred on 2026-08-23
- [ ] Automate deployment of the `.Addin` to the VM (currently a manual copy)
- [ ] Try the TIA Add-in Tester and/or `Siemens.Engineering.AddIn.DebugStarter.exe` to shorten the test cycle
- [ ] Decide how many satellites there will be and whether they need live TIA data or just a snapshot

---

@README.md
