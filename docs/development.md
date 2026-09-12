# Development

How this is built and tested. Only of interest if you intend to work on it.

> **You cannot build this repository from a clean clone.** It compiles against Siemens
> Openness assemblies that are licensed and cannot be redistributed, so they live outside
> the repo. Without them the Add-In projects do not build and no `.addin` can be produced.
> Released binaries are the supported way to run it.

## Requirements

- **.NET SDK** (verified with 10.0.400) and the **.NET Framework 4.8 targeting pack** - `dotnet build` compiles `net48` without Visual Studio.
- For Visual Studio: the **".NET desktop development"** workload plus the **".NET Framework 4.8 targeting pack"** individual component.
- The **Siemens Openness assemblies**, which are not in this repository. See *Siemens dependencies* in [openness-notes.md](openness-notes.md) for what is needed and where it is expected.

> The official Siemens `.vsix` (Add-In project template) is **not used** here: it generates a classic-style project with a single API version hardcoded, and two projects with different references are needed. A hand-written SDK-style `.csproj` is shorter and more flexible.

## Working environment: two machines

| Machine        | Role                                          | TIA Portal                              |
| -------------- | --------------------------------------------- | --------------------------------------- |
| Development PC | write code, build, produce the`.addin`      | **not** installed, and not needed |
| VM             | debugging and testing: load the Add-In in TIA | V20 and V21 installed                   |

The full cycle up to the `.addin` package closes on the development PC: references resolve against the DLLs in `.lib\`, not against a TIA installation, and the Publisher does not need the product installed either.

The **VM** does require TIA Portal and the user to belong to the local **"Siemens TIA Openness"** group — without it the Add-In will not even show up.

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

|                                                                                                   | V20                                                                                          | V21                                                                                      |
| ------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------- |
| Add-In assembly                                                                                   | `Siemens.Engineering.AddIn.dll` — **2269 types**, carries the object model embedded | `Siemens.Engineering.AddIn.Base.dll` — **71 types**, Add-In infrastructure only |
| Object model (`TiaPortal`, `Project`, `IEngineeringObject`, `NotificationIcon`, `HW.*`) | inside the Add-In assembly                                                                   | `Siemens.Engineering.Base.dll` — 1382 types                                           |
| `SW.Blocks.PlcBlock`                                                                            | inside the Add-In assembly                                                                   | `Siemens.Engineering.Step7.dll`                                                        |
| HMI types                                                                                         | `Siemens.Engineering.Hmi.dll`                                                              | no such assembly —`WinCC.dll` / `WinCCUnified.dll`                                  |
| References needed                                                                                 | **only** the Add-In assembly                                                           | **both** `AddIn.Base` and `Base`                                               |
| `extern alias`                                                                                  | needed if`Siemens.Engineering.dll` is added                                                | never needed                                                                             |

`AddIn.V20` references: `Siemens.Engineering.AddIn` · `.AddIn.Permissions` · `.AddIn.Utilities` · `Siemens.Engineering.Hmi`

`AddIn.V21` references: `Siemens.Engineering.AddIn.Base` · `Siemens.Engineering.Base` · `.AddIn.Permissions` · `.AddIn.Utilities`

#### V20: do not add `Siemens.Engineering.dll`

Since `Siemens.Engineering.AddIn.dll` already carries the whole object model, referencing **both** assemblies breaks the build: they define distinct, incompatible copies of `Siemens.Engineering.IEngineeringObject`, which becomes ambiguous, forcing `extern alias TiaAddIn;` plus `using AddInEngineering = TiaAddIn::Siemens.Engineering;` with every menu generic typed over `AddInEngineering.IEngineeringObject`.

**All of that is avoidable**: do not add `Siemens.Engineering.dll` until a specific type actually fails to compile.

#### V21: the split is clean

`IEngineeringObject` exists exactly once, in `Siemens.Engineering.Base.dll`. Nothing is duplicated, so referencing both assemblies is not only safe but required, and the `extern alias` problem simply does not arise.

> **Two referenced assemblies are missing from `V21\net48\`.** `Siemens.Engineering.Contract` is referenced by **13 of the 16** — not only by `AddIn.Base` — and `Siemens.Engineering.ClientAdapter.Interfaces` by `Base`. Neither has blocked a build so far, because the compiler only needs a referenced assembly when one of its types appears in a signature the code actually touches. The failure to recognise is *"is defined in an assembly that is not referenced"*, and the fix is to copy the DLL out of a TIA V21 installation — Openness does not ship it.

### Deploying to the VM

Two scripts, one on each machine, because the copy has to happen where the destination is.

```
development PC:   scripts\step1-stage-host.ps1    builds, then gathers into .deploy\
VM:               scripts\step2-deploy-vm.cmd     installs what was gathered
```

The names carry the order and the machine, because getting either wrong is the easy
mistake: step 2 on the host would install into the development PC, and step 1 on the VM has
no Siemens tools to build with.

**Step 2 is launched through a `.cmd`, and that is not cosmetic.** Windows refuses to run a
`.ps1` twice over here:

```
.\step2-deploy-vm.ps1 : File <share>\...\step2-deploy-vm.ps1 cannot be loaded because
running scripts is disabled on this system.
```

The default `ExecutionPolicy` is `Restricted`, **and** the VM reaches the repo through a
mapped drive, which Windows classifies as the Internet zone — so even `RemoteSigned`, the
usual answer, would still block it for a second and entirely separate reason. The wrapper
sidesteps both without changing anything on the machine:

```bat
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0step2-deploy-vm.ps1" %*
```

`-ExecutionPolicy` on the command line applies to that one process, needs no elevation and
leaves the machine's policy alone; `%~dp0` resolves the script beside the `.cmd`, so it
works from any working directory; `%*` passes `-ToolsOnly` and `-AddInsOnly` straight
through. **Prefer this to `Set-ExecutionPolicy`**: a deployment script is a poor reason to
loosen a machine-wide security setting, and the next machine would need the same change
made by hand.

`step1-stage-host.ps1` writes to `.deploy\` and **not** to `bin\Debug\net48`, which is the whole
reason it exists: TIA holds the `.addin` open through the shared folder, so the Publisher
cannot overwrite the file the VM is reading and the build dies with `MSB3073`. Two copies
of the artefact, and the build only ever touches one of them.

```
.deploy\
├── bin\                     the three satellites, merged flat as InstallPaths.Root expects
└── addins\                  PLC-Framework.V20.addin, PLC-Framework.V21.addin
```

**The installer is not copied in here.** It was, briefly, so that `.deploy\` was one
self-contained folder to carry to a local disk — but the UNC below does cross, so that
fallback never ran, and staging for the VM is a different job from packaging a release.
Pairing an installer with this payload is the release script's work, and it names the files
what a stranger expects rather than what the maintainer's two steps are called.

The three satellites share `Core.dll` and `UI.Shared.dll`, and the copies are identical
because they were built together — which is what lets one folder serve all of them. `.pdb`
files are excluded; add `/XF` back if a crash needs chasing on the VM.

#### Reaching the repo from an elevated session

The VM mounts a folder of the host — the host knows nothing of the VM — and the everyday
route is the mapped drive `Z:`. **That route dies the moment you need elevation**, and the
machine-wide Add-In folder needs it:

```
PS C:\WINDOWS\system32> cd Z:\...\tia-portal-addins\scripts
cd : Cannot find drive. A drive with the name 'Z' does not exist.
```

Not a permission problem: **a mapped drive belongs to the logon session that created it**,
and elevating gives a different token, so the letter is simply absent. Worth knowing that
the first symptom is misleading — typing the folder as a command answers
`CommandNotFoundException`, which says the same thing whether the path is missing *or* is a
directory, so it is no evidence either way. `Test-Path Z:\` is what settles it.

**The UNC behind the mapping does cross, and that is the answer.** `\\vmware-host\Shared Folders` is VMware Tools' HGFS provider — a network provider rather than a mapping — and it
is reachable from the elevated session. Confirmed on this VM, which exposes `C`, `D` and `E`:

```powershell
& "\\vmware-host\Shared Folders\<share>\tia-portal-addins\scripts\step2-deploy-vm.cmd"
```

No copy, nothing to keep in step. `(Get-PSDrive Z).DisplayRoot` in the **normal** session
prints the UNC a mapping stands for, which is how to find it on another machine.

**The fallback, if a station's UNC does not cross either**: copy to a local disk from the
normal session — the one that can see `Z:` — and run from there elevated. **Copy both
folders, keeping them beside each other**, because `step2` resolves the payload relative to
itself and a lone script stops with *"Nothing staged"*.

```powershell
robocopy Z:\...\tia-portal-addins C:\PLC-Framework-deploy /E /PURGE .deploy scripts
C:\PLC-Framework-deploy\scripts\step2-deploy-vm.cmd                          # elevated
```

**A deployer copied by hand goes stale silently** — the same disease as testing a stale
build, one level up, except nothing prints the installer's age. Copy both folders again
after every build rather than leaving last week's script behind.

`StagingFolder` works out which case it is by **looking for the payload rather than being
told**: `bin\` and `addins\` beside the script means this is a staged copy that carries its
own installer, otherwise the staging folder is `.deploy\` one level up from `scripts\`.
Only the second case happens today, and the first is what the release package will use.

`step2-deploy-vm.ps1` copies `tools\` with `/PURGE`, so a renamed executable does not linger,
and puts each package in its TIA version's Add-In folder. Two switches for the common cases:
`-ToolsOnly` when TIA is open and holding the packages, `-AddInsOnly` when only the Add-In
changed.

**The Add-In folder is chosen per TIA version, not per run**, because that is how the VM is
actually set up — and a single switch for both could only ever be right about one of them:

```powershell
$targets = @(
    @{ Package = "PLC-Framework.V20.addin"; Portal = "Portal V20"; Scope = "Machine" },
    @{ Package = "PLC-Framework.V21.addin"; Portal = "Portal V21"; Scope = "User"    }
)
```

```
V20  ->  C:\Program Files\Siemens\Automation\Portal V20\AddIns\        needs elevation
V21  ->  %AppData%\Siemens\Automation\Portal V21\UserAddIns\           needs nothing
```

So the everyday run is `step2-deploy-vm.cmd` **as administrator**, and V21 rides along in
the same pass. `-Scope Machine` or `-Scope User` overrides *both* versions, for a station
set up differently; `-InstallRoot` moves the machine-wide root when TIA is not under
`%ProgramFiles%`. Four things the script does rather than let Windows explain them badly:

- **It asks whether it can write, not whether it is elevated**, once and up front. Elevation
  is only a proxy: a TIA installed outside `Program Files` is writable without it, and
  refusing that run would be wrong. The probe creates the folder and a temporary file in it,
  which is work that had to happen anyway. Left to the copy, the refusal arrives as an
  `Access denied`, which reads exactly like the *other* thing that fails here — a package
  locked by a running TIA. It is asked only for versions that are present, since being sent
  to find an administrator for a TIA the station does not have would simply be a lie. And if
  the session is already elevated and still refused, it says so instead of repeating advice
  that has already been taken.
- **A missing `Portal V2x` folder means TIA is not installed there; a missing `AddIns` or
  `UserAddIns` inside it is just a folder to create.** The parent is Siemens' — the
  installation in one scope, the per-user settings folder in the other — and the leaf is
  ours: neither exists until an Add-In is installed. One rule serves both scopes, and
  reading a missing leaf as "TIA is not installed" would be wrong in either.
- **It reports the package sitting in the other folder.** TIA reads both, so a leftover copy
  after a version changed scope means the Add-In is loaded twice and the one under test is
  whichever TIA reached first. It prints the full path of the file to delete rather than
  deleting it — that folder may hold somebody else's decision.
- **It prints every destination before attempting anything.** That is also the only way to
  catch an elevated run whose `%AppData%` belongs to the administrator rather than to the
  engineer TIA runs as: the printed path reads `C:\Users\<somebody else>\…` and the package
  lands where TIA will never look. Nothing else in the run would say so.

Exercised here without TIA installed, by redirecting `%AppData%` and pointing `-InstallRoot`
at a simulated installation shaped like the VM — V20 machine-wide, V21 per-user. Both
packages landed in their own folder with the leaf created, and separately: the write refusal
listing only the versions present, the duplicate warning firing for one version and not the
other, the `-Scope User` override, and the not-installed report.

**It prints the destination folder and the age of everything it installs — "2 min ago" —
and that is the point.** The failure worth preventing is testing a stale build and spending
an afternoon debugging something already fixed, and that failure never announces itself.
"It is deployed" has to be something you can read, and once `-Scope` and `-InstallRoot` can
move the target, *where* it was deployed must be readable too rather than reconstructed from
the switches afterwards. Both are printed before anything is attempted, so a package that
was not staged still shows the folder it would have gone to.

Two things it reports rather than fails on, because both are ordinary: a TIA version that
is not installed, and a package locked by a running TIA. The second says which TIA to close.

> **`robocopy` reports success with a non-zero exit code** — 1 means files were copied, 2
> that extras were found, 3 both; only 8 and above are failures. A script that does not
> normalise that prints success on screen and returns failure to its caller. Both scripts
> reset `$LASTEXITCODE` and `exit 0`.

> Both scripts are **ASCII only**, deliberately. PowerShell 5.1 reads a UTF-8 file with no
> BOM as ANSI, so a single accented character becomes a parse error.

### Installing a satellite

Copy the project's **whole build output** into `tools\`, not just the executable:

| File                                         |                                                                |
| -------------------------------------------- | -------------------------------------------------------------- |
| `PLC-Framework.Satellite.About.exe`        | the app                                                        |
| `PLC-Framework.Core.dll`                   | `Product.Title`, used by the window title and the mutex name |
| `PLC-Framework.UI.Shared.dll`              | `BrandLogo.xaml`, `Theme.xaml`, `SingleInstance`         |
| `PLC-Framework.Satellite.About.exe.config` | 174 bytes pinning .NET Framework 4.8                           |

The three `.pdb` files are optional: they only add line numbers to stack traces, which is worth having while testing on the VM and not afterwards.

```
robocopy "src\Satellite.About\bin\Debug\net48" "C:\Program Files\PLC-Framework" /E
```

That is the manual form; `scripts\step2-deploy-vm.ps1` does it for all three satellites at
once. Either way `C:\Program Files\PLC-Framework\` needs elevation. To test without it,
point `PLC_FRAMEWORK_HOME` at any folder holding the executables — the deploy script honours
it too — and **restart TIA Portal afterwards**, since a running process does not see an
environment variable created after it started.
