# Openness notes

What was learned about Siemens' Add-In API by reading the assemblies rather than the documentation — the reference model, the Publisher, what a `.addin` really is, and the partial-trust sandbox that decides what an Add-In may do.

Two generated pages accompany this one, built from the assemblies themselves with `GetExportedTypes()`:

- [`reference/TIA-Portal-V20-Openness-object-model.md`](reference/TIA-Portal-V20-Openness-object-model.md)
- [`reference/TIA-Portal-V21-Openness-object-model.md`](reference/TIA-Portal-V21-Openness-object-model.md)

Neither contains Siemens code — only type names, member names and counts, which is what lets them live here when the assemblies they describe may not.

## Siemens dependencies

The assemblies live **outside the repo**, in a folder of your choosing — `$(SiemensPublicApi)` in the `.csproj` points at it, with **one folder per version**:

| Folder | Contents |
| --- | --- |
| `V17\`, `V18\`, `V19\`, `V20\` | `Siemens.Engineering.dll`, `Siemens.Engineering.Hmi.dll` |
| `V17.AddIn\`, `V18.AddIn\`, `V19.AddIn\` | `Siemens.Engineering.AddIn.dll`, `.AddIn.Permissions.dll`, `.AddIn.Utilities.dll`, `Siemens.Engineering.Hmi.dll` |
| `V20.addIn\` | the above**plus `Siemens.Engineering.AddIn.Publisher.exe` and `.AddIn.DebugStarter.exe`** (note the lowercase `a` in the folder name) |
| `V21\` | `Siemens.Engineering.AddIn.Publisher.exe` and its `.xsd` |
| `V21\net48\` | the split V21 assemblies |
| `.doc\TIA-Openness\TIA Add-in Tester\` | TIA Add-in Tester v1.1.6557.1192 (entry 109783096) — tests the Add-In without opening TIA Portal |

Other tools, shipped by Siemens under `Support\TIA_Portal_Add-In_Tools\`:

| Folder | Contents |
| --- | --- |
| `Development\` | `.nupkg` + `.vsix` — the official VS template for Add-Ins (TIA V18+) |
| `Trusted_Add-Ins_Certification_Tool\` | `Company_Trusted_Add-In_Certification_Tool.exe` — signs the `.addin` as trusted |

The path is parameterized in the `.csproj` through `$(SiemensPublicApi)`, so it can be overridden without editing the file:

```
dotnet build src\AddIn.V20\AddIn.V20.csproj -p:SiemensPublicApi=D:\some\other\path
```

### The reflected reference in `docs/reference/`

Two pages under `reference/`, generated from the assemblies themselves rather than from documentation, and kept in the repo precisely because the assemblies cannot be. **They share a section order**, so the same question is answered in the same place in both:

| File | Covers |
| --- | --- |
| `S7-exports.md` | what TIA writes when a block, a PLC data type or a tag table leaves the project — the SimaticML document, its interface, what nests inside a member and what belongs to another type — read off the exports under `.example\vci\` (V21) and `.example\vci-v20\` (V20) |
| `TIA-Portal-V20-Openness-object-model.md` | the V20 object model as `Siemens.Engineering.AddIn.dll` declares it — the spine from `TiaPortal` down to a block group, the system/user group pattern, software units, and the Add-In surface |
| `TIA-Portal-V21-Openness-object-model.md` | the same model in V21, where it is **split across sixteen assemblies** — which one declares what, what an Add-In has to reference, and every shape difference found against V20. It does **not** restate the model: seven of the eight spine types are the same surface, which is the finding rather than an omission |

**They were self-contained HTML until 2026-09-12, and Markdown is a correction rather than a preference**: GitHub serves an `.html` file in a repository as source, so once this repo went public both pages were a wall of CSS to anybody who followed a link. Markdown renders, and GitHub draws the mermaid class diagrams natively — the designed layout was costing exactly the readers it was meant to serve. It also leaves `reference/` in one format.

**Neither contains Siemens code** — only type names, member names and counts read with `GetExportedTypes()`, which is what lets them live in the repo when the DLLs they describe may not.

Both record where Siemens' own published object-model diagram is incomplete: it omits `ProjectBase` and `HardwareObject` entirely, and attributes their properties to `Project` and `Device` instead.

### V17–V20 vs V21

V17 through V20 share the same Openness API and are binary compatible. **V21 introduces breaking changes**: up to V20 the API is monolithic, whereas V21 splits it into **sixteen assemblies**. Read from `V21\net48\` with `GetExportedTypes()`:

| Assembly | Types | Carries |
| --- | ---: | --- |
| `Base` | 1,382 | the whole object model:`TiaPortal`, `Project`, `Device`, `DeviceItem`, `Software`, `IEngineeringObject`, `NotificationIcon`, `ExclusiveAccess` |
| `WinCCUnified` | 536 | Unified HMI, including `HmiSoftware` |
| `Step7` | 228 | everything under `SW.*` — `PlcSoftware`, blocks, types, tags, software units |
| `AddIn.Base` | 71 | Add-In infrastructure only: providers, menus,`MessageBoxProvider` |
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

## Packaging and deployment

### 1. The Publisher

`Siemens.Engineering.AddIn.Publisher.exe` turns the compiled `.dll` into the `.addin` TIA loads. The syntax uses **flags**, not positional arguments:

```
Siemens.Engineering.AddIn.Publisher.exe --configuration <Config.xml> --outfile <output.addin> --console
```

Flags: `--configuration/-f`, `--outfile/-o` (optional — defaults to an `.addin` with the same name and folder as the main assembly), `--certificatepassword/-p`, `--logfile/-l`, `--edition/-e`, `--verbose/-v`, `--console/-c`, `--pause/-x`, `--template/-t`, `--skipEngMemberCheck/-s`.

Each project uses the Publisher **for its own version**:

| Project | Path |
| --- | --- |
| `AddIn.V20` (V17–V20) | `.lib\Siemens\PublicAPI\V20.addIn\Siemens.Engineering.AddIn.Publisher.exe` |
| `AddIn.V21` | `.lib\Siemens\PublicAPI\V21\Siemens.Engineering.AddIn.Publisher.exe` — loose in `V21\`, **not** inside `V21\net48\` |

**Gotcha**: the Publisher resolves the config's `<Assembly>` path **relative to the config file's own location**, not to the working directory. The approach taken here is to copy `Config.xml` next to the DLL before invoking it:

```xml
<Target Name="PublishTiaAddIn" AfterTargets="Build" Condition="Exists('$(TiaPublisher)')">
  <Copy SourceFiles="$(MSBuildProjectDirectory)\Config.xml" DestinationFolder="$(TargetDir)" />
  <Exec Command=""$(TiaPublisher)" --configuration "$(TargetDir)Config.xml" --outfile "$(TargetDir)$(TargetName).addin" --console" />
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

### Partial trust, and what it forbids

**TIA runs an Add-In in a restricted sandbox.** That is not a footnote: it decides what the Add-In half of the framework may do, and it fails at run time with no hint at compile time.

It surfaced as a `System.Security.SecurityException` out of `DataContractJsonSerializer.WriteObject`:

```
The data contract type 'AddIn.Shared.Actions.DataBlockSnapshotAction+HandoffPayload'
is not serializable in partial trust because it is not public.
```

The types were `internal`, nested inside the action. Serialization in partial trust needs a **visible** type — public, and public all the way out of any nesting. Moving them to top-level public types in their own file fixed it.

The lesson generalises past serialization: **anything reflective is likelier to be denied inside TIA than outside it**, and the failure arrives as a crash report from the field rather than a red squiggle. It can be reproduced here, though, which is much cheaper than a round trip to the VM:

```csharp
PermissionSet permissions = new PermissionSet(PermissionState.None);
permissions.AddPermission(new SecurityPermission(SecurityPermissionFlag.Execution));
AppDomain sandbox = AppDomain.CreateDomain("partial-trust", null, setup, permissions);
```

That is the tightest partial trust there is, so code that survives it survives TIA.

> **Building fails while TIA has the Add-In loaded.** If the VM reaches `bin\Debug\net48` through a shared folder, the Publisher cannot overwrite the `.addin` and the build stops with `MSB3073`. The Restart Manager names `vmware-vmx.exe` as the owner. Close TIA, or copy the package somewhere else before loading it.

### Why the Add-In cannot find itself

`InstallPaths` resolves a well-known folder rather than asking the running assembly where it is, and that is not caution — it is measured. **Both answers `Assembly.Location` can give inside TIA are unusable, and neither announces itself.**

|  | `Location` | `CodeBase` |
| --- | --- | --- |
| Partial trust, any load | **throws `SecurityException`** | **throws `SecurityException`** |
| Full trust, loaded from bytes | `""` — empty, **not null** | `System.dll` in the GAC |
| Full trust, loaded from a file | the real path | the real path |

Read in the restricted `AppDomain` described above, `Location` demands `FileIOPermission` and is refused. That alone settles it: an Add-In runs in partial trust. But the second row is the one worth remembering, because a host that reads assemblies out of a package loads them from **bytes**, and a byte-loaded assembly has no file to point at:

- `Location` is the **empty string, not null**, so the obvious `if (path == null)` guard does not fire.
- `CodeBase` is not a fallback. Under full trust it answered the *calling* assembly's codebase — `System.dll` from the GAC — which is a perfectly well-formed path to something entirely unrelated, and would be believed.

And an empty `Location` then fails **quietly** rather than loudly:

```
Path.Combine("", "tools", "app.exe")      -> "tools\app.exe"        relative, no exception
Path.GetFullPath(Path.Combine("", "x"))   -> <current directory>\x  confident, and wrong
Path.GetDirectoryName("")                 -> throws ArgumentException
```

Only the third throws. The other two produce a path that looks right, resolved against whatever directory the host process happens to be in — which for TIA Portal is nothing to do with where the Add-In lives. A "not installed" error would then name a folder nobody chose.

`Environment.GetFolderPath` needs no permission of this kind and no discovery, which is why the framework agrees on a location instead of deriving one.

### Reading a CPU's addresses out of the project

The satellite cannot ask TIA anything, so the Add-In gathers the addresses and hands them over. Two things about that walk are not obvious, and both are verified on the VM:

**The interface is not on the item that owns the software.** A CPU's network interface is a separate child device item — "PROFINET interface_1" and the like — so the whole device has to be walked. Asking `GetService<NetworkInterface>()` on the item that carries the `PlcSoftware` finds nothing.

**The IP is an attribute of the node, not a typed property.** `HW.Address` is a different thing entirely — an I/O address, with a start and a length — and reaching for it here is the obvious wrong turn. The path is:

```csharp
DeviceItem.GetService<NetworkInterface>()   // per device item, walking the whole device
    .Nodes                                   // NodeComposition
    .GetAttribute("Address")                 // confirmed: the attribute is called "Address"
```

Nodes that are not IP — a PROFIBUS node's address is a number like `2` — are filtered out by shape, so the drop-down only offers things worth pointing a browser at.

### Walking a PLC for the coding-style check

`Adapters/TiaCheckedObjects.cs` turns whatever was selected into the `CheckedObject`s that `Core.Checks.CodingStyleChecker` reads, and `CheckCodingStyleAction` runs the check. Read off both assemblies by reflection, **the surface it touches is identical in V20 and V21**, so the two files are byte-identical and duplicated only for the assembly identity. **Run inside TIA Portal on the VM (2026-09-15)**, which is what settles the three things no test here could: that `Export` writes the interface the reader expects, that partial trust really does let the Add-In create a folder inside the project and write to it, and that the whole chain — menu entry, walk, export, report window — holds together.

"Check coding style" is registered on eleven kinds of node, one `AddActionItemWithIcon<T>` each, and only the one matching the selection shows:

| Registered on | Checks |
| --- | --- |
| `Project` | every PLC in the project, found through `Devices`, `UngroupedDevicesGroup` and every `DeviceGroups` folder |
| `DeviceItem` | the PLC it carries; any other device item contributes nothing |
| `PlcUnitBase` | one software unit, or a safety unit |
| `PlcBlockGroup`, `TechnologicalInstanceDBGroup`, `PlcTagTableGroup`, `PlcTypeGroup`, `PlcAlarmTextlistGroup` | a folder and everything below it — the system root and a user folder alike, since both derive from the registered type |
| `PlcBlock`, `PlcTagTable`, `PlcType`, `PlcAlarmTextlist` | the selected objects |

**Technology objects get no registration of their own**, and that is not an omission: `TechnologicalInstanceDB` derives from `InstanceDB`, so it already is a `PlcBlock` and a second registration would show the entry twice. The adapter recognises it and files it under `technologyObjects`. Every registration takes the whole selection, and the action drops an object reached twice — a folder selected together with a folder inside it.

**The action loads and validates before it walks.** The walk arrives as a delegate, so a broken `codingStyle` is reported without first reading several thousand objects on TIA's own thread; only `codingStyle` is validated, so a broken `hierarchy` does not stop a naming check. Before any of that it checks the report window is installed, since no amount of walking helps a missing executable. The result travels to `Satellite.CodingStyleReport` as a `Core.Checks.StyleReport` over standard input — see [the satellites page](satellites.md).

**The checker survives the tightest partial trust.** Run inside an `AppDomain` granted `Execution` alone, the validator, the checker and a pattern hitting its match timeout all behave as they do outside, with no `SecurityException` — the first time `Regex` with a timeout runs in TIA's process, checked before it gets there.

**Where an object lives is three answers: its PLC, its software unit and the folders in between.** A project holds several PLCs and a PLC several units, each repeating the same folder names and often the same block names, so one joined path could be filtered on as a whole or not at all — the report gives each its own column, and the general program is written as `*`. The three are found together, downwards from a PLC or a unit and upwards through `Parent` from a folder or an object, to the same answer; links in the chain the walk does not recognise are stepped over rather than ending it.

> Until 2026-09-15 the three were one string starting with the PLC's name. The columns came out of the same reading that found the defect below: a report cannot be acted on if it does not say which PLC, and a folder name repeated in ten units says nothing on its own.

What the object model offers, and therefore what phase one can check without exporting anything:

| Family | Walked through | Interface |
| --- | --- | --- |
| Blocks | `BlockGroup` → `Blocks` / `Groups` | an OB's, an FC's, an FB's and a global DB's, **from the export**. An instance DB's belongs to its FB and an array DB has none |
| Technology objects | `TechnologicalObjectGroup` → `TechnologicalObjects` / `Groups` | none: Siemens names every member |
| Tag tables | `TagTableGroup` → `TagTables` / `Groups` | `Tags` as `Tag`, `UserConstants` as `UserConstant`, **straight from the object model** — names with nothing nested inside them |
| Types | `TypeGroup` → `Types` / `Groups` | a UDT's elements, from the export; `PlcType` exposes no interface at all |
| Alarm text lists | `PlcAlarmTextlistGroup` → `PlcAlarmUserTextlists` | none. The group has **no folders and no `Name`** |

**An interface is read by exporting the object, and the export is what costs.** `PlcBlock.Export` and `PlcType.Export` write SimaticML into `<TIA project>\.plc-framework\tmp\coding-style-<timestamp>\`, `Core.Checks.SimaticMlInterface` reads it, and the file is deleted immediately; the run's folder goes when the run ends, whatever happened, because what is in it is somebody's source code. `Config.xml` already declares `FileIOPermission`, which is what makes any of this possible inside the sandbox.

**Nothing is exported during the walk.** An object hands over a *delegate*, and the checker calls it only for the objects whose own name matched a rule that expects something inside — so a project where no rule names an interface writes no file at all, and one that names a few exports a few. Measured against doubles: of five objects, only the three whose rules wanted an interface were asked.

**Nothing is compiled to make an export work.** TIA refuses to export a block that is not consistent, and compiling it would change the project behind an operator who asked for a naming check. That refusal, a know-how protected object, and a `tmp\` folder that could not be created all come back as a reason, which the checker turns into one skipped row naming the object — the check itself carries on.

Software units — `PlcUnit` and `PlcSafetyUnit` — repeat four of those roots and are walked the same way, with the unit's name after the PLC's in the path; technology objects exist only at PLC level. On an S7-1200 `GetService<PlcUnitProvider>()` answers nothing, which is expected.

Decisions worth keeping:

- **Only names an engineer chose.** `SystemBlockGroups`, `SystemTypeGroups`, system constants, system text lists and the default tag table are named by TIA. A rule could only fail them, and nobody reading the report could act on it.
- **An interface belongs to whoever declares it.** An instance DB's members are its FB's, and are checked on the FB; an array DB holds elements of one type rather than named members; a technology object's are Siemens'. Inside an export the same rule applies again, one level down: a member typed by a UDT, a library FB or a GRAPH type carries that type's interface, and it is checked where that type is defined.
- **A DB's members cannot be checked through the object model at all, and that was found on a real project** (2026-09-15). `Member` carries a `Name` and nothing else — verified again by reflection against V20 — so a member inside a `Struct` arrives as `variableA.variableB`, the name of its parent and its own joined by a dot, and nothing distinguishes it from a top-level member. Held against a naming rule that reads one name, every such member fails. There is no fix on this side: the tree the object model shows is flat. Phase two reads interfaces out of the exported SimaticML instead, where a nested member sits inside its parent and is checked by its own name — see [`reference/S7-exports.md`](reference/S7-exports.md) for that format, measured off real exports. `Core.Checks.SimaticMlInterface` is the reader; **what exports the block, and when, is still this adapter's to build.**
- **A `TechnologicalInstanceDB` is an `InstanceDB` to the type system**, so the type name is decided most specific first — the other way round reports every technology object as an instance DB.
- **Reading the tree is not guarded; reading an interface is.** A folder that silently failed to read would drop out of the report and leave it looking complete. An interface that cannot be read — a know-how protected block, a block TIA will not export, nowhere to export it to — travels as a reason, and the checker turns that into a skipped row naming the object rather than one that looks clean. A source that throws is caught in `Core` as well: one block must not end a check of several thousand.
- **The closed-set names come from `Core.Config.CodingStyleNames`**, the same constants the validator uses. A misspelt type on this side would not fail; it would read as "no rule for this type".

## The Add-In API

Verified by reflection over `V20.addIn\Siemens.Engineering.AddIn.dll` and `V21\net48\Siemens.Engineering.AddIn.Base.dll`. **These signatures are identical in both versions** — only the assembly they live in changes:

| Member | Actual signature |
| --- | --- |
| `ProjectTreeAddInProvider` | `abstract`, **parameterless** constructor. TIA instantiates the derived class by looking for a constructor taking `TiaPortal` and injects it |
| `GetContextMenuAddIns()` | **`protected virtual`** (not `abstract`), returns `IEnumerable<ContextMenuAddIn>` |
| `ContextMenuAddIn` | constructor `(string displayName)`; override `protected override void BuildContextMenuItems(ContextMenuAddInRoot)` |
| `ContextMenuAddInRoot.Items` | a `ChildItemFactory` |
| `ChildItemFactory` | `AddActionItem<T>(string, OnClickDelegate)` **`where T : IEngineeringObject`**, plus `WithIcon` / `WithCheckBox` / `WithRadioButton` variants, 2- and 3-generic-type versions, and `AddSubmenu(string)` |
| `MenuSelectionProvider<T>` | **declares no members of its own**; inherits from the non-generic base |
| `MenuSelectionProvider` (base) | `public IEnumerable<object> GetSelection()` · `public IEnumerable<TRequested> GetSelection<TRequested>()` · `internal int GetSelectedObjectCount` |
| `NotificationIcon` | lives in**`Siemens.Engineering`**, NOT in `Siemens.Engineering.AddIn.Menu` |

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

|  | V20 | V21 |
| --- | --- | --- |
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
| --- | --- |
| `Graphics` | **removed** — `MultiLingualGraphic` and its composition exist nowhere in the sixteen assemblies |
| `PlantViews` | **removed** — likewise, no `PlantView` and no `PlantViewComposition` |
| `IsSimulationDuringBlockCompilationEnabled` | **removed** |
| `IsVirtualPlcDuringBlockCompilationEnabled` | **removed** |
| `TextCategories` | **added** — returns `TextCategoryComposition`, declared in `Base` |

These are removals rather than relocations: the types themselves are gone. Code touching `project.Graphics` or `project.PlantViews` fails to compile against V21, which is the good outcome; the bad one is a V20 Add-In still shipping those calls and nobody noticing until someone opens V21.

**Assembly identity, where the source is identical.** This one is easy to miss, because there is nothing to see in the code. `Siemens.Engineering.AddIn.Utilities` exposes the very same `Process` wrapper in both versions — same namespace, same type, same members, verified by reflection — but the assembly is signed with a **different public key token**:

|  | V20 | V21 |
| --- | --- | --- |
| Assembly name | `Siemens.Engineering.AddIn.Utilities` | `Siemens.Engineering.AddIn.Utilities` |
| Public key token | `65b871d8372d6a8f` | `29bfe5fdf4ba5d3b` |

So even source-identical code cannot be compiled once: a single binary would bind to one identity and fail to load in the other host. That is why `Adapters/ProcessLauncher.cs` exists twice, byte for byte, behind `IProcessLauncher`. Nothing in either file hints at the reason, which is why it is written down here.

The wrapper itself delegates to `System.Diagnostics.Process` — it holds an `m_InternalProcess` field — so its `Dispose` releases the handle without touching the process that was started, and a fire-and-forget launch can safely use a `using`.
