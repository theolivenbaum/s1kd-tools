// Several tools and their tests change the process working directory (to
// resolve .defaults / .dmtypes config files and to emit generated objects into
// a temp CSDB). The current directory is process-global, so xUnit's default
// cross-class parallelism causes races. Disable parallelization to keep these
// directory-sensitive tests deterministic.
[assembly: Xunit.CollectionBehavior(DisableTestParallelization = true)]
