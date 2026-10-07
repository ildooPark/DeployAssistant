using Xunit;

// Screens write through the static AnsiConsole, and the frame tests swap it (and Term's glyph
// mode) to capture output. Parallel classes would write into each other's capture buffers.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
