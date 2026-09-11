/// Lets FsLiveDocs.Tests exercise this assembly's `internal` modules directly, instead of only
/// through Program.fs's command handlers.
module internal FsLiveDocs.Cli.AssemblyInfo

[<assembly: System.Runtime.CompilerServices.InternalsVisibleTo("FsLiveDocs.Tests")>]
do ()
