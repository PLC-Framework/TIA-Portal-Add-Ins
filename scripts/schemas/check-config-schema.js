// Runs config.schema.json over the real configurations and a corpus of deliberately broken
// variants. Two assertions, and the second is the interesting one:
//
//   - every variant the schema SHOULD catch is rejected;
//   - every variant it CANNOT express is accepted - proving the documented limit rather
//     than trusting the prose. Those four are exactly what Core's validators exist for.
//
// This exists because the contract is now stated three times: the README table, Core's
// validators, and the schema. Add a type to a closed set in CodingStyleValidator.cs and the
// schema does not follow on its own - this is what turns that drift into a failing run.
//
// ajv is resolved from OUTSIDE the repo, on purpose: this is a .NET solution and a
// node_modules\ inside it would be the only one, kept alive by a single test. Install it
// wherever you keep scratch tooling and point NODE_PATH at it:
//
//     npm install --prefix %TEMP%\plcfw-tools ajv@8
//     set NODE_PATH=%TEMP%\plcfw-tools\node_modules
//     node scripts\schemas\check-config-schema.js
//
// Newtonsoft.Json.Schema would have been the in-house choice - the repo already carries
// Newtonsoft.Json - and it is commercially licensed beyond 1000 validations an hour, which
// is a poor thing to bury in a test.
const fs = require("fs");
const path = require("path");
const Ajv = require("ajv");

// Found by the solution file rather than by counting folders up from here. This script has
// already moved once - scripts\ into scripts\schemas\ - and a hardcoded "..\.." breaks
// silently the next time, resolving to a folder that simply has no config.json in it.
function repoRoot(from) {
    let dir = from;

    for (;;) {
        if (fs.existsSync(path.join(dir, "TIA-Portal-Add-Ins.slnx"))) return dir;

        const parent = path.dirname(dir);
        if (parent === dir) throw new Error("TIA-Portal-Add-Ins.slnx not found above " + from);

        dir = parent;
    }
}

const repo = repoRoot(__dirname);
const schema = JSON.parse(fs.readFileSync(
    path.join(repo, "src/Satellite.ConfigEditor/Resources/config.schema.json"), "utf8"));

const ajv = new Ajv({ allErrors: true, strict: false });
const validate = ajv.compile(schema);

const clone = (o) => JSON.parse(JSON.stringify(o));

function set(doc, dotted, value) {
    const parts = dotted.split(".");
    let node = doc;
    for (let i = 0; i < parts.length - 1; i++) {
        const key = parts[i].match(/^\d+$/) ? Number(parts[i]) : parts[i];
        node = node[key];
    }
    const last = parts[parts.length - 1];
    node[last.match(/^\d+$/) ? Number(last) : last] = value;
    return doc;
}

function drop(doc, dotted) {
    const parts = dotted.split(".");
    let node = doc;
    for (let i = 0; i < parts.length - 1; i++) {
        const key = parts[i].match(/^\d+$/) ? Number(parts[i]) : parts[i];
        node = node[key];
    }
    delete node[parts[parts.length - 1]];
    return doc;
}

// --- the real files, which must be clean -------------------------------------------------
let failures = 0;
for (const project of ["TestAddins-v20", "TestAddins-v21"]) {
    const file = path.join(repo, ".example", project, ".plc-framework/config.json");
    const doc = JSON.parse(fs.readFileSync(file, "utf8"));
    const ok = validate(doc);
    console.log(`real   ${project.padEnd(16)} ${ok ? "valid" : "INVALID"}`);
    if (!ok) {
        failures++;
        console.log(ajv.errorsText(validate.errors, { separator: "\n         " }));
    }
}

const base = JSON.parse(fs.readFileSync(
    path.join(repo, ".example/TestAddins-v21/.plc-framework/config.json"), "utf8"));

// --- variants the schema must reject -----------------------------------------------------
const mustFail = {
    "metadata missing":                 (d) => drop(d, "metadata"),
    "coreSource missing":               (d) => drop(d, "metadata.coreSource"),
    "coreSource wrong case":            (d) => set(d, "metadata.coreSource", "Local"),
    "coreSource outside the set":       (d) => set(d, "metadata.coreSource", "github"),
    "version without the v":            (d) => set(d, "metadata.version", "2.0"),
    "version with one component":       (d) => set(d, "metadata.version", "v2"),
    "version with three digits":        (d) => set(d, "metadata.version", "v100.0"),
    "remote selected, section absent":  (d) => drop(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig"),
    "local selected, section absent":   (d) => drop(set(d, "metadata.coreSource", "local"), "coreLocalRepositoryConfig"),
    "apiUrl not absolute":              (d) => set(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig.apiUrl", "api.github.com"),
    "apiUrl wrong scheme":              (d) => set(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig.apiUrl", "ftp://api.github.com/"),
    "branch empty":                     (d) => set(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig.branch", ""),
    "branch only whitespace":           (d) => set(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig.branch", "   "),
    "remote dependencyFile missing":    (d) => drop(set(d, "metadata.coreSource", "remote"), "coreRemoteRepositoryConfig.dependencyFile"),
    "local folder missing":             (d) => drop(d, "coreLocalRepositoryConfig.folder"),
    "projectConfig missing":            (d) => drop(d, "projectConfig"),
    "hierarchy missing":                (d) => drop(d, "projectConfig.hierarchy"),
    "codingStyle missing":              (d) => drop(d, "projectConfig.codingStyle"),
    "hierarchy.blocks missing":         (d) => drop(d, "projectConfig.hierarchy.blocks"),
    "hierarchy.blocks not an array":    (d) => set(d, "projectConfig.hierarchy.blocks", {}),
    "group without a name":             (d) => drop(d, "projectConfig.hierarchy.blocks.0.name"),
    "group name empty":                 (d) => set(d, "projectConfig.hierarchy.blocks.0.name", ""),
    "softwareUnits missing a list":     (d) => set(d, "projectConfig.hierarchy.softwareUnits", { blocks: [], tagTables: [] }),
    "codingStyle.rules missing":        (d) => drop(d, "projectConfig.codingStyle.rules"),
    "rule without an id":               (d) => drop(d, "projectConfig.codingStyle.rules.0.id"),
    "rule without a regex":             (d) => drop(d, "projectConfig.codingStyle.rules.0.regex"),
    "block type outside the set":       (d) => set(d, "projectConfig.codingStyle.blocks.0.type", "DB"),
    "block type wrong case":            (d) => set(d, "projectConfig.codingStyle.blocks.0.type", "ob"),
    "type from another section":        (d) => set(d, "projectConfig.codingStyle.technologyObjects.0.type", "OB"),
    "implements empty":                 (d) => set(d, "projectConfig.codingStyle.blocks.0.implements", []),
    "implements missing":               (d) => drop(d, "projectConfig.codingStyle.blocks.0.implements"),
    "implements a string, not a list":  (d) => set(d, "projectConfig.codingStyle.blocks.0.implements", "naming"),
    "implements entry blank":           (d) => set(d, "projectConfig.codingStyle.blocks.0.implements", ["  "])
};

// --- variants it cannot express, and Core's validators own --------------------------------
const mustPass = {
    "implements names a rule that does not exist":
        (d) => set(d, "projectConfig.codingStyle.blocks.0.implements", ["no_such_rule"]),
    "two rules share an id":
        (d) => set(d, "projectConfig.codingStyle.rules.1.id", d.projectConfig.codingStyle.rules[0].id),
    "two sibling folders share a name":
        (d) => set(d, "projectConfig.hierarchy.blocks.1.name", d.projectConfig.hierarchy.blocks[0].name),
    "a regex that does not compile":
        (d) => set(d, "projectConfig.codingStyle.rules.0.regex", "[")
};

console.log("\n--- must be rejected ---");
for (const [name, mutate] of Object.entries(mustFail)) {
    const ok = validate(mutate(clone(base)));
    const where = ok ? "" : (validate.errors[0].instancePath || "/");
    console.log(`  ${ok ? "MISSED " : "caught "} ${name.padEnd(34)} ${where}`);
    if (ok) failures++;
}

console.log("\n--- beyond JSON Schema: must pass here, Core catches them ---");
for (const [name, mutate] of Object.entries(mustPass)) {
    const ok = validate(mutate(clone(base)));
    console.log(`  ${ok ? "passes " : "UNEXPECTEDLY REJECTED"} ${name}`);
    if (!ok) failures++;
}

console.log(`\n${failures === 0 ? "OK - every expectation held" : failures + " EXPECTATION(S) BROKEN"}`);
process.exit(failures === 0 ? 0 : 1);
