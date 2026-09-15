# The satellite apps

A satellite is a WPF application the Add-In launches from the TIA menu. This page covers what each one does and the decisions behind it.

## Capturing a data block

`Satellite.DataBlockSnapshot` writes one workbook per block, `<ip>-<DB>-snapshot-<timestamp>.xlsx`, into `<TIA project>\.plc-framework\exports\` by default. The rules it follows are all about the file being trustworthy later:

- **Every variable that was browsed has a row**, carrying its value or the reason it could not be read. The reference application drops the failures, which leaves a partial capture looking complete.
- **A file appears only when the block read completely.** An incomplete read is reported in the window and leaves nothing behind to be mistaken for a good capture.
- **Cells are typed.** A number arrives as a number and a bool as a bool, which is the reason to write `.xlsx` at all rather than let Excel guess at a CSV.
- **Numbers are written invariant**, because the format stores a numeric cell as an invariant string and lets the reader's locale display it. `6,785` in that slot produces a file no Excel reads as a number, including a Spanish one.
- **An empty string is written as an empty cell of string type**, not as an absent cell — otherwise "this setting is empty" and "this value could not be read" look identical.
- **`xml:space="preserve"`**, so a setting whose value ends in a blank keeps it.
- A second sheet, `Info`, records the PLC, the block, the start and end of the read, the variable and failure counts, and whether the browse was truncated. **A capture is a sweep, not an instant**, so both ends of the window are recorded rather than one timestamp that would imply otherwise.

Verified with the OpenXML SDK's own validator — zero errors — and by reading the package XML back: 990 numeric cells, 499 boolean, 126 string, none omitted.

### Filling the block list without TIA Portal

Launched from the menu, the list arrives filled: the Add-In hands over whatever was selected in the project tree. Launched by hand — the mode every layer below the window was verified in — it used to arrive empty, with a message sending the operator back to TIA Portal. **A `+` and an `✕` under the list now add and remove blocks by name**, which is what makes this satellite as usable standalone as the other two, and the empty-list message names both ways in rather than only one.

- **Typing, not browsing.** One call to the program root would list every block on the CPU, and offering that is deliberately still refused: the window would become a block explorer with a capture button, and the Add-In's selection would stop being what decides the contents of a capture. Typing a name is a statement of intent; picking from a list of four hundred is a different feature, and a slower one to use.
- **A duplicate is refused, case-insensitively.** It is one block on the CPU either way, so a second row would capture it twice and leave two workbooks differing only by the ` (2)` that a filename collision appends.
- **Enter adds too**, because the gesture is typing several names in a row. The handler marks the key handled — otherwise it reaches the window's default button and starts a capture.
- **Remove names a row**, so it stays disabled until one is selected, and re-selects a neighbour afterwards so clearing several is one click each.
- **A typo is not the list's problem.** Existence is checked against the CPU before capturing, and a name that is not there is reported per row as *not on this CPU* — the same path a block renamed in the project already took.

Exercised through UI Automation against the running binary, started with no handoff: adding three by button and a fourth with Enter, a duplicate in different case refused with the text kept for correction, empty and whitespace-only names refused, selection driving the remove button, and the three controls disabling for the length of a capture and coming back after.

**The `Block` row and the `Folder` row line up through `Grid.IsSharedSizeScope`**, not through a width. They are two separate grids, so their label columns and their button columns carry `SharedSizeGroup` names and WPF gives each pair the wider of the two — both text boxes then start and end at the same x, and the `+` / `✕` sit flush with `...` / `Open`. Setting `Width` on the new box instead lines the two up at exactly one window size: `Folder` lives in a star column that grows, a constant does not, and this window is resizable from 660 upwards. It is the same lesson the connection grid at the top of the file already records about absolute margins. Measured at four widths with the star column growing from 829 px to 2139: both edges identical at every one.

> **`DataBlockItem.ToString()` returns the name**, and that is not decoration — the same trap `GroupNode` hit in the config editor. A `ListViewItem` takes its **automation name** from the bound object's `ToString()`, so until this was added every row announced itself as `Satellite.DataBlockSnapshot.Capture.DataBlockItem`, to a screen reader as much as to a test. The columns are what the eye reads; `ToString` is what everything else reads.

### Remembering the web server credentials

Capturing settings across a plant means launching this window many times in an afternoon, and the CPU's web server wants a user and a password every time. They are therefore remembered — but where, and under what rule, is the whole of the design:

```
%LOCALAPPDATA%\PLC-Framework\credentials.json
```

```jsonc
{
  "entries": [
    { "project":  "E:\\proyectos\\Planta1",
      "plc":      "PLC_1",
      "address":  "192.168.0.20",
      "user":     "webclient",
      "password": "AQAAANCMnd8BFdERjHoAwE/Cl+sBAAAA…" }   // DPAPI, CurrentUser
  ]
}
```

**Keyed by project directory plus PLC name, because one TIA project routinely holds ten CPUs and each has its own user and password.** Any design keyed by project alone — a pair in `.env`, a pair in `config.json` — has ten CPUs overwriting each other, and two open projects overwriting each other again. Both keys already arrive in the handoff, so the Add-In needed no change. The device name is the key rather than the address because a CPU can be readdressed and has several addresses anyway; the address is the fallback for a window started by hand, where no project handed a name over.

**Not inside the TIA project.** That was the first instinct — next to the exports, in `.plc-framework\` — and it is wrong for one reason: TIA projects are under version control, `.version-control\` sitting right beside it, so a credential file there reaches a commit or a zipped copy eventually. Per user, outside the project, is what makes that impossible.

**The password is DPAPI-protected at `CurrentUser` scope**, with application entropy so a protected blob from some other program cannot be pasted in and decrypted. State the consequence plainly: **the file does not travel.** Another user, or the same project on another station, gets nothing back and the credentials are typed once more. That is the price of not having a shared key — and a key baked into the executable would not be encryption but obfuscation, which is worse than plaintext because it looks safe.

**Nothing is stored until the CPU has accepted the credentials.** `CaptureRunner.Run` takes an `authenticated` callback and invokes it on the line after `client.Login()`, so a wrong password never reaches disk and never has to be removed from it. The runner itself knows nothing about credential storage; it only reports that the login went through.

A *Remember* checkbox sits on the same row as the user and the password, ticked already when something is stored for that CPU; its tooltip carries the full sentence the label has no room for. Unticking it and capturing **forgets** the entry, which is what unticking means. It starts unticked on a CPU never captured before: storing a password nobody asked to store is not a good default. The timeout moved down to a row of its own, right-aligned — it is a setting one touches once, and giving it a column beside the credentials was what squeezed the two fields that matter.

**The store never throws.** A corrupt file, a blob written by another Windows user, a missing folder — every one of them degrades to "nothing remembered" and the window opens normally. A credential cache that breaks the application it exists to smooth would be worse than no cache.

### Showing the password

An eye inside the password field reveals it, and that matters more once credentials are remembered rather than typed: when a password arrives pre-filled from the store, revealing it is the only way to check *which* one came back.

WPF gives no help here. `PasswordBox` cannot display what it holds, and `Password` is not even a dependency property, so there is nothing to bind a "reveal" flag to. The shape that works is **twin controls in one grid cell** — the `PasswordBox` and a plain `TextBox` — with one of the two always `Collapsed`:

```csharp
private string CurrentPassword() =>
    RevealButton.IsChecked == true ? PasswordPlain.Text : PasswordBox.Password;
```

Three details are what make it feel like one control rather than two:

- **The text is carried across on every toggle**, in both directions, so editing while revealed and then hiding does not lose the change.
- **Focus and caret follow.** Without that, the operator carries on typing into a control that is no longer on screen, which is indistinguishable from a dead keyboard.
- **Everything else asks `CurrentPassword()`**, never either control directly, so no caller has to know the value lives in two places.

The icon is drawn in XAML — two paths and an ellipse — rather than loaded from an asset: an `.ico` would have to be embedded, resolved and themed for sixteen pixels' worth of picture. It shows a **struck-through eye while the password is visible**, stating what is true now rather than what clicking would do.

Exercised through UI Automation against the running binary: the plain field is absent from the accessibility tree while collapsed, revealing shows the password the store handed back, and a value typed while revealed survives the round trip out to the `PasswordBox` and back.

### How the Add-In launches one

`AboutAction` is the worked example and the shape generalises:

```csharp
string path = InstallPaths.Tool(ExecutableName);
if (!File.Exists(path)) { /* notifier.Error naming the missing path */ return; }

string error = launcher.Start(path);
if (error != null) { /* notifier.Error with the reason */ }
```

Three things there are deliberate:

- **The "not installed" check sits in the action, not in the adapter.** It is the expected failure, and it deserves a message naming the missing path rather than whatever the process API happens to say.
- **`IProcessLauncher.Start` returns the failure as a `string`, not as an exception**, so the Add-In reports it through `ITiaNotifier` like every other problem.
- **`ExecutableName` is the satellite's `AssemblyName`.** Renaming that project breaks this at runtime rather than at compile time: the two sit in different layers on purpose, so nothing binds them at build time.

The adapter wraps Siemens' `Process` — what `ProcessStartPermission` authorises — and is duplicated per version for the reason given under [*Known V20 / V21 divergences*](openness-notes.md).

## Reporting a coding-style check

`Satellite.CodingStyleReport` opens when *Check coding style* runs in TIA Portal. **The Add-In computes and the satellite shows**: the Add-In walks the selection, checks every name and sends the result on the window's standard input, so nothing is written anywhere nobody asked for a file. Writing the report to disk is the window's job, when the operator exports it.

**The document is `Core.Checks.StyleReport`, one definition for both ends.** The Add-In serializes it and the satellite reads it with the same type, so the two cannot drift into the window showing less than was sent. It carries the project, what was selected ("2 block folders"), a UTC timestamp, the framework version, every row, and **the rules the rows mention, with their patterns and descriptions**. A report is read after the fact — exported, attached to a ticket, imported next week — and the configuration it was checked against may have changed or be on another machine by then; explaining an old result with today's rules would describe rules that did not produce it.

- **Every type in it is public with public setters.** The Add-In serializes it inside TIA's partial-trust sandbox, which refuses anything less. Built, serialized and read back inside an `AppDomain` granted `Execution` alone, identical after the round trip.
- **It carries a `format` number**, today **2**: a row names its PLC, its software unit and the object it belongs to, and its path no longer repeats the first two. A window reading a report from a newer framework says so rather than showing half of it.
- **Named `StyleReport`, not `CodingStyleReport`.** Inside the namespace `Satellite.CodingStyleReport` a type of that name loses to the namespace and every use fails to compile — the same trap as a namespace called `AddIn.Core`.
- **No instance guard.** Each run is a report of its own selection at its own moment; a single window would refuse the second or throw the first away, and two side by side is how a before-and-after gets compared.
- **Two empty states, two sentences.** Started by hand, it says how to get a report — from TIA Portal, or by importing one; handed something it cannot read, it says that, with the reason in the status line — sending the operator back to repeat what already failed would be the wrong advice.

Verified here with the payload the action really produced: handed over on standard input the way `ProcessLauncher` does it and through the fallback path argument, twelve rows with their header and counts, a second window opening beside the first, and both empty states.

### Reading the table

- **Where a row comes from is three columns, not one path**: **PLC**, **Software unit** and **Object**. A project holds several PLCs, a PLC several units, and each repeats the same folders and often the same names, so a single path could be read but not filtered. The unit column shows **`*`** for the general program, and **Path** now holds only the folders inside that PLC or unit.
- **Every row names the object it belongs to, its own included.** Which tag table a constant is in is the first question a member row raises, and the row above it only answers that until somebody sorts the table or filters the failures. With the object on the row, "everything of this table" is one filter — and an object row naming itself is what makes that filter catch the table and its contents together.
- **The outcome colours the whole row**: red for a name that matched none of its rules, amber for what could not be judged — not configured, or skipped with a reason. The two are different problems, and an unjudged row painted red would read as a naming mistake nobody made.
- **Four outcome chips carry the counts and are the filters.** On shows those rows, off hides them, so the bar is also the summary; *Failed* comes first because it is what the window is opened for. A kind drop-down narrows to one object type or interface section, and a search box looks through every column — PLC, unit, object, name, path, rules and notes. **Ctrl+F** reaches it and **Esc** clears it.
- **Each row stands on its own under a filter.** A failing member shows without its object, whose name its path already carries: pulling the object in for context would put a passed row on screen under a filter that hides passes.
- **A row whose outcome the window does not recognise is never hidden** — filtering away what cannot be classified would hide it for good.
- **Hovering a Rules cell shows the rules behind it, as the report carried them**: the matched ones in full, pattern and description; the suggestions by pattern and first line only. A failing FC is offered nine rules, and nine full descriptions is a tooltip taller than the screen — what a suggestion has to say is which rule the name was aiming at.
- **A cell cut short hands its full text to a tooltip**, and an empty one opens none: an empty string would still show a small blank box.

Exercised through UI Automation on the real payload: counts on every chip, every combination of chips down to "No row matches the filters", a kind filter, searches that hit a name, a note and a rule, Esc and Ctrl+F, and the tooltip text read back for a matched rule, nine suggestions and an interface rule. Checked by capture at the default and the minimum size, where the chips wrap and both scroll bars show.

### Exporting the report

**`.xlsx` only, like every other export the framework writes** — the snapshot's workbooks and this one read and sort alike, and one format is one thing to import back. `Export .xlsx` writes `<project>-coding-style-<timestamp>.xlsx` into `<TIA project>\.plc-framework\reports\` by default, with the folder editable, a `...` to pick another and `Open` to show it. The timestamp is **when the check ran**, the report's own moment, not when it was exported.

- **Every row is exported, whatever the filters show.** A workbook of the failures alone would read as a clean report to whoever opens it next; the sheet carries an autofilter for narrowing.
- **Three sheets, and together they are the whole report**: *Report*, one row per report row — Result, Level, PLC, Software unit, Object, Kind, Name, Path, Matched, Suggestions, Note — coloured as the window colours it, header frozen, autofilter on; *Rules*, every rule the rows name with its pattern and description; *Info*, project, directory, scope, when it was checked and exported, framework version, report format, and the counts as numbers. That is what lets a workbook be opened again as a report.
- **Only the name cell of a member is indented**, not its whole row. The columns that say where a row comes from line up down the sheet, and a member reads as part of its object through the one cell that is about the object's contents.
- **Names are written exactly**: whitespace preserved, and members indented by cell alignment rather than by padding the name, so a cell holds the name and a filter on it still matches.
- **Suggestions only where nothing matched**, as the window shows them. The checker lists every rule a name missed, so a passed FB would otherwise carry ten "suggestions".
- **Never overwrites**: a taken name gets ` (2)`. **The folder is checked, and created, before anything is written** — `.plc-framework\reports` does not exist until the first export. **A failed write leaves nothing behind**: a half-written workbook would later be opened as the report.
- **Colours are darker in the workbook than in the window.** The window paints on a dark surface and a spreadsheet on white; the same red cannot serve both.

Verified with the OpenXML SDK's own validator — zero errors — on the report the action produces and on a synthetic one of 20,000 rows (written in under half a second), and by reading every sheet back: cell by cell, with styles, frozen pane, autofilter and its defined name, and a name ending in a blank kept whole. Through UI Automation: the default folder resolved and created on first export, a second export taking ` (2)`, an export with a filter on still carrying all twelve rows, an unwritable and an empty folder refused with a sentence, and no export row without a report.

### Importing a report

**Import is offered only to a window TIA Portal sent nothing to.** One opened from the menu is that run's report, and its header says which selection it checked; loading another workbook over it would contradict that. Started by hand, the window shows **Import .xlsx…** in its header and says both ways in: run the check from TIA Portal, or import a report exported earlier. The button stays after an import, so another report can replace it.

- **A workbook named on the command line is imported at start** — "Open with", or a shortcut. It is not a handoff: nobody in TIA sent it, so the window keeps its button, and a `.xlsx` argument is never read as JSON — which, before this, would have opened as a report that "could not be read".
- **It reads what Excel leaves behind, not only what the window wrote.** Opened and saved in Excel, a workbook has its text moved into the shared string table, and its columns may have been rearranged or its rows sorted. Columns are therefore found **by header**, every kind of text cell is read, and a sorted sheet simply gives rows in that order.
- **A workbook that is not a report is refused with the reason**, and nothing on screen changes: no *Report* sheet, a column missing — named, since reading the rest would present a report with a hole in it as whole — a report from a newer framework, or a file that is not a workbook at all. **An import that fails never costs the report already open.**
- **A report exported by an earlier version still imports**, with the columns it never had left empty. Only the columns the rows cannot be read without are required; PLC, software unit and object are not among them. Refusing a workbook somebody kept, over three columns that were not in the format it was written in, would be the wrong half of "refuse what cannot be read whole" — and an empty cell says "this report does not know", which is true, where a `*` would claim the general program.
- **A missing *Rules* sheet is tolerated**: the rows still read, and their tooltips say the rules are not described. A report without rows would say nothing; one without rule descriptions still says which names failed.
- **The header names the file** — "imported from …" — so an imported report is never taken for a fresh run. Exported again, it goes to its project's `reports` folder when the workbook records one, and beside the workbook it came from when it does not.
- **The file is opened shared for reading and writing**, so a report somebody still has open in Excel imports anyway.

The reader lives beside the writer in `ReportWorkbook`, with the sheet names, the columns and the *Info* keys spelled once for both: the two disagreeing about one would lose that fact on import without a word.

Verified: an export read back identical field by field, for the real report and for 20,000 rows (read in under 0.6 s, a trailing blank kept); a workbook opened and saved by the real Excel — shared strings, a column inserted in front, rows sorted by name — read back identical; refused with the right sentence, an unrelated workbook, one with the *Note* column deleted, one claiming format 9, and a text file renamed `.xlsx`; read without its *Rules* sheet; and read while Excel had it open. Through UI Automation: no button when handed a report, the button and the note when started by hand, a failed import through the dialog changing nothing but the status line — over an empty window and over a filtered report alike — a successful one filling table, chips and header, and a workbook named on the command line imported at start.

The scroll bars were light grey in every satellite until this window, whose table scrolls both ways, made it impossible to miss; they are now themed for all of them — see *Theming* in [architecture.md](architecture.md).

## Editing a configuration

`Satellite.ConfigEditor` opens from **"Config. Editor"** on the project root of both Add-Ins. Section navigation down the left rather than tabs — two of the four sections subdivide again — and a **dot beside a section** marks where the problems are, which costs nothing because the validator already reports per concern.

**The JSON tree is the document.** Editing it in place is what lets a key the model has never heard of survive a save, and this file is meant to be hand-edited, so somebody's extra key is not a bug to clean up. Every key and its order are preserved; the original whitespace is not, because keeping that would mean editing text by character offsets and every edit could then corrupt the file.

Three rules the window follows, and each exists because the opposite was worse:

- **Structural problems block `Save`; environmental ones never do.** A configuration prepared here for another station is not wrong because a drive is not mapped on this one.
- **A file that exists but does not parse is never offered the template buttons.** Either would overwrite it, and a file somebody broke by hand is still a file somebody wants back. It shows the parser's line and column instead.
- **Creating from a template writes nothing until `Save`**, so backing out costs nothing.

### Which template a new configuration starts from

A project with no `config.json` offers two starting points, and **the operator chooses**:

|  |  |
| --- | --- |
| **Create from system template** | the template built into the editor — always the contract this version of the framework expects |
| **Create from user template** | `%LOCALAPPDATA%\PLC-Framework\config.template.json`, shaped to a plant. Always shown; disabled, with the path in its tooltip, when there is no such file |

**This replaced a rule that chose silently, and the silence was the defect.** The per-user file used to win whenever it existed and parsed — but the editor writes that file itself, so a station that never customised anything kept the template of whichever version first opened the editor, and every framework upgrade after that was ignored without a word. On the VM it produced a brand-new configuration with the old rule catalogue, no interface rules and no interfaces.

- **No fallback.** If the chosen template cannot be used, the status line says why and the panel stays, so the other one is a click away.
- **A user template that does not parse is reported, never replaced.** It is still one somebody shaped.
- **Creating from the system template leaves a copy where the user template lives, only when there is none**, and says so — that is how there comes to be something to customise. It is only ever used when chosen, so it can age harmlessly.
- **Revert, before the first save, returns to the template the document started from.**

### The rules, and what a type implements

The `Coding style` section is where the editor stops being a form and starts preventing mistakes. It has three tabs — **Object rules**, **Interface rules** and **Applies to** — and the two catalogues share one area, since a rule looks the same in either; the tab decides which list is shown, each tab keeps its own selection, and a hint beside the tabs says what the visible one is for.

- **Ids are unique across both catalogues.** Renaming a rule onto an id the other catalogue uses is adjusted rather than allowed, and says so in the status line.
- **Removing an interface rule takes its references out of every object rule's interface**, and removes a section it leaves with nothing to implement — such a section would be invalid, and it checks nothing anyway. A type left with nothing to implement stays, because its row is on screen to be fixed.
- **An object rule's interface is edited in its own detail.** One row per section: the section chosen from the eight TIA spells (`Input` … `Constant`, `Tag`, `UserConstant`), never typed, and never one another row already has; the interface rules it answers to as tags, a dangling one in red; a `+` that offers only interface rules. It is the same row as *Applies to*, fed a different closed set and catalogue, so both lists behave alike. "Add section" stays disabled while no interface rule exists, and removing the last section removes the `interface` key.
- **At the window's minimum size the rule detail scrolls rather than squeezing**, and a row's tags wrap below its drop-down rather than being cut — both found by capturing the window at that size, not by reading the XAML.

- **`implements` is chosen, never typed.** Each type shows only the rules it implements, as tags with a cross, and a `+` offers the ones it does not have yet. Building the choice from the catalogue makes a dangling reference **impossible** rather than merely detectable — half of what the validator exists to catch, removed at the source. The `type` comes from a drop-down of its section's closed set, which removes the other half.
- **A reference to a rule that does not exist is shown in red, not hidden**, so the editor can repair a hand-edited file rather than only complain about it. Tick boxes could not: with no box to clear, there was nothing to click.
- **Renaming a rule carries its references with it**, and refuses to collide. Committed on leaving the field, not per keystroke — an id is a key, and rewriting references on the way from `type` to `typeName` would churn the document through a dozen half-typed names.
- **The pattern says whether it compiles, and a "Try it" box says whether a sample name matches.** A regex that compiles can still be perfectly wrong, and otherwise that is only discovered once the Add-In has marked half a project.
- **A type appears at most once per section.** A second row for the same type says nothing the first does not, so a type in use is offered nowhere it could be duplicated.

### The token

`config.json` always carries the literal `${GITHUB_TOKEN}`; the secret goes to the per-user `.env`. The field is masked with the same twin-control eye as the PLC password, and the `.env` is written **before** the JSON — there is no point leaving a `config.json` behind that references a variable nobody managed to set.

### One editor per TIA Portal

The satellite works out which TIA launched it **by itself**, from its own parent process — which is TIA because the launcher starts it with `UseShellExecute=false`. The Add-In could not tell it: under partial trust `Process.GetCurrentProcess()` throws `SecurityException`, measured in a restricted `AppDomain`, and `AddIn.Utilities` holds only `Process` and `ProcessStartInfo`. Reading the parent costs ~170 ms once, cannot be forged by editing the handoff, and degrades correctly when started by hand: no TIA parent, so the guard falls back to one editor per file. Confirmed on the VM.

> **`ConfigLocation.Resolve` accepts all three ways of naming a project** — the project folder, the `.plc-framework` inside it, or the `config.json` itself. Appending the convention to whatever was picked is wrong the moment somebody picks one step deeper, which is the natural thing to do since `.plc-framework` is the folder with the file visibly in it. It produced `…\.plc-framework\.plc-framework\config.json` and a window reporting "no configuration" on a project that had one.
