# S7 export files

What TIA Portal writes when a block, a PLC data type or a tag table leaves the project. Everything here was **read off real exports** — the ones under `.example\vci\` (V21) and `.example\vci-v20\` (V20) — rather than taken from documentation, which is the rule for this folder.

It matters because the object model does not show a block's interface in a usable shape: `Member` carries a `Name` and nothing else, so a member inside a `Struct` arrives as `variableA.variableB` and nothing says which part is the parent's. The export does show it, which is why the coding-style check reads interfaces from here.

## Where these files came from

TIA's **Version Control Interface** wrote them: a VCI workspace exports the project into a folder, and `.vci\workspace.vci.exportFormats.config` inside it decides the extension per kind of object.

| Object kind | Extension VCI writes |
| --- | --- |
| Code blocks — LAD, FBD, STL, GRAPH | `.xml` |
| Code blocks — SCL | **`.scl`** |
| Data blocks, safety data blocks | `.xml` |
| PLC data types, F-compliant PLC data types | `.xml` |
| PLC tag tables | `.xml` |
| Technology objects | `.xml` |
| HMI Unified global scripts | `.yml`, `.js` |

**That table belongs to VCI, not to Openness.** `PlcBlock.Export(FileInfo, ExportOptions)` — what an Add-In calls — writes SimaticML; whether a block written in SCL also comes out as SimaticML through that call is **not measured here**, and it is the one thing to check on the VM before relying on it.

## The document

```xml
<?xml version="1.0" encoding="utf-8"?>
<Document>
  <Engineering version="V21" />
  <SW.Blocks.GlobalDB ID="0">
    <AttributeList> … <Interface> … </AttributeList>
    <ObjectList> … </ObjectList>
  </SW.Blocks.GlobalDB>
</Document>
```

**One object per file**, under `<Document>`, always preceded by `<Engineering version="V20" />` or `V21`. The element names the type:

| Element | Object |
| --- | --- |
| `SW.Blocks.OB` · `SW.Blocks.FC` · `SW.Blocks.FB` | code blocks, safety ones included |
| `SW.Blocks.GlobalDB` · `SW.Blocks.InstanceDB` · `SW.Blocks.ArrayDB` | data blocks |
| `SW.Types.PlcStruct` | a PLC data type |
| `SW.Tags.PlcTagTable` | a tag table |
| `SW.TechnologicalObjects.TechnologicalInstanceDB` | a technology object |

`AttributeList` holds `Name`, `Namespace`, `Number`, `ProgrammingLanguage`, `MemoryLayout` and the `Interface`; `ObjectList` holds the block's comment and title as `MultilingualText`, and for a code block its **`SW.Blocks.CompileUnit` parts, which are the code**. A reader after names never has to go near them.

`ProgrammingLanguage` seen in these files: `LAD`, `FBD`, `SCL`, `STL`, `GRAPH`, `DB`, `F_FBD`, `F_DB`, `Motion_DB`. The safety ones differ from the rest **only** in that word.

## The interface

```xml
<Interface>
  <Sections xmlns="http://www.siemens.com/automation/Openness/SW/Interface/v5">
    <Section Name="Static">
      <Member Name="id" Datatype="String" />
      <Member Name="mystruct" Datatype="Struct">
        <Member Name="myarray" Datatype="Array[0..2] of Int" />
        <Member Name="myarray2" Datatype="Array[0..2] of Struct">
          <Member Name="test" Datatype="Bool" />
        </Member>
      </Member>
      <Member Name="myudt" Datatype="&quot;node&quot;" />
    </Section>
  </Sections>
</Interface>
```

**V20 and V21 use the same namespace**, `…/SW/Interface/v5`. Nothing in a reader needs to branch on the TIA version — though matching on local names rather than on the namespace is what keeps that true if `v6` ever appears.

Section names found: `Input`, `Output`, `InOut`, `Static`, `Temp`, `Constant`, `Return`, `None`, `Base`. Which appear depends on the object:

| Object | Sections |
| --- | --- |
| `OB` | `Input` (what TIA puts there), `Temp`, `Constant` |
| `FC` | `Input`, `Output`, `InOut`, `Temp`, `Constant`, **`Return`** — whose one member is `Ret_Val`, often `Void` |
| `FB` | `Input`, `Output`, `InOut`, `Static`, `Temp`, `Constant` |
| `GlobalDB` | `Static` |
| `InstanceDB` | `Input`, `Output`, `InOut`, `Static` — the FB's interface, copied |
| `ArrayDB` | `None`, holding one member named after the DB itself |
| `PlcStruct` (a UDT) | **`None`** — a UDT has no sections in the editor, and this is how that is spelled |
| a GRAPH `FB` or its instance DB | the six above **plus `Base`** |

**An empty section is written as a closed element** — `<Section Name="Temp" />` — not omitted.

### A member

| Attribute | |
| --- | --- |
| `Name` | the only one a naming rule reads |
| `Datatype` | `Bool`, `String[24]`, `Array[0..2] of Int`, `Struct`, `Array[0..2] of Struct`, `"node"` for a UDT (XML-escaped as `&quot;node&quot;`), `SinaPos` or `G7_StepPlus_V6` for a library or system type |
| `Version` | present when the datatype is a library or system type: `SinaPos` v3.0, `TON_TIME` v1.0, `G7_StepPlus_V6` v1.0, `TO_Struct_Actor` v9.0 |
| `Informative` | `true` on members TIA supplies — an OB's `Initial_Call` and `Remanence` |

Its children are `Member`, `Sections`, `AttributeList`, `Comment`, `StartValue` and `Subelement`, so **only the child `Member` elements are members**; the rest are the member's own metadata, start value or array element values.

### What is nested, and what belongs to another type

This is the distinction a reader has to get right, and the file states it structurally:

- **A member of type `Struct`, or of `Array[…] of Struct`, carries its members as direct `<Member>` children.** Those names were typed in this block and belong to it.
- **A member of a UDT, a library FB or a system type carries a nested `<Sections>` instead**, holding that type's own interface. Those names belong to the type and are checked there — once, wherever that type is defined, rather than in every block that instantiates it.

Measured on `_oc_seq2_picking.xml`: `MOP`, `SQ_FLAGS` and `OFFSETS` are **not** members of the block. They sit at `Section[Static] > Member[RT_DATA]{G7_RTDataPlus_V6} > Sections > Section[None]`, inside the GRAPH runtime type.

### GRAPH

A GRAPH block is an ordinary `FB` with two additions.

**A `Base` section** holding a nested `<Sections Datatype="GRAPH_BASE" Version="1.0">` with its own `Input`, `Output`, `InOut` and `Static` — the same shape as any typed member, so the same rule covers it. In both sequence blocks here those four are empty.

**TIA's own parameters live in the ordinary sections, beside the engineer's**, which is what makes a GRAPH block awkward to check:

| Section | TIA's | the engineer's |
| --- | --- | --- |
| `Input` | `INIT_SQ`, `ACK_EF` | `endOfRestart`, `cmd0_toStart`, … |
| `Output` | `S_NO`, `S_MORE`, `S_ACTIVE` | `restarting`, `wp0_toStart`, … |
| `Static` | `RT_DATA` | `isReady`, `inHome`, **and every step and transition** |

The steps and transitions are the subtle part: `S001_Init` is `G7_StepPlus_V6` and `t_001_002` is `G7_TransitionPlus_V6`, both **written by TIA but named by the engineer** in the GRAPH editor — which is exactly what a naming rule wants to check.

**`Informative` does not separate the two.** It sits on the `<Member>` for an OB's parameters, but in a GRAPH block it appears on the `<Comment>` and `<StartValue>` of TIA's parameters *and* of every step and transition, whose text comes from the type. Filtering on it would drop the names most worth checking. See [`configuration.md`](../configuration.md) for what is done instead: the names TIA writes are checked like any other, and the configuration carries a rule that accepts them.

## A tag table

```xml
<SW.Tags.PlcTagTable ID="0">
  <AttributeList><Name>EHmiCommand</Name></AttributeList>
  <ObjectList>
    <SW.Tags.PlcUserConstant ID="1" CompositionName="UserConstants">
      <AttributeList>
        <DataTypeName>UInt</DataTypeName>
        <Name>HMI_CMD_00000_NIL</Name>
        <Value>0</Value>
      </AttributeList>
```

No `Interface` and no `Sections`: tags and constants are objects in the `ObjectList`, each with its own `Name`. The coding-style check does not export tag tables at all — the object model gives `Tags` and `UserConstants` directly, with no nesting to lose.

## An SCL source

A block VCI exports as `.scl` is a source file, not a document to parse for structure:

```
FUNCTION "_rpmToRevPerSecond" : LReal
TITLE = {"dependencies":[]}
{ S7_Optimized_Access := 'TRUE' }
AUTHOR : 'core/converter'
FAMILY : cyanezf
NAME : _rpmToRevPerSecond
VERSION : 1.0
   VAR_INPUT
      rpm : LReal;   // Rpm
   END_VAR

BEGIN
	#_rpmToRevPerSecond := #rpm / 60.0;
END_FUNCTION
```

The interface is there — `VAR_INPUT … END_VAR` — but as text with a grammar of its own, where the XML gives a tree. Anything that needs interfaces should ask Openness for the SimaticML rather than parse this.

## The files this was read from

| File | What it shows |
| --- | --- |
| `vci\TEST_DATA.xml` | a global DB with a `Struct`, an `Array of Struct`, an array of UDT and a UDT member |
| `vci\node.xml`, `vci\destToSourceNode.xml` | a UDT, section `None` |
| `vci\TEST_ARRAY_DB.xml` | an array DB |
| `vci\MAIN.xml` | an OB, with `Informative` on the two members TIA writes |
| `vci\_oc_blabla.xml`, `vci\OC_DATA.xml` | an FB with library instances (`SinaPos`), and its instance DB |
| `vci\F_RTG1_control.xml`, `vci\F_RTG1.xml` | a safety FB and its instance DB |
| `vci-v20\Controllers.xml` | an FC, with the `Return` section |
| `vci-v20\_oc_seq2_picking.xml`, `_oc_seq4_dropping.xml` | GRAPH FBs: `Base`, TIA's parameters, steps and transitions |
| `vci-v20\SEQ2.xml`, `SEQ4.xml` | their instance DBs |
| `vci-v20\EHmiCommand.xml` | a tag table |
| `vci-v20\G02_OC020_PT_120A1.xml`, `vci\PositioningAxis_1\PositioningAxis_1.xml` | technology objects |
| `vci-v20\*.scl` | blocks VCI exported as SCL sources |

**These are the maintainer's own project files**, kept in the repo because a reader for this format is worth no more than the real files it was tested against. They carry no Siemens code — only a project's names and structure.
