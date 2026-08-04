This solution compiles successfully and emits three deliberate compiler warnings
(`CS0169`, `CS0414`, `CS0219` in `Calculator`).

It exists so the build response can be tested against the case that matters: a build that succeeds
with warnings must still answer in one line, and a build that fails must list its errors without its
warnings. `FixtureSolution` cannot cover it - many tests assert that it is diagnostic-free. This
solution is not part of `TerseSharp.slnx`; `BuildWarningsE2ETests` builds it through the `build` tool,
so CI does compile it, on the E2E leg only.

It has no test project, so `run_tests` and `list_tests` cannot be covered against it — their
warning-hiding is covered at the render-function level in `DotnetRunnerTests`. Adding one
warning-emitting test project here would close that gap.
