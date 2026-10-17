This solution maps `src/Mapped` to Release|x64 under every solution configuration, and that project turns
on `TreatWarningsAsErrors` only under Debug|AnyCPU. `build` therefore reports `CS4014` as a warning, while
MSBuildWorkspace, which evaluates each project at its own default, reports it as an error. `src/Plain` has
no mapping and must never be named. `ConfigMappingE2ETests` builds it; it is not part of `TerseSharp.slnx`.
