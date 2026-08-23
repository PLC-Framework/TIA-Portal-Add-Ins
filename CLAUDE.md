# CLAUDE.md — tia-portal-addins

Working instructions for this repo. The full technical documentation (Siemens DLL paths, Publisher mechanics, API surface, gotchas) lives in the README imported at the bottom — **do not duplicate any of it here**.

## Language

- **All documentation, code, comments and commit messages: English.**
- **Conversation with the user: Spanish.**

## Rules not to break

- **Never add `Siemens.Engineering.dll`** to a project that already references `Siemens.Engineering.AddIn.dll`. The latter already carries the full object model (2269 types, including `HW.*` and `SW.Blocks.*`). Referencing both makes `IEngineeringObject` ambiguous and forces `extern alias`. Only reconsider if a specific type actually fails to compile.
- **`PlatformTarget` = `x64`** in every project TIA loads into its process.
- **`<Private>False</Private>`** on every reference to a Siemens assembly.
- **Siemens DLL paths parameterized** through `$(SiemensPublicApi)`, never hardcoded in each `HintPath`.
- Classes TIA instantiates by reflection (`AddInProvider`, `AddInController`) must be **`public`**. If they are `internal` the build succeeds and the Add-In never appears — a silent failure.
- `Core` **references nothing from Siemens**. If an `IEngineeringObject` seems to belong there, that type stays in the Add-In project instead.

## Naming convention (settled)

Project `addin.v20` · `AssemblyName` `PLC-Framework.v20` · `RootNamespace` `addin`.

Lowercase, and the version is not repeated in the namespace because the project name already carries it. **Do not propose PascalCase**: this was a deliberate decision by the user, not an oversight.

Hyphens are legal in an `AssemblyName` and illegal in a C# namespace.

## Architecture decisions already made

Do not relitigate without new information:

1. **Layers**: `Core` (no Siemens) → Add-In → satellites.
2. **Two Add-In projects**: `addin.v20` (valid V17–V20) and `addin.v21`. V21 breaks binary compatibility and splits the assemblies.
3. **Satellites**: standalone WPF apps launched with `Process.Start`.
4. **Add-In ↔ satellite communication**: JSON snapshot through a temp file when data at open time is enough; named pipes plus an `IPC` project only if live queries are required. **Prefer the snapshot** until hitting a real limitation.
5. **Avoiding duplicates**: each satellite takes a named `Mutex` at startup.

### Sharing code between `addin.v20` and `addin.v21`

V21 rules out a single binary. Options, best to worst:

1. **An interface in `Core` plus two independent implementations** — no build tricks, no `#if`. ← start here
2. **Shared Project (`.shproj`) or linked files** — move here if the second adapter turns out to be a 90% copy of the first.
3. **`#if V20 / #if V21`** — avoid: it degrades fast and makes menu code unreadable.

## Status (2026-08-23)

- [x] `addin.v20` created and **validated end to end**: builds → produces the `.addin` → loads correctly in TIA Portal V20 on the VM. It is a hello world with one context-menu entry.
- [x] Added to the `.slnx`
- [ ] `addin.v21`, `Core`, `IPC`, satellites

### Pending

- [ ] Create `Core` and extract into it the first interface isolating the TIA API
- [ ] Create `addin.v21` implementing that interface against `V21\net48` — this is what proves whether the abstraction holds
- [ ] `Config.xml` for V21 with its own `.../Publisher/V21` `xmlns`
- [ ] **Decide**: migrate the domain logic from `add-in-for-tia-portal` into `Core`, or leave it aside. Deferred on 2026-08-23
- [ ] Automate deployment of the `.addin` to the VM (currently a manual copy)
- [ ] Try the TIA Add-in Tester and/or `Siemens.Engineering.AddIn.DebugStarter.exe` to shorten the test cycle
- [ ] Decide how many satellites there will be and whether they need live TIA data or just a snapshot

---

@README.md
