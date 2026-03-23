# Architecture Docs

Use this folder to understand how the runtime is structured and where behavior should live.

## Recommended Read Order

1. `MentalModel.md`
2. `Overview.md`
3. `BootAndSessions.md`
4. `SystemMap.md`
5. `ArchitectureRules.md`
6. `ServiceResponsibilities.md`
7. `SimulationModel.md`
8. `AntiPatterns.md`

## Also Here

- `DisplayDataPipeline.md` describes planned future architecture for display-content scaling.

## Current UI Infrastructure Note

Feature-facing UI still lives under `Assets/Scripts/UI/...`.

Extractable runtime UI infrastructure now lives under `Assets/Scripts/Ignition/Runtime/...`, including:

- binding contracts and metadata
- runtime binders
- command primitives
- screen/navigation base types

Use `ArchitectureRules.md` as the source of truth for how those layers should interact.
