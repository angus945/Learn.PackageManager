# module.verification.oracle

Passive, ordered evaluation of immutable caller-provided contexts.

The module distinguishes test verdicts from operation outcomes, preserves stable oracle order, classifies evaluation exceptions as infrastructure errors, and never captures observations, submits operations, waits, persists evidence or controls a target.

The assembly targets `netstandard2.1`, uses C# 9, and has no Unity, Simulation or Playback dependency.

Oracle does not retain runtime history, control runtime, store diagnostics or own evidence. Its public expectation-judgment contract is frozen.
