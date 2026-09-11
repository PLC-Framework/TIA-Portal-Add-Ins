<h1 align="center">TIA Portal V20, V21 Add-Ins and Satellite apps</h1>

<p align="center">
  <img src="assets/Brand/favicon.svg" alt="PLC-Framework" width="140" height="140">
</p>

A <b>TIA Portal Add-In</b> that brings a project's folder structure and naming rules under one versioned configuration file,  plus the <b>desktop apps it launches</b> from the TIA menu.

Written against Siemens Openness for <b>TIA Portal V17–V20 and V21</b>.

## What it does

Right-click the project, or a PLC, or a selection of blocks, and:

|                                    |                                                                                                                                                                                                          |
| ---------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Config. Editor**           | edits`.plc-framework\config.json` — the folder hierarchy to create and the naming rules to enforce — with validation while you type, so a broken reference is caught before the Add-In ever reads it |
| **Create project hierarchy** | creates that folder tree inside the PLC: program blocks, technology objects, tag tables, PLC data types, and inside every software unit                                                                  |
| **Data block snapshot**      | reads every variable of the selected data blocks over the CPU's own web server and writes one`.xlsx` per block — for capturing settings, not live process values                                      |

The configuration lives **inside the TIA project**, in `.plc-framework\`, so it travels with the project and belongs to it rather than to whoever last ran the tool.

## Requirements

- **TIA Portal V17–V20, or V21**, on the machine where you install it.
- The Windows user must belong to the local **"Siemens TIA Openness"** group. Without it the Add-In does not appear in the menu, and TIA gives no clue why — this is the single most common reason for "nothing happened".
- **.NET Framework 4.8**, present on any current Windows.
- Administrator rights **once**, to install.

## Installing

Download the release archive, unpack it anywhere, and run `install.cmd` as administrator.
Full instructions, including uninstalling and what to do when the Add-In does not show up, are in **[docs/getting-started.md](docs/getting-started.md)**.

> The `.addin` package is **not signed**. TIA Portal will list it as untrusted and you have
> to enable it by hand the first time. That is expected, not a fault — see the same page.

## What to expect

This is **early**. It is used daily on one engineer's projects and has been exercised
against two real CPUs and both TIA versions, but you are among the first people outside that. In particular:

- The hierarchy and the naming rules come from **your** `config.json`. Without one you get the editor and a template to start from, not a result — try it on a scratch project first.
- There is no migration story yet between versions of the configuration format.
- Feedback is the point of publishing it. Open an issue with what you did, what happened, and the version from the About window.

## Building

**You cannot build this from a clean clone**, and that is not an oversight. It compiles
against Siemens Openness assemblies that are licensed and must not be redistributed, so they live outside the repository. Without them the Add-In projects do not build and no `.addin` can be produced. Released binaries are the supported way to run it.

Everything else — the configuration model and its validators, the web server client, the windows — builds and is exercised without TIA Portal installed at all. See **[docs/development.md](docs/development.md)**.

## Documentation

|                                                |                                                                                                |
| ---------------------------------------------- | ---------------------------------------------------------------------------------------------- |
| [Getting started](docs/getting-started.md)      | requirements, installing, uninstalling, troubleshooting                                        |
| [Configuring a project](docs/configuration.md)  | the`config.json` contract, its validators, the JSON Schema, and how secrets stay out of it   |
| [The satellite apps](docs/satellites.md)        | what each window does, and the decisions behind it                                             |
| [Architecture](docs/architecture.md)            | the layering, the naming convention, where everything installs                                 |
| [Openness notes](docs/openness-notes.md)        | the Add-In API as the assemblies declare it, V20/V21 differences, the Publisher, partial trust |
| [The CPU web server API](docs/webserver-api.md) | JSON-RPC findings measured against two real CPUs — useful on its own                          |
| [Development](docs/development.md)              | building, testing, and how releases are cut                                                    |

`CLAUDE.md` holds the working rules and the decision record: what was chosen, what was rejected, and why. Read it before proposing a change that looks obvious.

## Licence

MIT — see [LICENSE](LICENSE).

The released binaries include third-party components under their own terms; see
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

**Not affiliated with or endorsed by Siemens.** TIA Portal and Openness are Siemens products, licensed separately, and you need them for any of this to do anything. No Siemens binary is redistributed here.
