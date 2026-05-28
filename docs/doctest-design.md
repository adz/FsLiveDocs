---
title: DocTest Design
weight: 6
type: explanation
---

# When Not to Use DocTests

FsLiveDocs intentionally runs documentation examples in a real compiled context. That is a strength when you want examples to stay correct, but it also means doc-tests are not the right tool for every job.

## Sandboxing limits

Examples are not isolated from the outside world in the way a dedicated test harness or process sandbox might be.

That means:

1. mutable singletons can leak state between examples if you share them,
2. files written during an example can remain on disk,
3. environment-dependent behavior can vary across machines,
4. global process state can affect later examples.

If your example depends on destructive or non-repeatable behavior, write a normal unit or integration test instead.

## Side effects and output

FsLiveDocs verifies stdout as part of the example run. That is useful for simple console examples, but it is not a general replacement for structured assertions.

Use doc-tests when:

1. output is the primary point of the example,
2. the example is short and deterministic,
3. the example is intended to teach the API surface.

Prefer ordinary tests when:

1. the behavior depends on timing or concurrency,
2. the code writes to external systems,
3. you need richer assertions than `EXPECTED:` output,
4. the setup is so large that the example stops being instructional.

## Practical guidance

Keep documentation examples close to the public API and keep the setup minimal. If a snippet needs a fake clock, an in-memory store, and multiple services just to explain one method call, it is probably better as a regular test plus a shorter doc example.

## Rule of thumb

Doc-tests are best for "this is how you use it" examples.

Normal tests are best for "this is how it behaves under every condition" coverage.
