This solution contains a deliberate compile error (`Calculator.PreExistingError`).

It exists so the compile gate can be tested against the case that matters: an edit to a *valid*
member of a file that already has an unrelated error must still apply. It is not part of
`TerseSharp.slnx` and is never built by CI.
