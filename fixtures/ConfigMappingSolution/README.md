This solution maps `src/Mapped` to Release|x64 under every solution configuration, and that project turns
on `TreatWarningsAsErrors` only under Debug|AnyCPU. `build` therefore reports `CS4014` as a warning, while
MSBuildWorkspace, which evaluates each project at its own default, would report it as an error - terse
re-reads `TreatWarningsAsErrors`, `WarningsAsErrors`, `WarningsNotAsErrors` and `NoWarn` at the mapped
configuration, so `get_diagnostics` and an edit's `errors=` report it as a warning too. `src/Plain` has
no mapping and must never be named. `ConfigMappingE2ETests` builds it; it is not part of `TerseSharp.slnx`.
