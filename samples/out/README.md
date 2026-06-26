# Generated harness output

**These files are generated** by the sample harnesses in
[`../harnesses/`](../harnesses) — one folder per dataset. They are checked in as
a reference snapshot of the expected results (flattened publications, metadata
listings, and `validate`/`brexcheck` XML reports) so they can be browsed and
diffed without running anything.

Do not edit by hand. Each harness **deletes and recreates** its own
`out/<dataset>/` folder at startup, so to refresh the snapshot just re-run the
harnesses from the repository root:

```bash
for h in Fossig S1000DSpec ToolsDoc XslStylesheets S1kd2db; do
  dotnet run --project samples/harnesses/Samples.$h
done
```

See [`../README.md`](../README.md) for what each artifact is and which dataset
it came from.
