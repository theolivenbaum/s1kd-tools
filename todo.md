# todo.md — s1kd-tools C# port progress

Tracks the port of each component from C (`reference/`) to C# (`src/`).
Tools are ordered roughly by ascending complexity (C LOC in parentheses) so the
shared foundation and easy wins come first.

Legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked/needs decision

## 0. Project setup
- [x] Move C source into `reference/`
- [x] Add Visual Studio `.gitignore`
- [x] Write `CLAUDE.md`
- [x] Write `todo.md`
- [x] Scaffold solution: `S1kdTools.Core`, `S1kdTools.Cli`, `S1kdTools.Tests`

## 1. Foundation — `S1kdTools.Core` (port of `tools/common/s1kd_tools.c`)
- [x] `XmlUtils`: ReadDoc/SaveDoc, XPathFirstNode/Value, XPathOf
- [x] `Csdb`: object-type detection (is_dm/pm/com/imf/ddn/dml/icn/smc/upf)
- [x] `Csdb`: `IsInRange` / `IsInSet`, wildcard `StrMatch`
- [x] `Csdb`: latest-issue extraction, basename compare, config discovery
- [x] `Applicability`: is_applic / eval_assert / eval_evaluate / eval_applic
- [x] `Applicability`: same_annotation (C14N), rem_delete_elems
- [ ] CCT dependency injection (`add_cct_depends`) — used by aspp/instance
- [ ] ICN entity / notation helpers (`add_icn`, `add_notation`) — used by addicn
- [ ] XSLT extension shim for EXSLT (as needed by stylesheet tools)

## 2. libs1kd public API (port of `tools/libs1kd/include/s1kd/*.h`)
- [~] `Metadata` — get/set (curated key table; grow to full set)
- [ ] `Instance` — `Filter(doc, applicability, mode)` (depends on s1kd-instance)
- [ ] `BrexCheck` — `CheckBrex` / `CheckDefaultBrex` (depends on s1kd-brexcheck)

## 3. Tools (port of `tools/s1kd-*`)
Generation / `new*` family (share template + .defaults plumbing):
- [x] s1kd-defaults (777) — text<->XML for .defaults/.dmtypes/.fmtypes, default
      generation, sort, BREX-driven generation (DOM, no XSLT). TODO: -i init for
      the non-BREX case shells out to newdm/fmgen (wire up once those exist).
- [ ] s1kd-newdm (2110) — flagship "new" tool; establishes template engine
- [ ] s1kd-newpm (1002)
- [ ] s1kd-newcom (906)
- [ ] s1kd-newddn (797)
- [ ] s1kd-newdml (1171)
- [ ] s1kd-newimf (663)
- [ ] s1kd-newsmc (965)
- [ ] s1kd-newupf (852)
- [ ] s1kd-dmrl (274) — drive new* from a DMRL

Authoring:
- [x] s1kd-addicn (111) — ICN entity/notation declarations. NOTE: XmlDocument
      can't add NOTATION/ENTITY via DOM; serialized DTD manually.
- [~] s1kd-ls (1050) — type selection, official/inwork, latest/old, recursive,
      list input, writable/read-only, null output. TODO: -e/--exec, -N file
      inwork lookup.
- [~] s1kd-metadata (3240) — list/edit metadata (big key table)
- [x] s1kd-mvref (768) — recode dmRef/pmRef from a source object to a target.
- [ ] s1kd-ref (2040)
- [ ] s1kd-sns (468)
- [x] s1kd-upissue (1016) — inwork/official workflow, RFU/change marks, QA
      reset, file renaming, issue 3.0 vs 4.x switching. TODO: bundled short
      flags (-ife), libxml2 parser long-opts.

Validation:
- [ ] s1kd-validate (633) — XSD validation + IDREF checks + XML report
- [ ] s1kd-brexcheck (9147) — largest; business-rule checking
- [ ] s1kd-refs (2794) — dependency listing
- [ ] s1kd-repcheck (965) — CIR reference validation
- [ ] s1kd-appcheck (2840) — applicability validation

Publication:
- [ ] s1kd-acronyms (1020)
- [ ] s1kd-aspp (929) — applicability preprocessing
- [ ] s1kd-flatten (765)
- [ ] s1kd-fmgen (1021)
- [ ] s1kd-icncatalog (577)
- [ ] s1kd-index (400)
- [ ] s1kd-instance (5126) — applicability/CIR filtering (core algorithm)
- [ ] s1kd-neutralize (292) — easy; IETP neutral metadata (XSLT)
- [x] s1kd-syncrefs (504) — rebuild the References table (refs) from references
      in a data module (DOM, no XSLT).
- [ ] s1kd-uom (599) — unit-of-measure conversion

## 4. Cross-cutting
- [ ] Common option handling: `--version`, `-h/--help`, libxml2 parse opts
      (`--huge`, `--net`, `--noent`, `--xinclude`, `--xml-catalog`)
- [ ] Embedded resources: wire each tool's `*.xsl`/templates/data as resources
- [ ] Multi-call dispatch (`s1kd-<tool>` argv[0] routing) in CLI
- [ ] Packaging: `dotnet pack` for the library; single-file publish for the CLI
- [ ] Port/port-equivalent of `libs1kd` tests under `reference/.../tests`

## Known risks / decisions
- EXSLT coverage in `XslCompiledTransform` (str:/exsl:/dyn:) — shim per stylesheet.
- libxml2 IDREF/IDREFS handling: validate tool reimplements it; replicate the
  hardcoded attribute list from `s1kd-validate.c`.
- Output serialization must byte-match the C tools where tests compare files;
  otherwise compare via canonical XML (C14N).
- libxml2 XML-catalog support has no direct BCL equivalent; implement a minimal
  `XmlResolver` if/when `--xml-catalog` is exercised.
