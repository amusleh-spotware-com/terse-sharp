This solution compiles successfully and emits three deliberate compiler warnings
(`CS0169`, `CS0414`, `CS0219` in `Calculator`).

It exists so the build response can be tested against the case that matters: a build that succeeds
with warnings must still answer in one line, and a build that fails must list its errors without its
warnings. `FixtureSolution` cannot cover it - many tests assert that it is diagnostic-free. This
solution is not part of `TerseSharp.slnx`; `BuildWarningsE2ETests` builds it through the `build` tool,
so CI does compile it, on the E2E leg only.

`tests/Fixture.Warning.Tests` exists so the same case can be put to the *test* tools: it holds one
passing `[Fact]` over `Calculator` and emits no warnings of its own, so building or testing this
solution still reports exactly the three warnings `Calculator` produces. That lets
`BuildWarningsE2ETests` sweep the whole build/test family — `build`, `run_tests`, `rerun_failed` and
`list_tests`, discovered from `tools/list` rather than hand-written — and assert that none of them
returns a warning unless `verbose=true`. Keep this project warning-free: a warning added here changes
the count `Build_WhenTheSolutionCompilesWithWarnings_AnswersInOneLineAndNamesNone` asserts.
