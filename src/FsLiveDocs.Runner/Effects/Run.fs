namespace FsLiveDocs.Runner.Effects

open Axial

/// Runs Flows against the shared `LiveEnvironment` synchronously. FsLiveDocs' migrated modules
/// stay synchronous and exception-raising at their public boundary -- these two functions are
/// the only place that boundary is crossed, so every caller composes its own work into one Flow
/// first (via `flow { }`/`Flow.traverse`) and runs it exactly once here, rather than once per
/// underlying effect.
[<RequireQualifiedAccess>]
module Run =

    /// Runs a Flow, falling back on any typed failure -- for operations (existence checks) that
    /// never threw in their pre-Axial form and shouldn't start throwing now.
    let orFallback (flow: Flow<LiveEnvironment, 'error, 'value>) (fallback: 'value) =
        match flow |> Flow.run LiveEnvironment.instance with
        | Exit.Success value -> value
        | Exit.Failure _ -> fallback

    /// Runs a Flow, raising `InvalidOperationException` built from `description` and the typed
    /// error on failure, instead of letting a raw, unhandled exception (disk full, permissions,
    /// a process that failed to start) escape uncaught.
    let orRaise (describe: 'error -> string) (description: string) (flow: Flow<LiveEnvironment, 'error, 'value>) =
        match flow |> Flow.run LiveEnvironment.instance with
        | Exit.Success value -> value
        | Exit.Failure(Cause.Fail error) -> invalidOp $"{description}: {describe error}"
        | Exit.Failure cause -> invalidOp $"{description}: {cause}"
