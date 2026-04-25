namespace BSE_Code.Tests;

/// <summary>
/// Marks tests that must not run in parallel because they mutate shared state
/// (static managers, Directory.SetCurrentDirectory, file system).
/// </summary>
[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialCollection { }
