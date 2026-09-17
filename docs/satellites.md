# The satellite apps

A satellite is a WPF application the Add-In launches from the TIA menu. This page covers what each one does and the decisions behind it.

**Every satellite with a form wears the same header**: the logo at 38 pixels, the window's name at 18 SemiBold in the brand ink, and a line under it at 11 in the muted one saying what it is working on. It comes from `UI.Shared/Controls/BrandHeader.xaml`, so it cannot drift again — which it had, into three sizes and two spellings of the same grey, before the four of them were pulled onto one control. `Satellite.About` is the exception and stays one: it has no form, and the logo *is* its content.

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
- **It carries a `format` number**, today **3**: a row names its PLC, its software unit and the object it belongs to, its path no longer repeats the first two, and a member row names the members it is declared inside. A window reading a report from a newer framework says so rather than showing half of it.
- **Named `StyleReport`, not `CodingStyleReport`.** Inside the namespace `Satellite.CodingStyleReport` a type of that name loses to the namespace and every use fails to compile — the same trap as a namespace called `AddIn.Core`.
- **No instance guard.** Each run is a report of its own selection at its own moment; a single window would refuse the second or throw the first away, and two side by side is how a before-and-after gets compared.
- **Two empty states, two sentences.** Started by hand, it says how to get a report — from TIA Portal, or by importing one; handed something it cannot read, it says that, with the reason in the status line — sending the operator back to repeat what already failed would be the wrong advice.

### The window opens before the check, not after

**Because an operator restarted his machine over it.** A check of a whole PLC takes long enough that TIA sits busy with nothing on screen, and the only reasonable reading of that is that the Add-In died. The Add-In now starts the window *first*, works, and writes the report into the process it already started.

- **It says what is being checked while it waits**: "Checking the coding style of TestSlave — 2 block folders…", the same facts the header will carry afterwards, so one window among several is recognisable before any of them has a report. They travel as a `CheckingNotice` on the command line — the handoff itself stays one JSON document read to the end of input, rather than growing a header and a second parser.
- **A moving bar, not a percentage.** The check knows how many objects it has, not how long each will take, and a bar that guesses is worse than one that only says work is happening.
- **The handoff is read on a thread of its own**, so the window paints and can be moved while TIA works.
- **Four endings, four sentences**: the report arrives and fills the table; a report arrives that cannot be read; **the input closes with nothing in it**, which is the check having broken off — TIA was stopped, or it ran into something — and says so instead of waiting forever; and nothing selected, which is an *empty report* rather than a closed pipe, because "no rows" and "it never finished" are different facts.
- **It waits only when the Add-In said it would.** Both the notice and redirected input are required: a window started any other way shows the empty state as before, and so does one launched by an Add-In older than this window, which sends no notice and is served by the path that reads the handoff at startup.

**And TIA itself says it is busy**, through its own exclusive access: the caption names the check and the project, the text says "Checking 26 of 65 — DB_FILLER_20", and the dialog's Cancel stops it between one object and the next. Without it the satellite would be the only thing moving while TIA sat there ignoring clicks with nothing to say for itself.

- **Cancelling produces no report.** The window is already open, so closing its input with nothing is what tells it the check did not finish — and TIA says so too. A report of whatever had been reached by then would be one with a silent hole in it.
- **A busy state TIA will not give is not a reason to refuse the check**, which then runs without one.

> **An Add-In may not hold a Siemens engineering object in a field**, which is how the first version of this was written and how the Publisher stopped it: it refuses to package an Add-In whose member holds one, because from V20 an Add-In is not reloaded between executions. `IProcessLauncher` therefore takes the work as a delegate — `Start(fileName, arguments, Func<string> payload)` — so the process stays a local variable while the check runs inside it.

Verified here with the payload the action really produced: handed over on standard input the way `ProcessLauncher` does it and through the fallback path argument, twelve rows with their header and counts, a second window opening beside the first, and both empty states.

### Reading the table

- **Where a row comes from is three columns, not one path**: **PLC**, **Software unit** and **Object**. A project holds several PLCs, a PLC several units, and each repeats the same folders and often the same names, so a single path could be read but not filtered. The unit column shows **`*`** for the general program, and **Path** now holds only the folders inside that PLC or unit.
- **A member checked inside a `Struct` shows its own name, and its parents beside it.** `maxSpeed` declared in `motor` is the name a rule was held against, and **Parent** says `motor` — or `motor.drive` further in. Joining the two into `motor.maxSpeed` is what the object model does, and no naming rule can read it: every such member fails.
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
- **Three sheets, and together they are the whole report**: *Report*, one row per report row — Result, Level, PLC, Software unit, Object, Kind, Parent, Name, Path, Matched, Suggestions, Note — coloured as the window colours it, header frozen, autofilter on; *Rules*, every rule the rows name with its pattern and description; *Info*, project, directory, scope, when it was checked and exported, framework version, report format, and the counts as numbers. That is what lets a workbook be opened again as a report.
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
- **A report exported by an earlier version still imports**, with the columns it never had left empty. Only the columns the rows cannot be read without are required; PLC, software unit and object — format 2 — and a member's parent — format 3 — are not among them. Refusing a workbook somebody kept, over three columns that were not in the format it was written in, would be the wrong half of "refuse what cannot be read whole" — and an empty cell says "this report does not know", which is true, where a `*` would claim the general program.
- **A missing *Rules* sheet is tolerated**: the rows still read, and their tooltips say the rules are not described. A report without rows would say nothing; one without rule descriptions still says which names failed.
- **The header names the file** — "imported from …" — so an imported report is never taken for a fresh run. Exported again, it goes to its project's `reports` folder when the workbook records one, and beside the workbook it came from when it does not.
- **The file is opened shared for reading and writing**, so a report somebody still has open in Excel imports anyway.

The reader lives beside the writer in `ReportWorkbook`, with the sheet names, the columns and the *Info* keys spelled once for both: the two disagreeing about one would lose that fact on import without a word.

Verified: an export read back identical field by field, for the real report and for 20,000 rows (read in under 0.6 s, a trailing blank kept); a workbook opened and saved by the real Excel — shared strings, a column inserted in front, rows sorted by name — read back identical; refused with the right sentence, an unrelated workbook, one with the *Note* column deleted, one claiming format 9, and a text file renamed `.xlsx`; read without its *Rules* sheet; and read while Excel had it open. Through UI Automation: no button when handed a report, the button and the note when started by hand, a failed import through the dialog changing nothing but the status line — over an empty window and over a filtered report alike — a successful one filling table, chips and header, and a workbook named on the command line imported at start.

The scroll bars were light grey in every satellite until this window, whose table scrolls both ways, made it impossible to miss; they are now themed for all of them — see *Theming* in [architecture.md](architecture.md).

## Updating a project's core

`Satellite.CoreUpdater` opens from **"Core updater"** on a PLC — not on the project root, and not on a block: a core belongs to one PLC's software, so an entry anywhere else would either have to ask which PLC it meant or offer a whole-PLC operation from a single object.

**It is the one satellite that talks to TIA Portal itself.** Every other one is handed what it should work on, because it cannot ask. This attaches to the running TIA Portal as an Openness *client* and finds the project that way, so the Add-In hands over nothing — no payload, no handoff, and none of the bugs where the two ends disagree about what was selected. The Add-In entry is one line that starts an executable.

- **It attaches to the TIA Portal that has the Add-In's project open**, which the Add-In names on the command line. With only one running there is nothing to choose between; when nothing identifies one it **refuses rather than guesses**, because guessing would attach to somebody else's project and then offer to change it — and hands the list to the operator instead.
- **Either way it lists what was running.** "No TIA Portal is open" and "two are, and neither is the one that started this" look identical from the window and want opposite answers.
- **A project that was never saved is attached and still unusable**, and the window says so: the core is copied into the project's own folder, and there is no folder yet.
- **It maps the PLC you picked.** The Add-In sends the PLC that was right-clicked and `*` for the general program; the window offers that PLC's software units so the star is a starting point rather than a decision. *Map project* walks blocks, PLC data types, tag tables and every folder, reads what each says about itself, and writes `repo\project.json`.
- **Everything, not only what looks like the core.** A block downloaded into the wrong folder, and a folder built by hand that holds core blocks, are two of the three discrepancies this exists to find — and neither is visible from a list of core blocks alone.
- **What a block says about itself travels with it**: the `TITLE` line carries the core's metadata, and its `family` names the folder the block belongs in. Held against where it actually sits, a misplaced block shows up **without the repository being reachable at all**.
- **In TIA V17-V20 every block and type is exported to read its title**, because those versions expose no `Title` at all - measured on the VM, where even the untyped escape hatch refuses it. A block would still have its native `VERSION` and `FAMILY`; a PLC data type has neither in any version, so without the export a core UDT is indistinguishable from one of the plant's. It costs one export per object, so the window counts as it goes, and the files are written, read and deleted one at a time under the project's own `repo\tmp\`. **V21 needs none of it** and reads the property directly.
- **A map with holes says where they are.** A folder that would not read is a line of its own, not a silent omission: the one thing a comparison must never be handed is a map that looks complete.
- **It disposes nothing, and that is not an oversight.** `TiaPortal.Dispose` is how an Openness client shuts a portal **down**, and attaching to one does not change what the method means — a `using` around an attached portal closed the engineer's TIA Portal, with their project open, the moment the window said "Ready". The handles it holds instead are released when the window closes and the process ends.

### Finding the right TIA Portal among several

**The first version got this wrong, and only a station with two instances open could show it.** It matched its own parent process against the processes `TiaPortal.GetProcesses()` lists, on the reasoning that an Add-In runs inside TIA Portal so the process that started the satellite *is* the one to attach to. Measured on the VM, in both TIA versions, it is not:

| | Parent of the satellite | What Openness listed |
| --- | --- | --- |
| V21 | 12896 | 12924, 5236 |
| V20 | 14736 | 7756, 2480 |

Neither parent is among the portals — TIA either hosts the Add-In in a process of its own or starts the child through an intermediary, and which of the two is still not settled. **With one instance open it worked anyway**, because a lone TIA Portal needs no identifying, which is why this survived until somebody opened two.

So the signals are three, in order, and **each has to name exactly one instance or it says nothing**:

1. **The project.** The Add-In passes the open project's own file as a third argument, and `TiaPortalProcess.ProjectPath` says what each running instance has open. The Add-In is *in* a project, so this is the one fact both ends name identically — and it is the same string the window was already printing in that list.
2. **The ancestry**, now the whole chain rather than the immediate parent: whatever is hosting what, the TIA Portal is an ancestor if it is anywhere at all. One WMI query for the machine, walked in memory, and **read on the worker's thread** — four queries at 170 ms each, before the window paints, is what a satellite that failed to start looks like.
3. **Only one is running**, which is not a guess.
4. Otherwise **the operator picks**, from the same list that used to be the dead end. Nothing is preselected: a highlighted first row turns *Attach* into one click on whatever happened to be at the top, which is the guess this panel exists to replace with a decision.

**Two answers is no answer.** The same project open in two instances, or a chain running through both, identifies nothing — and taking the first would be the guess again, so it falls through to the operator.

**An older Add-In still works.** It sends two arguments and simply has nothing to say about which instance, which is exactly where it was before; blank and absent are read as the same thing, so the positions stay fixed whatever is known. A project that was never saved sends an empty third argument for the same reason.

> The plain `ListBox` in that panel is what found the framework's last unthemed control: there was no implicit `ListBox` style, so the stock one painted a **white** background behind rows in `Ink` — light grey on white. It is themed in `Controls.xaml` now, so every window gets it. `ListView` is untouched, an implicit style matching the exact type, which is the rule that already forces three separate row styles.

### Choosing what to map, before paying for it

Picking a PLC and a unit **counts what is in there without reading any of it**, and offers the answer as a grid: one row per kind of object, and on that row the languages found **inside that kind**. Untick what this run is not about, and only what is left is mapped.

```
Objects   All  None        Languages   All  None

[x] OB (5)                 [x] LAD (4)   [x] SCL (1)
[x] FB (31)                [x] SCL (22)  [x] GRAPH (6)  [x] LAD (3)
[x] FC (13)                [x] SCL (12)  [x] STL (1)
[x] GlobalDB (28)          [x] DB (28)
[x] PlcStruct (91)
[x] PlcTagTable (9)
```

**The language belongs to the kind, and one flat list could not say so** (2026-09-17). Across the whole map, unticking `SCL` to drop a function block took every SCL function with it — "the SCL functions and every data block" was not a filter that could be expressed, though it is the ordinary thing to want. A language only ever means anything inside the kind it was counted in.

**The rows read in the order a PLC does** — OB, FB, FC, the data blocks, then the types and tables underneath them — not by size. Most-numerous-first moves a row every time the project changes, and the operator is looking for a row by name. A kind this order has never heard of follows, by name, so one added to the object model shows up rather than disappearing. The languages *inside* a row are ordered by count, because there the question is what this row is mostly made of.

**The point is what a walk costs, and it costs it in one version only.** A kind and a programming language are typed properties in every TIA version, so counting a four-hundred-object PLC is one pass over the tree. Mapping it in V17–V20 is four hundred exports. Deciding *before* the export is the whole of the difference; filtering afterwards would pay the minutes and then throw the answer away. V21 reads a title from a property and is fast either way — the panel is there too, because the same window serves both and an operator should not have to know which of them is the slow one.

- **The choices come from the project, not from a list in the code.** `PlcBlock.ProgrammingLanguage` has twenty-nine values and a project uses four or five; a fixed list would be mostly empty boxes, and it would put Siemens' vocabulary — versioned by TIA release — inside a binary, which this project has refused three times already. **The day a GRAPH block arrives it appears on the row it belongs to, with its count, and nothing changes here**: that is what makes the grid ready for it rather than a table that has to be kept up to date. The counts also answer the question the operator actually has, which is not "does TIA have GRAPH" but "how much GRAPH is in *this* PLC".
- **A row with no languages is an ordinary row.** A PLC data type and a tag table have no programming language at all, so `PlcStruct` and `PlcTagTable` carry a kind box and nothing beside it — and ticking `SCL` on another row cannot touch them. Getting that wrong would delete all 91 UDTs, and 91 of the core's 249 sources *are* data types. Null and empty both mean "has none"; the first version of the rule only handled null and a test found it.
- **Everything starts ticked**, which is what this window did before there were any boxes. `All` / `None` act on a whole column, so narrowing to one kind is two clicks rather than eight.
- **A kind's own box gates its languages.** With the kind off, what it is written in decides nothing, so those boxes go dead rather than inviting a filter that has no effect.
- **Nothing ticked is refused, not read as everything.** An empty filter means the whole scope — the safe reading of a decision nobody made — and an operator who has just cleared a column has very much made one. So *Map project* switches off and says so. **The same rule one row down**: a kind ticked with none of its languages would map nothing, and the row is named rather than silently skipped.
- **A filtered map records its filter and says so.** `project.json` carries `filter` beside the scope — one entry per kind, each naming the languages wanted inside it — and the window's heading reads *"Of what was ticked, not of the whole scope"*. Without that, a map covering only FBs says the project has no FCs, the silent hole this whole design exists to avoid. **A map written before the shape changed is refused rather than half-read**, in both directions: its filter names members this version cannot see, so it would come back looking like a map of the whole PLC. Unlike a coding-style report, which is somebody's record of a moment, this file is a snapshot of the project right now and is rebuilt by one click.
- **The survey and the map walk the same code.** Two copies would drift, and the pair is worth nothing the moment the counts an operator ticked against and the map they then asked for cover different folders. **`Core` does the counting**, through a builder the adapter feeds one object at a time — how many of a kind there are is not a TIA question, and two adapters keeping their own tallies is exactly how they come to disagree.

### Holding the project against the core

*Compare with core* reads `repo\project.json` back off disk, brings the project's copy of the core up to date, and holds one against the other. **Metadata only** — a version, a status and a family, never a line of code.

```
6 of 18 objects come from the core, held against 264 core nodes.

    1 up to date
    1 outdated
    1 at a version the core does not define
    2 in a folder their family does not name
    1 whose TITLE and VERSION header disagree
    12 not from the core

    241 current core nodes the project does not have
```

- **The core is always copied into the project first**, and read out of the copy. A comparison against a repository that moves on describes a core nobody can point at afterwards; with a copy, both halves are on disk side by side for as long as the project keeps them. Configuration, repository, mirror and `core.json` are one call, `CoreRefresh` — the copy has to happen before the catalogue is read out of it, and a caller that got that backwards would compare against whatever the last run left behind.
- **A project that names no core is a normal state**, not a failure, and the status line says which: "no core" is a fact about the project, "not compared" is something to go and fix.
- **A finding is a list, not a verdict.** A block can be outdated *and* sitting where its family does not otherwise live, and one word would lose half of what has to be fixed.
- **The `TITLE` decides; TIA's `VERSION` header is what a block falls back on.** The TITLE is the contract the core's own generator writes, and a PLC data type has no header at all in either TIA version. **The two disagreeing is its own finding** — such a block is neither up to date nor outdated, it says two things, and what it needs is an edit rather than an import. `1.1` and `1.1.0` are one version, not two: they arrive from a hand-typed TITLE and from a `System.Version` rendered back to a string.
- **Nothing compares one version as greater than another.** Outdated means the core marks *that* version `deprecated`, and it carries the replacement the core names. A version the graph does not list is reported with the versions it *does* list beside it — and with nothing beside it when the core has never heard of the name, which is what a block claiming a `core/` family it is not entitled to looks like. `core.json` is the source of truth, so that is the whole answer.
- **A family is compared within one tree.** `core/node` holds a function block *and* the data type it works on, and TIA files those under `Program blocks\…` and `PLC data types\…` — two folders by construction, for every family that spans both. The first version reported every one of them as split. **It never names which copy is in the wrong place**: the core's `family` is a path in the repository and a project folder is whatever the hierarchy calls it, with no mapping between them, so what it says is that the family is not in one place.
- **What the project is missing is only answered when the map covers everything.** On a filtered map it is not listed and the window says why, because "the project has no UDTs" and "this map did not look for them" are different facts.
- **Nothing is written.** `project.json` is the durable half; the comparison is recomputed each time, which also means the format guard on that file is what stands between an old map and a comparison quietly reading it as complete.

### The two panels

**The project on the left, the repository on the right** — the layout settled before any of this was built, and drawn out in full by the maintainer once both halves worked. **Two trees, both open**, so the two sides read alike and neither has to be unfolded before it says anything.

```
PLC  [KF1022            v]   Software unit  [*                          v]

Project tree  [Filter]        |  Core tree  [Filter]
+---------------------------+ | +---------------------------------------+
| v *  (6)                  | | | v adt  (1)                            |
|   v Program blocks  (3)   | | |     [ ] EAdtConstants v1.1   absent    |
|     v core  (2)           | | | v adt/queue  (4)                      |
|       v adt/queue  (1)    | | |     [ ] EQueueMethod v3.0  in the …   |
|           _queue v3.0 FC  | | |     [ ] EQueueStatus v3.0  absent     |
|             up to date    | | |                                       |
+---------------------------+ | +---------------------------------------+
Project - 6 of 8 objects …    |  Repository - 249 current nodes, 5 in …
[Map project] [Sync folders…] |  [Compare with core] [Download and import]

Compared.
```

- **Each side owns its filter, its caption and its buttons.** What a control acts on is the panel it sits under, rather than something to remember.
- **The filters open from a button over each tree.** As three columns of tick boxes they were taking a third of the window for decisions taken once per run, above the two trees somebody opened it to read. The left popup holds the map filter — kinds, and the languages inside each — beside the finding boxes; the right one holds the core's states. Both have `All` and `None`.
- **The line between the two is fixed and centred**, a one-pixel border rather than a splitter: the trees exist to be read against each other, and a divider one operator dragged is a layout the next one has to put back.
- **The caption is under its tree, one line, with the whole of it on hover** — a map's counts kind by kind do not fit a line, and trimming with a tooltip is what this framework already does to a cell it cannot fit.
- **It compares by itself when it opens, mapping first if there is no map**, and then never again on its own: changing the PLC or the unit is the operator steering, and re-walking several hundred objects under them would be the opposite of helpful. Both buttons stay.

```
Project — 7 of 10 come from the core        Repository — 247 current nodes, 4 in the
[x] Up to date (2)   [x] Outdated (2)         project, 3 at another version, 240 absent
[x] Unknown version (1)  [x] In the wrong folder (2)
[x] Disagreeing (1)  [ ] Not from the core (3)  [x] In the project (4)
                                                [x] At another version (3)  [ ] Absent (240)
v *  (7)
  v PLC data types  (2)                       v datetime  (1)
    v node  (1)                                   _dtlToString v1.3  the project has v9.9
        nodeLink v1.1 PlcStruct  its family…   v motion-control  (1)
    v spare  (1)                                  _mc_positioning1Axis v2.0  the project has v1.1
        nodeLinkDestToSource v1.1 PlcStruct…   v node  (2)
  v Program blocks  (4)                           nodeLink v1.1  in the project
    v 04-MC  (1)                                  nodeLinkDestToSource v1.2  the project has v1.1
        _mc_positioning1Axis v1.1 FB  outda…
```

- **The project tree comes out of the map, not out of a rule.** A mapped object's folder already begins with TIA's own name for its tree — `Program blocks/03-ALL/adt`, `PLC data types/node`, `PLC tags/enums` — because the walk starts at the software's root groups. All the window adds is one root for the scope the map covers: the software unit, or `*`.
- **A line says the same things on both sides**: name, version, and what there is to say about it — with the object's kind on the project side. The kind is spelled as the map spells it, `GlobalDB` and `PlcStruct` rather than `DB` and `UDT`, which is `config.json`'s vocabulary and the one the filter rows at the top of this same window already use. `DB` would read faster and would hide the difference between a global and an instance data block.
- **Every line says something.** A core block with nothing against it reads *up to date*, so no line on either side trails off into nothing.
- **The counts are the filters**, as in the coding-style report, on both sides now: by finding on the left, by state on the right. **"Not from the core" starts off** — in a real project it is most of the rows and this window is about the core — and the box stays on screen with its count, so nothing is hidden without saying how much. A row shows under **any** of its findings, so a block that is outdated *and* in the wrong folder does not vanish when one box is cleared.
- **A folder the tick boxes empty is not drawn.** Folders exist in either tree only because something is in them, so filtering to the outdated blocks shows the folders that hold outdated blocks rather than the whole tree with two leaves in it.
- **The right panel holds the whole core, not only the gap.** What gets imported is not only what is missing: a block held at a retired version, or one sitting where its family does not otherwise live, is downloaded again too. Only what the core still stands behind, though — offering a retired version would be offering something the core has itself withdrawn. The folder comes from each node's own `file` in `core.json` rather than from a family written in a TITLE: the graph is the source of truth and it carries the path.
- **What the project already has is what gets marked.** `absent` is muted — it is 240 of 247 rows — and `in the project` and `the project has v1.1` are the two worth picking out. On a filtered map a node the walk never covered reads *not covered by the map*, a fourth state rather than a wrong one, and its tick box appears **only then**: on any other run it would be a box that never does anything.
- **A walk and a comparison never share the screen.** They describe different moments, and leaving a map's counts under a comparison of another scope would be the kind of half-truth this window has spent four stages avoiding.

> **An empty folder cannot appear in the project tree**, and that is worth knowing before it is read as "there is no such folder". `project.json` records objects with their paths, not folders, so a folder somebody made and left empty is invisible here. Nothing is lost that the comparison needs — a folder holding core blocks in the wrong place still holds them — but it is a limit of the map rather than of the tree.

> The plain `ListView` and `TreeView` were the framework's last unthemed controls, and the evidence was already in the repo: three windows each set `BorderThickness="0" Background="Transparent" Foreground="Ink"` by hand on every one of them. A fourth that forgot got the stock **white**. Both are implicit in `Controls.xaml` now — see *Theming* in [architecture.md](architecture.md).

Checked against the real 264-node core rather than a fixture: each finding on names and versions the repository actually holds — `_mc_positioning1Axis` at v1.1 outdated behind the v2.0 its `deprecatedBy` names, `_dtlToString` at a version the core does not define with v1.2 and v1.3 offered beside it, a name the core has never heard of with nothing beside it, a UDT with no header judged by its TITLE alone, and a block outdated and split at once. The two directions: 247 current nodes, four bases held, 243 absent — and a base held at an *old* version counted as outdated rather than missing. On a filtered map, absent silent and the rest still judged. Then the whole chain from a `config.json` naming the real repository: 264 nodes copied into `.plc-framework\repo\core\`, read back out of the copy, validating clean, a node resolving to its own source file — and `coreSource: null`, a folder that is not there, and `remote` each answered in their own words. And rendered end to end on the real window.

The two panels were driven on that window with the same real core behind them. The project tree: one root named for the scope, under it the three trees TIA names, and under those the folders the map's own paths carry, each counted by what is below it however deep. A line reading `nodeLink  v1.1  PlcStruct  its family is in more than one folder`, and a clean one reading `up to date`. Both trees open to the leaves. Filtering to the outdated left two leaves and **took the folders that no longer held any** — while the one that kept a leaf stayed — nothing ticked left no tree at all, and putting the boxes back rebuilt it. On the right: one tick box per state with its count, no fourth box on a map that cannot produce one, narrowing to `In the project` leaving three nodes in three folders and the rest still open — and on a *filtered* map, `Absent (0)` with `Not covered (242)` beside it, and the root taking the unit's name.

### Downloading from the core

Tick blocks in the repository panel and press **Download**. Each one comes down **with everything it depends on**, as the sources the core keeps, and goes into the folder its family names: `Program blocks/core/adt/queue`, `PLC data types/core/node`.

- **Dependencies come from the graph, and only the ones the core defines.** 147 of the core's 296 edges point at Siemens' own blocks — `TP_TIME`, `TON_TIME` — and none of those is ours to import. Dependencies are written **before** what needs them, because a UDT has to exist before the block whose interface names it.
- **What you ticked is replaced; a dependency already at the core's version is left alone.** Asking for a block you already have is asking for it to be put back, which is how one that was edited or misfiled gets repaired.
- **A dependency at *another* version is where you get asked.** Replacing it changes the behaviour of every block that calls it, including blocks you did not select — so the window works out who else depends on it and shows you before writing anything. It is the only prompt here and it earns it. **It also says what it cannot see**: dependency lists come from each block's `TITLE`, so the plant's own blocks declare nothing and are not counted. A short list is a floor, not a total.
- **Nothing is compiled.** TIA refuses an inconsistent block, and compiling would change the project behind somebody who asked for an import.
- **Nothing is rolled back, and nothing pretends to be.** Openness has no transaction, so a refusal half way through leaves what already went in — named in the report rather than deleted, because removing blocks you asked for over a later failure is the worse outcome.
- **The comparison is thrown away afterwards**, with a line saying to map the project again: it described the project as it was a moment ago and no longer does.
- **The external source is a step, not a thing to keep.** It is deleted once the blocks are generated, so the folder does not fill with one per import and the next download does not meet its own leftovers.

> **One thing here is still unconfirmed.** `GenerateBlockOption` has only `None` and `KeepOnError` — no `Override` — so **what TIA does when the block already exists has to be measured on the VM**. It arrives as TIA's own words against that object's name, and the rest of the download carries on.

### A tag table is built, not imported

The core keeps its enumerations as `.xlsx`, and the first version handed the file straight to `PlcTagTableComposition.Import` — which takes a bare `FileInfo` and no format, next to a product that plainly does import Excel. **TIA Portal does; Openness does not.** On the VM it answered *"Invalid XML encountered while reading Simatic ML file: Data at the root level is invalid. Line 1, position 1."*

So the table, its constants and its tags are created one at a time. What that took:

- **The workbook is read by `Core`, without the OpenXML SDK.** Writing a real `.xlsx` is five XML parts in a zip whose only true test is whether Excel opens it, which is why the SDK earned its place for the exports; reading a shape this file already knows is one zip entry and two documents — and `Core` may not take that package at all, being loaded inside TIA Portal's process. What makes the reader worth anything is not the parser but the fifteen real workbooks it is run against.
- **Sheets by name, columns by header, never by position.** `EAdtConstants` puts `Constants` first, `system` puts `PLC Tags` first, and the parts are not reliably called `sheet1.xml` either. Both would have been wrong if assumed.
- **The first row of `Constants` is the table**, not a constant: its name is the table's, and **its comment is the `TITLE` line** — the same JSON a block carries, read by the same reader. Checked against `core.json` on all fifteen: the name is the node's `base`, and version, status and family agree with the graph.
- **The `Path` column is the family**, `core\adt\EAdtConstants`, so a table is placed like every other object in a download. The `TagTable Properties` sheet beside it has a `Path` of its own — `90_LIbrary\ADT\ADT` — and it is deliberately not used: that is where the table sits in one plant's tree, not where the core says it belongs, and a table placed by it would land somewhere *sync folders with core* then wants to move it out of.
- **A table already there is deleted and rebuilt.** An enumeration that has dropped a constant must drop it here too; creating over the top would leave the retired one behind with nothing saying so.
- **A constant TIA refuses is named and the rest still go in** — five named, the remainder counted. A constants table quietly three entries short is the silent hole the rest of this window exists to avoid.

### Putting a misplaced object back

**Sync folders with core** moves every object the comparison found in a folder its family does not name — the rows the left panel shows as *belongs in core/node*, and nothing else.

- **Openness has no move**, and that was checked: no `Move`, no `Cut`, no `Reparent`, no `ChangeGroup`, no `Relocate` in any of the 2,269 types. So a move is an export, a delete and an import, and **the order is forced**: an object's name is unique across a PLC's software, so the copy cannot go into its new folder while the original is still in the old one.
- **Which leaves a moment where the object is only a file, and that is what the report is built around.** A move whose import fails keeps its export, names the path, and the run leaves the scratch folder alone. An object nobody can find again is the one outcome this must not produce.
- **An object is found by name, not by the path the map recorded.** A name is unique across a PLC's software; a path starts with a tree name that follows TIA's interface language. Which tree to start in comes from the kind instead, which is the framework's own vocabulary.
- **It asks first, naming every object against its destination** up to twenty — and says what a move *is*, because "for a moment this is only a file" is worth knowing before rather than after.
- **A tag table moves this way too**, and here SimaticML is the door that works: what comes *out* of a project is SimaticML. The PLC's default table is refused by name — TIA rebuilds it, and it never came from the core.

> **Three projects for one window.** `Satellite.CoreUpdater` is a library with the window and a port; `…V20.exe` and `…V21.exe` are thin shells around it. Openness is `Siemens.Engineering` in V17–V20 and `Siemens.Engineering.Base` in V21 — different assemblies with different public key tokens — so one binary cannot serve both, the same reason the Add-In exists twice. The library references no Siemens assembly at all, which is checkable from its metadata rather than promised in prose.

> **An Openness client has to locate the assemblies at run time**, and the registry only half helps. On a station with both installed, `HKLM\SOFTWARE\Siemens\Automation\Openness\20.0\PublicAPI\` publishes `Siemens.Engineering` with its path for every API V20 serves — while **`21.0\PublicAPI\21.0.0.0` publishes `EngineeringVersion` and nothing else.** So V20 resolves by name and V21 has to derive: what V20 publishes is the *layout*, `…\Portal V20\PublicAPI\V20\`, and the same installation root with the version rewritten is where V21's are. The root comes from the machine rather than from a hardcoded `C:\Program Files`.
>
> When it still fails the window names what the loader said **and where it looked** — which keys existed, what each published, which folders were probed — and that panel scrolls, because the account is longer than the window. The two failures worth telling apart are "the assemblies are not here" and "TIA refused the connection", the second of which usually means the Windows user is not in the **Siemens TIA Openness** group.

Exercised here without TIA Portal, by running the real shared library behind a stand-in session: attached to a saved project, attached to one never saved, nothing running, several running, and the Openness assemblies missing — with the rendered window read back each time, which is what confirms the dark theme and the brand icon resolved rather than silently falling back.

The way it recognises its own TIA Portal was checked apart from TIA: the command line the Add-In writes, parsed back with **Windows' own `CommandLineToArgvW`** rather than with anything that agrees with the code that wrote it — a PLC name and a path both carrying spaces, and the measured backslash trap where a quoted argument ending in `\` swallows the one after it; two arguments from an older Add-In still parsing, and blank and absent reading alike; a project matched case-insensitively and through a relative step, and refused when it is a different file; and the ancestor walk run in a grandchild process, whose chain starts with the child that spawned it and carries on past it without repeating.

The chooser was driven on the real window with a stand-in session behind the real worker: two instances offered as rows naming their projects, **nothing preselected and *Attach* off** until one is picked, the click reaching the session with that instance's id and the failure panel giving way to the project, one instance still offered as a choice, and the two states that are not a choice at all — nothing running, and nothing having looked, which must not claim that no TIA Portal was running.

The grid was driven on the real window, shown and laid out: six rows in the order a PLC reads, an unlisted kind falling in after them, each row carrying its own languages and a UDT row carrying none, a label keeping its underscore — in `Content` it would have rendered `SomethingNew` as `SomethingNew` but `F_DB` as `FDB` — everything ticked asking for no filter at all, one language unticked narrowing *that row only* while an FC in the same language still maps, a kind unticked killing its language boxes and leaving the filter, a row ticked with no language switching *Map project* off **by name**, `All` and `None` on either column, and an empty scope taking the rows away and leaving the headings. The filter and the survey were checked apart from the window, including the two cases that would be silently wrong: a UDT surviving a language filter, asked with a real null and with an empty string, and `SCL` counted twice over — 22 under FB and 12 under FC — rather than once across the map. And the map file: a format 2 filter written, read back and still narrowing the same, with an older and a newer one each refused in its own words.

**And attached for real, in TIA Portal V20 and V21 on the VM** (2026-09-16), which is what settles the two things no test without TIA can reach: that the resolver finds the assemblies in both versions, and that attaching leaves the engineer's session alone. The second cost a wrong assumption to learn — see the note about disposing, above.

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
