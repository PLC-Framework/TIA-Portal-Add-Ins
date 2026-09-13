# Configuring a project

`config.json` lives in `.plc-framework\` inside a TIA project and is what the Add-In reads. This page is the authority on what it must contain, how it is validated, and how secrets stay out of it.

## The `config.json` contract

Settled 2026-09-08, ahead of building the validator and `Satellite.ConfigEditor`. This is the authority on what the file must contain; the model under `Core/Config/Model/` is deliberately **permissive** and enforces none of it, because a serializer that throws stops at the first problem and loses the rest.

Two kinds of check, kept apart because only one of them touches the disk:

|  |  |
| --- | --- |
| **Structural** | required fields, closed value sets, internal references, uniqueness, regex that compile. Pure, and safe to run inside TIA Portal |
| **Environmental** | a path exists, a `${VAR}` resolves, a repository is reachable. Touches disk and network, and belongs in its own pass |

"Required" is scoped **per concern**, not per document: an action reads only its own section and validates only that, while the editor runs the whole set before saving.

### `metadata`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `metadata` | **Yes** | object | Holds `coreSource`, which decides the rest of the file |
| `metadata.coreSource` | No | string or null | Closed set: `local` \| `remote` \| `null`. Selects which repository section is required; `null`, or no key at all, means **no repository**, and neither section is required or validated |
| `metadata.version` | No | string | `v<n>.<n>` with two to four components of up to two digits — `v2.0`, `v1.12.3`, `v9.9.9.9` |
| `metadata.author` | No | string | Informative. Not validated |
| `metadata.description` | No | string | Informative. Not validated |

**A repository is optional, and that was a correction** (2026-09-13). `coreSource` was required and `local` or `remote`, so every project had to name a core — and fill in a path nobody read — although only actions that read a core need one, and none exists yet. `null` is the answer for a project that uses none. **Absent and `null` mean the same**: the serializer hands both to `Core` as null, so `Core` cannot tell them apart, and a schema that demanded the key would disagree with a validator that could not. `Satellite.ConfigEditor` writes the explicit `null` anyway, so a reader sees a decision rather than an omission, and the template ships with it. An empty string is **not** null — it is a value outside the set, and reading it as "no repository" would turn a half-typed word into a decision.

### `coreRemoteRepositoryConfig` — required only when `coreSource = remote`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `apiUrl` | **Yes** | string | GitHub API endpoint. Must be an absolute URI |
| `owner` | **Yes** | string | Repository owner |
| `repository` | **Yes** | string | Repository name |
| `branch` | **Yes** | string | Branch. The template ships `main`; empty is an error |
| `folder` | **Yes** | string | Path inside the repository down to the core |
| `dependencyFile` | **Yes** | string | Dependency graph file name, today `core.json` |
| `token` | No | string | **Always the literal `${GITHUB_TOKEN}`** — see below. Absent or empty means a public repository |

### `coreLocalRepositoryConfig` — required only when `coreSource = local`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `repository` | **Yes** | string | Local path. That it exists on disk is*environmental*, checked separately |
| `folder` | **Yes** | string | Path inside the repository down to the core |
| `dependencyFile` | **Yes** | string | Dependency graph file name |

### `projectConfig`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `projectConfig` | **Yes** | object | What the Add-In applies to the TIA project |
| `projectConfig.hierarchy` | **Yes** | object | The folder tree to create |
| `projectConfig.codingStyle` | **Yes** | object | Naming rules, and which objects they apply to |

### `projectConfig.hierarchy`

Every list is **required but may be empty**. `[]` says "this concern has no folders"; a missing key says nothing at all, and the difference is worth keeping.

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `blocks` | **Yes** | array of `Group` | Folders under Program blocks. May be `[]` |
| `technologyObjects` | **Yes** | array of `Group` | Folders under Technology objects. May be `[]` |
| `tagTables` | **Yes** | array of `Group` | Folders under PLC tags. May be `[]` |
| `types` | **Yes** | array of `Group` | Folders under PLC data types. May be `[]` |
| `softwareUnits` | No | object | Applied inside**every** software unit. Only the S7-1500 has them; on an S7-1200 the section is ignored |
| `softwareUnits.blocks` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |
| `softwareUnits.tagTables` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |
| `softwareUnits.types` | **Yes**, if `softwareUnits` is present | array of `Group` | May be `[]` |

There is deliberately **no `technologyObjects` under `softwareUnits`**: a software unit exposes blocks, tag tables and types only. Nothing in this section creates units — they are named by the user after the plant's architecture, so the config describes only what goes *inside* one.

### `Group` — recursive

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `name` | **Yes** | string | Folder name. Not empty.**Unique among siblings of the same parent**; the same name in a different branch is legal |
| `groups` | No | array of `Group` | Sub-folders. Same rules, all the way down |

### `projectConfig.codingStyle`

Same rule as the hierarchy: required, possibly empty.

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `objectRules` | **Yes** | array of `Rule` | Rules for the names of TIA objects. This is what every `implements` points at. May be `[]` |
| `interfaceRules` | No | array of `Rule` | Rules for the names of what lives inside an object: interface members, tags, constants. Referenced only from an object rule's `interface` |
| `rules` | — | array of `Rule` | **The former name of `objectRules`**, still read. See below |
| `blocks` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `OB`, `ArrayDB`, `GlobalDB`, `InstanceDB`, `FC`, `FB` |
| `technologyObjects` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `TechnologicalInstanceDB` |
| `tagTables` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `PlcTagTable` |
| `types` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `PlcStruct` |
| `alarmTextLists` | **Yes** | array of `PlcTypeObject` | Closed set of `type`: `AlarmTexts` |

**The catalogue is split in two because the two kinds of rule are referenced from different places and can never stand in for one another.** An object rule names a TIA object and is reached from a type's `implements`; an interface rule names what lives inside one and is reached only from an object rule's `interface`. Ids are unique across both anyway, so that an id in a report identifies exactly one rule.

**`rules` is the name `objectRules` used to have, and reading it is not politeness — it is the first migration this format has needed.** A `config.json` already sitting in a TIA project carries the old key, and a released Add-In has to keep working with it: a document with `rules` alone is valid, and `Satellite.ConfigEditor` renames the key in place the first time it opens the file, so it migrates on the next save without the user learning that anything was renamed. A document carrying **both** keys is an error rather than a guess — which of the two the author meant is not something to decide silently.

`interfaceRules` is optional for the same reason: a configuration written before the split has none, and reporting it as broken on a machine that merely opened it would be wrong.

### `Rule`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `id` | **Yes** | string | **Unique across both catalogues**, so an id in a report or an error message names exactly one rule |
| `regex` | **Yes** | string | Naming pattern.**Must compile** — checked structurally, since a pattern that cannot compile is a broken file, not a broken environment |
| `descriptions` | No | array of string | Human-readable explanation of the rule |
| `interface` | No | array of `InterfaceSection` | **Object rules only.** What this rule expects to find inside the object it names. Absent means the interface is not checked; on an interface rule it is an error |

**The `interface` hangs off the rule, not off the type, and that is the whole design.** An FB whose name matches `object_container_for_sequences` must hold sequence variables in its `Static`; an FB whose name matches `function` must not. Both are FBs, so a list hanging off the type could never tell them apart — it is *the rule that matched* which says what belongs inside.

That only works while a name matches one rule. Where two could match, the checker takes the union of their interfaces, but the better fix is a pattern that excludes the other: `generic_object_container` carries `(?!seq[0-9])` and `function` carries `(?!_oc_)` for exactly that reason.

### `PlcTypeObject`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `type` | **Yes** | string | TIA object type. Closed set, listed per section above |
| `implements` | **Yes** | array of string | Object rules this type accepts. Not empty, and**every id must exist in `objectRules`** — an internal reference, so a typo is caught rather than silently ignored |

**A name passes if it matches *any* rule in `implements`, not all of them.** An `FC` that implements `function`, `safety_function`, `subroutine` and `safety_subroutine` is offering four spellings and accepting whichever one was used — a name could not satisfy two of them at once. What the report shows is which rules a name matched, and the ones it did not are shown as suggestions, because they are what it was probably aiming at.

### `InterfaceSection`

| Field | Required | Type | Description |
| --- | --- | --- | --- |
| `type` | **Yes** | string | Closed set: `Input`, `Output`, `InOut`, `Static`, `Temp`, `Constant` for a block interface, and `Tag`, `UserConstant` for a tag table's two. Spelled as TIA spells it, like every other closed set here. **Unique within one `interface`** — the same section twice says nothing the first entry does not |
| `implements` | **Yes** | array of string | Not empty, and every id must exist in **`interfaceRules`**. The two catalogues never stand in for one another, so naming an object rule here reads as "no such rule", which from here it is |

**A tag table's tags and its user constants are named apart on purpose.** They are two different things that answer to different rules — an enumeration's constants are shouted in upper case while its tags are not — and one section covering both could not say so.

### The token never lands in `config.json`

`config.json` lives inside the TIA project, and TIA projects are under version control. So the file **always** carries the literal `${GITHUB_TOKEN}` and never the secret itself. The editor shows the field masked with a show/hide eye — the same twin-control pattern as the PLC password — and what the operator types goes to the `.env`, never to the JSON. Reading the field means reading the `.env` back.

**The `.env` lives at `%LOCALAPPDATA%\PLC-Framework\.env`, per user.** A GitHub token is personal, not a station-wide credential, and the install folder is not writable by the engineer who owns the token — so an editor writing there would fail on a real workstation while working perfectly on a developer's own machine. Per user it is writable by that person, and two engineers sharing a station stop overwriting each other.

> This supersedes an earlier placement inside the install folder, described under *Where the satellites live*. `InstallPaths.EnvFile` hangs off `UserRoot`.

The same reasoning caught a second file. `config.template.json` was first placed beside the executables, with the editor writing the embedded copy out there when it was missing — which would have failed on any station where the engineer is not an administrator. It moves to `%LOCALAPPDATA%\PLC-Framework\` too, and the general rule is written down under [*The five locations, at a glance*](architecture.md): **`Program Files` is what the installer puts there, `%LOCALAPPDATA%` is what the applications write.**

## Validating a configuration

Built 2026-09-09, in `Core/Config/Validation/`. The contract above says what is required; this says how it is checked.

**There are two entry points and choosing the wrong one is a design error, not a preference:**

```csharp
ConfigValidator.Validate(config);            // the whole document — the editor, before saving
HierarchyValidator.Validate(hierarchy);      // one concern — an action, before acting
```

An action reads one section and must refuse to run only over problems *in that section*. `CreateProjectHierarchyAction` calling the composite would let a broken `codingStyle` stop a folder tree that is perfectly fine, and "required" would quietly become a property of the document rather than of the concern that needs it.

Every per-concern validator takes an optional path prefix, which is how the composite makes its issues read `projectConfig.hierarchy.blocks[0].name` while the same validator called alone says `blocks[0].name`.

**The structural pass never touches disk.** It is handed an object and returns issues, which is what makes it safe inside TIA Portal and what let every behaviour below be checked against invented JSON without writing a file.

```csharp
public sealed class ValidationIssue { string Path; string Message; }   // "metadata.coreSource: Required."
public sealed class ValidationResult { IReadOnlyList<ValidationIssue> Issues; bool IsValid; }
```

`Path` is a position **inside the document**, not on disk — that is what will let the editor put the cursor on the offending field instead of showing a message about a file.

Three decisions worth keeping:

- **A depth guard, at 32 levels.** Not a style rule: this runs inside TIA Portal's process and a `StackOverflowException` **cannot be caught** in .NET — it takes the host down. A hand-written folder tree is three or four deep. Checked with 200: it reports and lives.
- **A literal token in `config.json` is not a validation error.** The file works with it; it is a secret in the wrong place, which is a warning for the editor to give, not a reason for the Add-In to refuse a configuration.
- **The composite does not demand a repository section when `coreSource` is unusable.** That is already reported once, and a second complaint about the same mistake trains the reader to skim the report.

### The JSON Schema: the only validator that runs while you type

`config.schema.json`, built 2026-09-11. **Three statements of one contract now exist** — the table above, `Core`'s validators, and this — and that is a deliberate cost with a specific payoff: the other two run before a save and after a load, while this one runs on every keystroke, in whatever editor opened the file. It is the first safety net the hand-edit path has ever had, and the only one that offers autocomplete over the closed sets.

**It expresses the mechanical half.** Required fields, types, the five closed `type` sets, `coreSource` deciding which repository section is required (`if`/`then`) — and validated: a section nobody selected is kept and ignored here as it is in `Core`, since checking a half-written one only in the schema would underline a file `Core` loads without a word — the recursive `Group` through a `$ref` to itself, the `version` pattern, an `apiUrl` that is absolute and `http`/`https`, and lists that may be empty but must exist.

**Six rules are beyond it, and they are the ones a typo breaks silently:**

| Not expressible | Why |
| --- | --- |
| `implements` naming an existing rule, in the catalogue it is allowed to reach | JSON Schema has no cross-references |
| a rule id unique across **both** catalogues | `uniqueItems` compares whole objects, not one field, and cannot look at two arrays at once |
| `Group.name` unique among siblings | same |
| an interface section listed twice for one rule | same |
| carrying both `objectRules` and `rules` | the schema has to accept either, so it cannot object to both |
| a `regex` that compiles | `format: "regex"` is annotation-only in most validators |

So **the schema does not replace `Core`'s validators; it takes the boring half earlier.** Anyone who mistakes it for the authority will ship a file the Add-In then refuses.

Two details that are easy to get wrong:

- **`additionalProperties` stays open.** The editor deliberately preserves keys the model has never heard of, and a schema flagging them would contradict that in the same breath.
- **Blank is not "present".** `Core`'s `Required` treats whitespace as missing, so every required string carries `"pattern": "\\S"` rather than `minLength: 1` — otherwise `"   "` passes here and fails there, which is the worst possible disagreement between two validators.

Draft-07 rather than 2020-12: nothing here needs the newer draft, and Draft-07 is what editors support most completely.

#### Where it lives, and why in the project

```jsonc
{ "$schema": "./config.schema.json", "metadata": { … } }
```

`Satellite.ConfigEditor` embeds it, writes a copy next to `config.json`, and adds that relative `$schema` as the **first** key. The alternatives each fail in a way that only shows later: a URL needs the network and answers 404 on a private repository, which stops validation with no message at all; an absolute path into the install folder gets committed and is wrong on the next station; an editor setting is per machine, so whoever clones the project inherits nothing. A copy per project is the price of it working for a stranger who opens the file. Nothing secret is in a schema, so `.plc-framework\` being under version control is fine here — that rule is about credentials.

**It is rewritten on every save**, because it is generated rather than authored: a project open across a framework upgrade would otherwise keep validating against last month's contract. Hand edits to it are therefore lost — the right trade for a derived file, and the exact opposite of `config.template.json`, where customising is the whole point. **A `$schema` pointing somewhere else is left alone** and no file is written: aiming at a shared copy on a network drive is a deliberate act, and overwriting it every save would be the editor arguing with its user.

**A schema that cannot be written never fails a save.** It costs autocomplete, not the configuration, so it is reported beside "Saved to …" and stepped over.

#### Keeping the three in step

`scripts\schemas\check-config-schema.js` is what stops the drift a third statement invites. Add a type to a closed set in `CodingStyleValidator.cs` and the schema does not follow on its own; this turns that into a failing run.

```
npm install --prefix %TEMP%\plcfw-tools ajv@8
set NODE_PATH=%TEMP%\plcfw-tools\node_modules
node scripts\schemas\check-config-schema.js
```

**`ajv` is resolved from outside the repo deliberately** — this is a .NET solution, and a `node_modules\` inside it would be the only one, kept alive by a single test. `Newtonsoft.Json.Schema` would have been the in-house choice, since `Newtonsoft.Json` is already here, and it is commercially licensed beyond 1000 validations an hour: a poor thing to bury in a test.

It checks both real configurations, forty-two broken variants — every one rejected, at the right path — and sixteen documents the schema must accept, **which must pass**: the test asserts the limits rather than trusting this prose. The wiring was then exercised end to end through the real `ConfigDocument`: the schema lands beside the file, `$schema` is first, `Core` still loads and validates a document carrying a key its model does not know, a second save neither duplicates nor moves it, a `$schema` aimed elsewhere survives untouched with no file written, and the file the editor wrote validates against the copy it wrote next to it.

### The environmental pass is separate, and that is the point

`EnvironmentValidator.Validate(config, lookup)` checks what depends on the machine: that a local repository path exists, that the folder and the dependency file under it exist, that every `${VAR}` resolves.

**A configuration is not wrong because a drive is not mapped here.** It is unusable *here, now* — a different sentence, and often a temporary one. So this pass runs when somebody asks rather than on every load, and it is the reason the two never share a method.

It deliberately does **not** reach the network. Whether GitHub answers is a question with a timeout attached, and a validator that can hang for thirty seconds is one nobody runs.

## Secrets: the `.env` and `${VAR}`

`Core/Secrets/` holds both halves, and the split matters: `Variables` is **pure** — it is handed a lookup and never opens a file — while `DotEnv` is the one that knows where the file is. That is what lets the same expansion run inside TIA, inside a satellite, and inside a test with three values in a dictionary.

```csharp
DotEnv.Get("GITHUB_TOKEN");                       // the .env, then the process environment
DotEnv.Set("GITHUB_TOKEN", "ghp_…");              // null on success, or a sentence
Variables.Expand("Bearer ${GITHUB_TOKEN}", lookup);
```

- **The file is written surgically.** `Set` replaces the one line that defines the name and leaves everything else — comments, order, unrelated entries, spacing — exactly as it was. A rewrite from a dictionary would silently eat the comments explaining what each secret is for. Same principle as the config editor's own writes.
- **An unresolved `${VAR}` is left standing, not blanked.** An empty string travels on and fails far away as an unexplained HTTP 401; a literal `${GITHUB_TOKEN}` arriving where a token was expected says exactly what went wrong. Reporting it is the environmental validator's job, and its message names the `.env` to add it to.
- **Empty counts as unresolved**, everywhere. `DotEnv.Get` returns null for a key present with no value, the validator reports that case as missing, and `Expand` leaves the reference alone — three places that would otherwise disagree about the same fact.
- **The process environment sits behind the file**, which is what lets a build server or a test override a secret without editing anything. That read is wrapped: it is denied outright under partial trust.
- **Nothing here throws.** A missing file, an unreadable one, a line that makes no sense: all mean "that secret is not available", which callers already handle.

The parser is forgiving because the file is edited by hand — `export` prefixes, quotes, blanks around the `=`, `#` comments, and a value containing `=` all behave as a reader would expect.
