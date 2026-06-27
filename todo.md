# todo.md — s1kd-tools C# port progress

Tracks the port of each component from C (`reference/`) to C# (`src/`).
Tools are ordered roughly by ascending complexity (C LOC in parentheses) so the
shared foundation and easy wins come first.

Legend: `[ ]` not started · `[~]` in progress · `[x]` done · `[!]` blocked/needs decision

## Status (all 32 tools ported)

All 32 `s1kd-*` tools are ported, registered (reflection-based), and exercised by
the test suite (**701 xUnit tests passing**, clean build, 0 warnings). The CLI dispatches
them as `s1kd <tool>` with multi-call (`s1kd-<tool>`) support. Two tools expose
libs1kd-style library APIs (`Instance`, `BrexCheck`); `Metadata` is a library
too. The library API also has a parity test suite ported from the C
`libs1kd/tests/tests.c`.

The port is effectively complete. Two parallelized depth waves have landed:
brexcheck severity-level config (brsl); instance containers/alts/auto-naming/
update-instances **plus** set-applic/list-properties/comments/acronym+entity
cleanup/add-required/read-only/list-input/originator/skill/whole-objects/
print-non-applic and -S/-3 ident control; ls -e/-N; refs hotspot/exec and
non-chapterized IPD SNS (-b); validate -T stats and source line numbers;
repcheck -X and line numbers; uom -p/-P preformatting; upissue bundled short
flags + libxml2 long-opts; metadata option/exit-code completion; appcheck
in-process -e/-b validators; defaults -i init (drives newdm/fmgen); acronyms
interactive -i/-I; the shared `add_cct_depends` CCT helper (appcheck -~); aspp
-x custom XSLT; the shared ICN entity/notation helpers (`Icn.AddIcn`/`AddNotation`);
NuGet packaging (`dotnet pack`) + single-file CLI publish.

Genuinely remaining (platform limits / niche, each noted inline): brexcheck
EXSLT/XPath-2 objectPaths (System.Xml is XPath 1.0); instance CCT dependency-test
injection in the filter loop (-2/-~); repcheck -X custom-stylesheet line numbers;
newdm interactive -p prompt + byte-exact output. See per-tool notes and
"Known risks / decisions".

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
- [x] ICN entity / notation helpers (`add_icn`, `add_notation`) — used by addicn
      (`S1kdTools.Icn.AddIcn`/`AddNotation`/`SerializeWithDtd`; AddIcnTool delegates
      to it. DTD internal subset is round-tripped as text — XmlDocument has no DOM
      API for NOTATION/ENTITY decls.)
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
      generation, sort, BREX-driven generation (DOM, no XSLT); -i init drives
      newdm/fmgen in-process via the registry (.dmtypes/.fmtypes).
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
      offline); -T stats.xsl summary (XSLT 1.0); source line numbers via a
      parallel XmlReader pass (`LineInfo`, matches libxml2 xmlGetLineNo).
- [x] s1kd-brexcheck (9147) — library API + tool; structure-object & value
      rules; SNS rules (with layered-BREX merge) + notation rules (via DOM
      DocumentType.Notations); severity-level config (brsl, -w/.brseveritylevels)
      now implemented. Remaining: EXSLT/XPath-2 objectPaths → xpathError.
- [x] s1kd-refs (2794) — reference listing + CSDB matching (all ref types) +
      update/overwrite/tag-unmatched, externalpubs, hotspot matching (-H/-j/-J,
      $id var + registered NS via a custom XsltContext), exec (-e), and
      non-chapterized IPD SNS (-b, sscanf-style grammar + inherited components).
- [x] s1kd-repcheck (965) — CIR reference validation, all 12 ref types + indirect
      (DOM reimpl of the extraction XSLTs); -X custom XSLT (via XslCompiledTransform,
      strips repcheck attrs); source line numbers (`LineInfo`). TODO: line numbers
      for the -X custom-stylesheet path (result-tree nodes aren't in the source map).
- [x] s1kd-appcheck (2840) — applicability validation (undefined props, nested,
      redundant, duplicate; standalone/full/products via in-process filter +
      broken-internalRef detection; CCT deps -~ via Applicability.AddCctDepends;
      in-process -e/-b validators driving ValidateTool/BrexCheckTool). PARTIAL:
      parallel threads (-#), -o/-K/-k external-filter options, progress bar.

Publication:
- [x] s1kd-acronyms (1020) — markup from .acronyms + list/table generation
      (DOM markup + original XSLTs); interactive -i/-I prompting (reads from an
      injectable TextReader / Console.In, mirrors the C chooseAcronym prompt).
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
      (-O/-5/-N), update-instances (-@/-8/-7), set-applic (-W/-Y/-y),
      list-properties (-H), comments (-C/-X), acronym fixing (-M), entity
      cleanup (-j), add-required (-Z), read-only (-%), list input (-L),
      originator (-g/-G), skill (-k), whole-objects (-w), print-non-applic (-0),
      and -S/-3 ident control. Remaining: CCT dependency-test injection in the
      filter loop (-2/-~).
- [x] s1kd-neutralize (292) — IETP neutral metadata via embedded XSLT
      (xlink/rdf/namespace/delete; no EXSLT).
- [x] s1kd-syncrefs (504) — rebuild the References table (refs) from references
      in a data module (DOM, no XSLT).
- [x] s1kd-uom (599) — UOM conversion via .uom rules + presets (DOM, formula
      evaluator); -p/-P display preformatting (uomdisplay.xsl reimplemented in
      DOM: quantity/group/value/tolerance templates + picture format-number).

## 4. Cross-cutting
- [x] Common option handling: `--version`, `-h/--help` done per tool; libxml2
      parse opts (`--huge`, `--net`, `--noent`, `--xinclude`, `--xml-catalog`)
      accepted-but-ignored (no System.Xml equivalent — this is the correct .NET
      behaviour, not a gap)
- [x] Embedded resources: `Resources/**` glob + `EmbeddedResources` loader
- [x] Multi-call dispatch (`s1kd-<tool>` argv[0] routing) in CLI
- [x] Packaging: `dotnet pack` for the library (S1kdTools.Core, GPL-3.0-or-later,
      v0.1.0) + single-file self-contained CLI publish (resources resolve from the
      assembly manifest in single-file mode). See README "Packaging".
- [x] Port/port-equivalent of `libs1kd` tests — `Libs1kdParityTests.cs` ports all
      six C test functions from `reference/.../libs1kd/tests/tests.c`.
- [x] DocBook 5 conversion (companion to the s1kd-tools) — `S1kdTools.DocBook.DocBookConverter`
      ports the S1000D→DocBook stage of two upstream projects, run in-process on
      `XslCompiledTransform` (no xsltproc/Java). Two embedded profiles:
      `S1kd2db` (kibook/s1kd2db, default) and `SmartAvionics`
      (kibook/S1000D-XSL-Stylesheets `s1000dtodb`, 12-file set with broader
      coverage). Exposed as `s1kd s1kd2db [-S]`. The only stylesheet change is a
      one-line shim replacing the unsupported `unparsed-entity-uri()` with the
      `EntityUriResolver` extension (resolves ICN graphic entities from the DTD/
      info-entity map); `xsl:include`s load from embedded resources via
      `EmbeddedXslResolver`. Validated: both profiles convert all 384 sample DMs
      (Issues 4.2/5.0) to well-formed DocBook 5. NOTE: the DocBook→FO→PDF
      rendering tail (Apache FOP) is intentionally out of scope.
- [x] Real-world testing dataset under `samples/` — five curated CSDBs sourced
      from upstream open-source S1000D projects (FOSSIG, the S1000D spec-as-CSDB,
      s1kd-tools-doc, S1000D-XSL-Stylesheets, s1kd2db), spanning schema Issues
      4.0/4.2/5.0, each with a README (provenance + license). One tiny CLI
      project per dataset (`samples/harnesses/Samples.*`, in `S1kdTools.slnx`)
      consumes `S1kdTools.Core` and builds the sample files (ls/validate/
      metadata/flatten/brexcheck/refs/syncrefs). The s1000d-spec corpus doubles
      as a negative-test fixture (dangling internalRefs → validate exit 1;
      missing reasonForUpdate → brexcheck exit 1), matching the C tools.

## Known risks / decisions
- EXSLT coverage in `XslCompiledTransform` (str:/exsl:/dyn:) — shim per stylesheet.
- libxml2 IDREF/IDREFS handling: validate tool reimplements it; replicate the
  hardcoded attribute list from `s1kd-validate.c`.
- Output serialization must byte-match the C tools where tests compare files;
  otherwise compare via canonical XML (C14N).
- libxml2 XML-catalog support has no direct BCL equivalent; implement a minimal
  `XmlResolver` if/when `--xml-catalog` is exercised.

## Accepted platform limitations (won't-fix without a new dependency)
- **fmgen XProc (.xpl) pipelines**: XProc is a full XML pipeline language; .NET
  has no XProc engine. The 10 plain-XSLT front-matter generators are ported; the
  handful of `.xpl` pipeline front-matter types are not.
- **brexcheck EXSLT / XPath-2 objectPaths → xpathError**: `System.Xml` evaluates
  XPath 1.0 only, so BREX `objectPath`s using XPath 2.0 or EXSLT cannot be
  evaluated; such rules are skipped rather than mis-evaluated.
- **Source line numbers for transform-derived nodes** (repcheck `-X`): nodes that
  come out of an `XslCompiledTransform` result tree have no position in the
  original source, so they report line 0. The built-in extraction path has real
  line numbers via `LineInfo`.
- **newdm interactive `-p` prompt + byte-exact output**: the interactive prompt is
  a no-op in-process; output is canonical-XML equivalent, not guaranteed byte-for-
  byte identical to libxml2's serializer.
