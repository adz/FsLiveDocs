namespace FsLiveDocs.Runner

open Axial
open Axial.Layers
open FSharp.Compiler.CodeAnalysis

/// A small, pooled set of live F# compiler checkers. Hides how they're provisioned and
/// distributed so callers never see Axial's Flow/Layer machinery.
module CheckerPool =

    /// A ready-to-use pool of live checkers.
    type T = private { Pool: Pool<FSharpChecker> }

    /// Provisions `size` live checkers eagerly.
    let create (size: int) : T =
        let layer = Layer.pool size (fun _ -> Layer.succeed (FSharpChecker.Create(keepAssemblyContents = true)))
        match Flow.env<Pool<FSharpChecker>, string> |> Layer.provide layer |> Flow.run () with
        | Exit.Success pool -> { Pool = pool }
        | Exit.Failure cause -> failwithf "Failed to provision the F# checker pool: %O" cause

    /// Returns the next checker, distributing calls round-robin across the pool.
    let next (pool: T) : FSharpChecker = pool.Pool.Next()

    /// A single fixed checker for one-off calls outside the round-robin hot path,
    /// such as resolving shared project options once.
    let anyOne (pool: T) : FSharpChecker = pool.Pool.Instances |> Seq.head
