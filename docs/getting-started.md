# Getting started

What you need, how to install, and what to do when the Add-In does not show up. If you only
want to try the thing, this is the only page you need.

## Requirements

| | |
|---|---|
| **TIA Portal** | V17–V20 for the V20 package, or V21 for the V21 one. Both can be installed side by side; each only ever loads its own |
| **The Openness group** | the Windows user must belong to the local **"Siemens TIA Openness"** group |
| **.NET Framework 4.8** | present on any current Windows |
| **Administrator** | once, to install |

**The Openness group is the one that catches people.** Without it the Add-In does not appear
in the menu at all, and TIA says nothing about why — it looks exactly like a failed install.
Add the user in *Computer Management → Local Users and Groups → Groups*, then **sign out and
back in**: group membership is read at logon, so a session that was already open still will
not see it.

## Installing

> **No release has been cut yet.** Until there is one, install by hand — the steps below are
> what the installer will do, and they are what the maintainer runs today.

Two things go onto the machine:

| What | Where |
|---|---|
| The `.addin` package for your TIA version | one of the two Add-In folders below |
| The satellite executables and their DLLs | `C:\Program Files\PLC-Framework\`, flat |

**There are two Add-In folders per TIA version, and TIA reads both:**

| | Path | For | Elevation |
|---|---|---|---|
| **Per machine** | `C:\Program Files\Siemens\Automation\Portal V2x\AddIns\` | every engineer on the station | **yes** |
| **Per user** | `%AppData%\Siemens\Automation\Portal V2x\UserAddIns\` | the engineer logged in | none |

Substitute `Portal V20` or `Portal V21`. **Put the package in one of them, never both** — TIA
reads both, would load it twice, and which copy you are then testing is undecided.

Three details that bite:

- The per-user folder is **`UserAddIns`**; the per-machine one is **`AddIns`**. Neither name
  is a typo for the other.
- The per-user one is under **`AppData\Roaming`** — what `%AppData%` expands to — not
  `AppData\Local`.
- Neither folder is guaranteed to exist. Create the one you are using.

## Enabling it the first time

**The package is not signed.** TIA Portal lists it as untrusted and will not load it until
you say so: open *Options → Add-Ins*, find it, and enable it. TIA distinguishes trusted,
unsigned, and revoked/tampered — an unsigned Add-In is the middle case and is expected here.

Then **restart TIA Portal**. A package dropped into the folder while TIA is running is not
picked up, and a changed one is not either.

## Checking it worked

Right-click the project in the tree. There should be a submenu with **Config. Editor** on it.
If the project has no `.plc-framework\` folder yet, that is fine — the editor treats a
missing configuration as a normal starting point and offers to create one.

## Uninstalling

1. Close TIA Portal.
2. Delete the `.addin` from whichever Add-In folder you put it in.
3. Delete `C:\Program Files\PLC-Framework\`.

Two things are deliberately left behind, because they are yours rather than the product's:

- `%LOCALAPPDATA%\PLC-Framework\` — your remembered PLC credentials, your `.env`, your
  customised template. Delete it too if you want nothing kept.
- `.plc-framework\` inside each TIA project — that is the project's configuration, and it
  belongs to the project.

## When it does not appear

In order of how often each is the answer:

1. **The Openness group**, and a sign-out afterwards. See above.
2. **The Add-In is not enabled** in *Options → Add-Ins*, because it is unsigned.
3. **TIA was not restarted** after the package was copied.
4. **The package is in the wrong folder for that TIA version** — `Portal V20\` for the V20
   package, `Portal V21\` for the V21 one. They do not read each other's.
5. **The package is in both folders**, and the copy being loaded is not the one you changed.

If none of those, open an issue with your TIA version, the exact folder you installed into,
and whether the Add-In is listed at all in *Options → Add-Ins*.
