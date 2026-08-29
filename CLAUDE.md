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
- `Core` **references nothing from Siemens**, and stays **AnyCPU** (the `x64` constraint is the host process's, and the satellites will reference `Core` too). If an `IEngineeringObject` seems to belong there, that type stays in the Add-In instead.
- **Adapters convert Siemens types into primitives or DTOs before crossing into `Core`.** `Project` stays in the Add-In; `Core` receives `project?.Name`.
- **Never name a namespace `AddIn.Core`.** Inside `namespace AddIn`, the identifier `Core` would then resolve to `AddIn.Core` before the global `Core`, breaking every qualified reference with a misleading "type does not exist" error. Adapters live in `AddIn.Adapters`.
- Any assembly beyond the Add-In's own must be declared in `Config.xml` under **`AdditionalAssemblies`**, or TIA throws `FileNotFoundException` at runtime even though everything built and packaged cleanly.

## Naming convention (settled 2026-08-29)

| Item | `Core` | `AddIn.V20` | `AddIn.V21` |
|---|---|---|---|
| Project / folder | `Core` | `AddIn.V20` | `AddIn.V21` |
| `AssemblyName` | `PLC-Framework.Core` | `PLC-Framework.V20` | `PLC-Framework.V21` |
| `RootNamespace` | `Core` | `AddIn` | `AddIn` |

**PascalCase throughout**, TIA version suffixes included (`V20`, `V21`). The version is not repeated in the namespace because the project name already carries it.

`AddIn` is spelled with a capital `I` everywhere — project, folder, namespace and class names — matching Siemens' own `Siemens.Engineering.AddIn`.

Two things stay lowercase, and both are deliberate:

- the repo and solution file name, `tia-portal-addins`;
- the **`.addin` package extension**, which is what the Publisher requires.

> This replaces an earlier all-lowercase convention. Anything still written as `core`, `addin` or `.v20` is a leftover, not a deliberate choice.

Hyphens are legal in an `AssemblyName` and illegal in a C# namespace.

## Architecture decisions already made

Do not relitigate without new information:

1. **Layers**: `Core` (no Siemens, holds the ports) → Add-In `Adapters/` (implement those ports per TIA version) → satellites. Ports go in `Core` as interfaces; anything touching a Siemens type stays in the Add-In.
2. **Two Add-In projects**: `AddIn.V20` (valid V17–V20) and `AddIn.V21`. V21 breaks binary compatibility and splits the assemblies.
3. **Satellites**: standalone WPF apps launched with `Process.Start`.
4. **Add-In ↔ satellite communication**: JSON snapshot through a temp file when data at open time is enough; named pipes plus an `IPC` project only if live queries are required. **Prefer the snapshot** until hitting a real limitation.
5. **Avoiding duplicates**: each satellite takes a named `Mutex` at startup.

### Sharing code between `AddIn.V20` and `AddIn.V21`

**Settled**: a port in `Core` plus one thin adapter per version. Applied to the message box and it worked — the divergence collapsed to a single line. No build tricks, no `#if`, no Shared Project. Keep using this for every new divergence.

`#if V20 / #if V21` remains rejected: it degrades fast and makes menu code unreadable.

### Shipping `Core` — settled, do not relitigate

`Core` travels inside each `.addin` via `AdditionalAssemblies` in `Config.xml`. **Do not propose merging the DLLs** with ILRepack or Costura.Fody: the `.addin` is already a single deployable file, `AdditionalAssemblies` is the vendor-supported mechanism, and the satellites will need `Core` as an assembly with one identity anyway.

## Status (2026-08-23)

- [x] `Core`, `AddIn.V20` and `AddIn.V21` created, all in the `.slnx`.
- [x] Both Add-Ins **validated end to end**: build → `.addin` containing `Core` → load and run correctly in TIA Portal V20 / V21 on the VM.
- [x] First port extracted: `INotifier`, implemented by `AddIn.vXX/Adapters/TiaNotifier.cs`. `HelloWorldAction` lives in `Core` and never sees a Siemens type.
- [ ] `IPC`, satellites

### Known V20/V21 divergence

The menu and provider API is identical across versions, but the two are **not source-compatible**. Found so far: the message box. V20 uses `tiaPortal.GetMessageBox()` returning `MessageBox`; V21 removed that extension and uses `tiaPortal.GetService<MessageBoxProvider>()` (which can return `null`). That single line is now the only difference between the two `TiaNotifier.cs` files — every new divergence should be pushed behind a `Core` port the same way.

### Pending

- [ ] **Decide**: migrate the domain logic from `add-in-for-tia-portal` into `Core`, or leave it aside. Deferred on 2026-08-23
- [ ] Automate deployment of the `.addin` to the VM (currently a manual copy)
- [ ] Try the TIA Add-in Tester and/or `Siemens.Engineering.AddIn.DebugStarter.exe` to shorten the test cycle
- [ ] Decide how many satellites there will be and whether they need live TIA data or just a snapshot

---

@README.md
