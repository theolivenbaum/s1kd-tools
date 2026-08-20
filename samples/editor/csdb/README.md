# Sample CSDB

Ten S1000D data modules and the two ICNs they illustrate, covering the object types
the editor is most worth trying on:

| File | Type | What it exercises in the editor |
| --- | --- | --- |
| `…-720A-A` | Procedure | Numbered steps, nested steps, warnings and cautions, a figure, job set-up information |
| `…-042A-A` | Descriptive | Levelled paragraphs with titles |
| `…-941A-A` | Illustrated parts data | A parts list as rows of labelled fields |
| `…-420A-A` | Fault isolation | An isolation procedure's question/answer tree |
| `…-130A-A` | Crew | Crew drill steps |
| `…-00SA-D` | Service bulletin | The service-bulletin sections |
| `…-301A-A` | Checklist | Check list items |
| `…-310A-A` | Maintenance planning | Inspection and task definitions |
| `…-258A-A` | Process | A data module sequence |
| `…-002A-D` | Front matter | A list of effective data modules |

**Everything here is synthetic.** The content is copied from the Airbus
technical-data demo (`curiosity-ai/tech-data-demo`,
`src/TechData.Presentation.Tests/Samples/`), where it is written to *look* like
A350 maintenance data without being any of it — the model identifier `A350X`, the
part numbers, the modification numbers and the prose are all invented. No real
Airbus technical data is in this repository.

The files were renamed from their schema names (`proced.xml`, `descript.xml`, …)
to the CSDB file names their own data module codes give them, because that is how
a CSDB addresses an object and the sample should not teach otherwise.

**The editor does not write here.** `save` writes to `samples/out/editor/`, so the
sample can be run, edited and saved as many times as you like without the checked-in
modules drifting. A real server would write back to the CSDB it read from.
