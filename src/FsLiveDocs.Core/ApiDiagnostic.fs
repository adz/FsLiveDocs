namespace FsLiveDocs.Core

/// <summary>
/// How much a documented-API problem matters.
/// </summary>
/// <remarks>
/// Documented code that does not compile breaks the guarantee the tool exists to provide, so it
/// is an error. A problem that only degrades presentation, such as a parameter shown under a
/// generated name, is a warning: the documentation is still correct, and the author may not be
/// free to change the API being documented.
/// </remarks>
type ApiDiagnosticSeverity =
    | Warning
    | Error

/// <summary>
/// A quality problem found in the documented API itself, reported against the source that
/// declares it. Diagnostics describe a single extraction run rather than the documented
/// snapshot, so they are deliberately not part of <see cref="T:FsLiveDocs.Core.PackageModel"/>.
/// </summary>
type ApiDiagnostic = {
    /// <summary>Stable kebab-case identifier for the kind of problem.</summary>
    Code: string
    /// <summary>Whether this fails the run or only reports.</summary>
    Severity: ApiDiagnosticSeverity
    /// <summary>Full name of the member the diagnostic concerns.</summary>
    Symbol: string
    /// <summary>Source location that declares the member.</summary>
    Location: SourceLink
    /// <summary>One-line description of what was found.</summary>
    Message: string
    /// <summary>What the author can change to resolve it.</summary>
    Remedy: string
}
