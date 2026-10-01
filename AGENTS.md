# AGENTS.md

Short instructions for coding agents working on this repository. **The decision record is [CLAUDE.md](CLAUDE.md)** — what was chosen, what was rejected, and why — and the technical facts live under [docs/](docs/). Both are long; read the section about the area you are touching rather than the whole of either.

## What this is

TIA Portal Add-Ins for V17–V20 and V21, plus the WPF satellite applications they launch, on .NET Framework 4.8. Everything up to the `.addin` package builds without TIA Portal; the Siemens assemblies are licensed, live outside the repository and are reached through `$(SiemensPublicApi)`, so the Add-In projects do not build from a clean clone. **"The VM" is the test machine with TIA Portal installed**, and "verified here" and "confirmed on the VM" are different claims. The test battery is the maintainer's own and is not in the repository.

## When reviewing

- Report correctness bugs only, most severe first, each with its file, line and a concrete scenario that fails. Do not modify files.
- **Something that looks wrong may be a decision on record**, often with the measurement that settled it. Check CLAUDE.md before reporting it; report it only with information the record does not already weigh, and name the decision you are disputing.
- These are the rules whose breaking fails silently, and the ones most worth checking:
  - **Partial trust.** `Core` and `AddIn.Shared` run inside TIA Portal's sandbox. Any type a serializer touches must be public, all the way out of any nesting. `Assembly.Location`, `Process.GetCurrentProcess()` and `Path.GetFullPath` throw there.
  - **Layering by dependency.** `Core` takes no package, no `System.Drawing` and no `System.Net.Http`. Only `AddIn.V20`/`V21`, `Openness.V20`/`V21` and the `Satellite.*.V20`/`V21` executables reference Siemens assemblies.
  - **An Add-In never keeps a Siemens engineering object in a field, a property or a static**; the Publisher refuses the package.
  - **An attached `TiaPortal` is never disposed**: disposing it closes the engineer's TIA Portal.
  - **Nothing a project already has is overwritten without asking.** TIA overwrites silently on the external source route.
  - **Secrets stay out of projects and logs.** `config.json` carries `${REPO_TOKEN}`, never the token; the token lives in the per-user `.env`, and PLC credentials in `credentials.json`, both under `%LOCALAPPDATA%\PLC-Framework\`.
  - **A renamed configuration key is still read under its former name** (`rules`, `folder`); carrying both at once is an error, not a guess.
  - **A failure is said, never swallowed into something that looks like success** — a report with a silent hole in it is the outcome this codebase is shaped to avoid.

## Conventions

- Code, comments, documentation and commit messages in English.
- One type per `.cs` file, named after it; a private nested type stays inside its owner.
- `.ps1` files under `scripts\` stay ASCII: PowerShell 5.1 reads a UTF-8 file with no BOM as ANSI.
- Never read or print the per-user `.env` or `credentials.json`, and never commit: the maintainer does.
