This solution exists to exercise the **code policy** gate, and it is the only fixture that checks in a
`.terse.json` carrying a `policy` section.

`FixtureSolution` cannot cover it. Policy is resolved by walking up from the workspace root, so a
`policy` section added there would switch the gate on for every edit test in the suite - dozens of
assertions that an edit simply applies would start failing on rules those tests were never written
against. A separate solution keeps the blast radius to the tests that mean to opt in.

`Ledger` is deliberately **clean against all twelve default rules** - complexity 1, three statements,
two methods, one parameter, conforming names, no `async void`, no chained reference, one nesting
level. That is the point: the gate reports only what an edit INTRODUCES, so a baseline that already
violated a rule would let a test pass for the wrong reason. Keep it conforming - a violation added
here is subtracted from every later finding and the introduced-only tests stop proving anything.

The checked-in `.terse.json` sets `methodStatements` to `warn` and leaves the rest at their
ReSharper-derived defaults, so one fixture covers both answers: a rule that rejects an edit and a rule
that lets it land with a `WARNING policy` line. `allowOverride` is left `true` so `allowPolicy=true`
can be exercised; a test that needs the override refused rewrites the file itself.

This solution is not part of `TerseSharp.slnx`.
