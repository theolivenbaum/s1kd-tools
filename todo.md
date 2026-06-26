# todo.md — s1kd-tools C# port progress

Tracks the port of each component from C (`reference/`) to C# (`src/`).
Tools are ordered roughly by ascending complexity (C LOC in parentheses) so the
shared foundation and easy wins come first.

Legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked/needs decision

## Status (all 32 tools ported)

All 32 `s1kd-*` tools are ported, registered (reflection-based), and exercised by
the test suite (**632 xUnit tests passing**, clean build, 0 warnings). The CLI dispatches
them as `s1kd <tool>` with multi-call (`s1kd-<tool>`) support. Two tools expose
libs1kd-style library APIs (`Instance`, `BrexCheck`); `Metadata` is a library
too.

A depth wave (parallelized) since landed: brexcheck severity-level config (brsl);
instance containers/alts/auto-naming/update-instances; ls -e/-N; refs hotspot/exec;
validate -T stats; repcheck -X; uom -p/-P preformatting; upissue bundled short
flags + libxml2 long-opts; metadata option/exit-code completion; the shared
`add_cct_depends` CCT dependency-injection helper, wired into appcheck (-~) and
aspp -x custom XSLT.

Remaining work is narrow depth — per-tool partial features (each noted inline
below): interactive prompts (no-op in-process), source line numbers in reports
(BCL `XmlDocument` has no `IXmlLineInfo`), the few EXSLT-dependent stylesheet
paths, and byte-exact output parity. See the per-tool notes and the
"Known risks / decisions" section.

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
- [x] CCT dependency injection (`add_cct_depends`) — used by appcheck/instance
      (`Applicability.AddCctDepends`; wired into appcheck `-~`)
- [ ] ICN entity / notation helpers (`add_icn`, `add_notation`) — used by addicn
- [x] XSLT extension shim for EXSLT — `S1kdTools.Xslt.Exslt` (str/math/date/set/
      common extension objects + Transform helper); native exsl:node-set used.

## 2. libs1kd public API (port of `tools/libs1kd/include/s1kd/*.h`)
- [x] `Metadata` — full key table (72 keys) + composite get/set + create-on-set
      + ICN-file metadata (icn_metadata[])
- [x] `Instance` — `Filter(doc, applicability, mode)` with FilterMode enum
- [x] `BrexCheck` — `Check` / `CheckDefault` + BrexCheckOptions flags

## 3. Tools (port of `tools/s1kd-*`)
Generation / `new*` family (share template + .defaults plumbing):
- [x] s1kd-defaults (777) — text<->XML for .defaults/.dmtypes/.fmtypes, default
      generation, sort, BREX-driven generation (DOM, no XSLT). TODO: -i init for
      the non-BREX case shells out to newdm/fmgen (wire up once those exist).
- [x] s1kd-newdm (2110) — flagship; templates+SNS+dmtypes embedded, downgrade
      via to*.xsl. TODO: interactive -p prompt (no-op), byte-exact output.
- [x] s1kd-newpm (1002) — incl. dmRef generation and to*.xsl downgrade.
- [x] s1kd-newcom (906) — incl. pre-Issue-6 downgrade (to*.xsl).
- [x] s1kd-newddn (797) — incl. pre-Issue-6 downgrade; -p no-op.
- [x] s1kd-newdml (1171) — incl. sort.xsl/sns2dmrl.xsl + pre-6 downgrade.
- [x] s1kd-newimf (663) — incl. 4.2/5.0 structural downgrade.
- [x] s1kd-newsmc (965) — incl. 4.1/4.2/5.0 downgrade.
- [x] s1kd-newupf (852) — diff two issues → delete/insert/replace groups.
- [x] s1kd-dmrl (274) — drives new* in-process via the registry.

Authoring:
- [x] s1kd-addicn (111) — ICN entity/notation declarations. NOTE: XmlDocument
      can't add NOTATION/ENTITY via DOM; serialized DTD manually.
- [x] s1kd-ls (1050) — type selection, official/inwork, latest/old, recursive,
      list input, writable/read-only, null output, -e/--exec (runs the command
      via /bin/sh -c, expanding {} to the path) and -N file inwork lookup.
- [x] s1kd-metadata (3240) — list/edit metadata (big key table)
- [x] s1kd-mvref (768) — recode dmRef/pmRef from a source object to a target.
- [x] s1kd-ref (2040) — build/insert references from codes; -T transform;
      .externalpubs; downgrade via to*.xsl.
- [x] s1kd-sns (468) — directory-tree generation from an SNS. NOTE: hard-link
      mode falls back to copy (no portable BCL hard-link API).
- [x] s1kd-upissue (1016) — inwork/official workflow, RFU/change marks, QA
      reset, file renaming, issue 3.0 vs 4.x switching; bundled short flags
      (-ife) and libxml2 parser long-opts now handled (getopt-style parser).

Validation:
- [x] s1kd-validate (633) — well-formedness + faithful IDREF/IDREFS checks +
      XML report; XSD validation when schema is locally resolvable (graceful
      offline); -T stats.xsl summary (XSLT 1.0). TODO: source line numbers
      (BCL XmlDocument has no IXmlLineInfo).
- [x] s1kd-brexcheck (9147) — library API + tool; structure-object & value
      rules; SNS rules (with layered-BREX merge) + notation rules (via DOM
      DocumentType.Notations); severity-level config (brsl, -w/.brseveritylevels)
      now implemented. Remaining: EXSLT/XPath-2 objectPaths → xpathError.
- [x] s1kd-refs (2794) — reference listing + CSDB matching (all ref types) +
      update/overwrite/tag-unmatched, externalpubs, hotspot matching (-H/-j/-J,
      $id var + registered NS via a custom XsltContext) and exec (-e). TODO:
      non-chapterized IPD SNS (-b).
- [x] s1kd-repcheck (965) — CIR reference validation, all 12 ref types + indirect
      (DOM reimpl of the extraction XSLTs); -X custom XSLT (via XslCompiledTransform,
      strips repcheck attrs). TODO: line numbers (BCL XmlDocument limitation).
- [x] s1kd-appcheck (2840) — applicability validation (undefined props, nested,
      redundant, duplicate; standalone/full/products via in-process filter +
      broken-internalRef detection; CCT deps -~ via Applicability.AddCctDepends).
      PARTIAL: external -e/-b validators.

Publication:
- [x] s1kd-acronyms (1020) — markup from .acronyms + list/table generation
      (DOM markup + original XSLTs). TODO: interactive -i/-I prompting (no-op).
- [x] s1kd-aspp (929) — applicability preprocessing; display-text reimplemented
      in DOM (C path needs EXSLT str:replace). -x custom XSLT done (mux + XSLT
      transform). NOTE: aspp does not use add_cct_depends in the C.
- [x] s1kd-flatten (765) — resolve dmRef/pmRef to files and inline; -u dedup
      reimplemented in DOM (C uses EXSLT). 
- [x] s1kd-fmgen (1021) — front matter via 10 embedded XSLTs (all XSLT 1.0, no
      EXSLT). XProc (.xpl) pipelines supported for -x/.fmtypes stylesheets
      (p:pipeline of p:xslt steps, p:document/p:inline inputs, p:with-param).
- [x] s1kd-icncatalog (577) — resolve ICN refs via catalog; media groups; regex
      pattern rules (DOM). NOTE: DTD serialized manually like addicn.
- [x] s1kd-index (400) — keyword flagging from .indexflags; issue-3.0 rename
      (DOM; XSLTs reimplemented).
- [x] s1kd-instance (5126) — applicability filtering core (Default/Reduce/
      Simplify/Prune) + Instance library API; CIR resolution, PCT product
      filtering, containers (-Q), alts flattening (-F/-4), auto-naming
      (-O/-5/-N), and update-instances (-@/-8/-7) now implemented. Remaining:
      set-applic, list-properties, comments, acronym/entity cleanup, list input.
- [x] s1kd-neutralize (292) — IETP neutral metadata via embedded XSLT
      (xlink/rdf/namespace/delete; no EXSLT).
- [x] s1kd-syncrefs (504) — rebuild the References table (refs) from references
      in a data module (DOM, no XSLT).
- [x] s1kd-uom (599) — UOM conversion via .uom rules + presets (DOM, formula
      evaluator); -p/-P display preformatting (uomdisplay.xsl reimplemented in
      DOM: quantity/group/value/tolerance templates + picture format-number).

## 4. Cross-cutting
- [~] Common option handling: `--version`, `-h/--help` done per tool; libxml2
      parse opts (`--huge`, `--net`, `--noent`, `--xinclude`, `--xml-catalog`)
      accepted-but-ignored (no System.Xml equivalent)
- [x] Embedded resources: `Resources/**` glob + `EmbeddedResources` loader
- [x] Multi-call dispatch (`s1kd-<tool>` argv[0] routing) in CLI
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
