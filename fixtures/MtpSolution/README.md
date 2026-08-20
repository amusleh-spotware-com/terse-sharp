# MtpSolution

The only fixture whose `global.json` selects the **Microsoft.Testing.Platform** runner
(`"test": { "runner": "Microsoft.Testing.Platform" }`), with a test project that is an executable
MTP host (`OutputType=Exe`, `UseMicrosoftTestingPlatformRunner`, `xunit.v3`, and deliberately no
`Microsoft.NET.Test.Sdk` or `xunit.runner.visualstudio`, which would put it back on VSTest).

`FixtureSolution` cannot cover this: it runs under VSTest, where every VSTest-shaped argument that
this host rejects is accepted, so no assertion made there can fail for the reason
`TestingPlatformE2ETests` exists to catch — `dotnet test` forwarding an argument the test application
does not recognise, which refuses the whole session with `Zero tests ran` and exit code 5.

`Directory.Packages.props` pins `xunit.v3` **3.2.2** here rather than inheriting the repository's,
because the version is part of what the assertions test: 3.2.2 runs MTP v1 and has **no** `--filter`,
while 4.0.0 runs MTP v2 and accepts the VSTest filter syntax. A silent bump would change the argument
surface under test without anyone touching this fixture.

`LedgerTests` is three passing tests, and `DeliberateMtpOutcomesTests` carries one failing and one
skipped, so a run reports `passed=3 failed=1 skipped=1 total=5` — counters, a real failure message
and a source frame, all read from the trx that `--report-xunit-trx` writes. It is intentionally
outside `TerseSharp.slnx`.
