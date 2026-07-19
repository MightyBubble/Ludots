# PR #658 / Issue #690 Review Packet

- Subject: issue #690, PR #658
- Branch: `codex/fix-pr-658-final`
- Remote PR branch: `codex/massnav-unified-rework`
- Intent: restore the single Order -> Ability/GAS -> typed MovePlan -> MassNavigation execution chain and remove the Formation/MassNavigation parallel order pipeline.

## Reviewer Summary

The historical PR optimized the wrong ownership boundary: MassNavigation inspected and completed `OrderBuffer`, while Core Formation introduced dedicated orders and execution state. This packet replaces that design with one canonical chain:

```text
CommandSource anchor
-> CommandIntentProfile / CastDispatch
-> FormationCommandActorExpander
-> clustered atomic OrderQueue batch
-> member OrderBuffer
-> GAS MovePlanOrderProjectionSystem
-> MovePlanExecutionIntent(CommandGroup)
-> MassNavigationMovePlanExecutionSystem
-> MovePlanExecutionResult
-> GAS MovePlanOrderLifecycleSystem
-> complete or cancel Order
```

MassNavigation consumes typed intent/result data only. Formation is showcase-owned and only expands an anchor command into member actors through Command Router. The anchor is selectable business/presentation state and is not an order or navigation actor.

## Changed File Clusters

### Order and command routing

- `src/Core/Input/Interaction/ICommandActorExpander.cs`
- `src/Core/Input/Orders/InputOrderMappingSystem.cs`
- `src/Core/Gameplay/GAS/Orders/OrderQueue.cs`
- `src/Core/Gameplay/GAS/Systems/OrderBufferSystem.cs`
- `src/Core/Gameplay/GAS/Orders/OrderSubmitter.cs`
- `src/Core/Gameplay/GAS/Systems/MovePlanOrderProjectionSystem.cs`
- `src/Core/Gameplay/GAS/Systems/MovePlanOrderLifecycleSystem.cs`

### Typed MassNavigation execution

- `src/Core/MovePlanning/MovePlanTypes.cs`
- `src/Core/MassNavigation/Systems/MassNavigationMovePlanExecutionSystem.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationGroupRuntime.cs`
- `src/Core/MassNavigation/Runtime/MassNavigationRouteExecutionSink.cs`
- `mods/capabilities/navigation/MassNavigationMod/MassNavigationModEntry.cs`

Prepare computes the resolved destination and exact per-member targets once. Binding, focus, route, and capacity checks validate that data; commit consumes the same prepared targets without recomputation. Route rejection emits typed failure before group/solver mutation.

### Showcase-owned Formation

- `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/Runtime/`
- `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/Systems/FormationCommandActorExpander.cs`
- `mods/showcases/formation_capability/FormationCapabilityShowcaseMod/assets/`

Deleted Core Formation, dedicated Formation orders, fake Q/E rotation, the unused Formation navigation profile, and the duplicate default input file.

### Test cleanup

- Deleted the 6203-line Formation source/config contract fixture.
- Deleted MassNavigation tests that scanned source strings, private field names, evidence-recorder wording, or fixed Markdown sentences.
- Added behavior tests for clustered atomic admission, projection/lifecycle failure semantics, Command Router expansion, Formation lifecycle/planning, typed Mass execution, route rejection, and group transactions.

### Documentation and governance

- `gitbook/reference/mass-navigation-formal-chain.md`
- `gitbook/reference/mass-navigation-user-book.md`
- `gitbook/architecture/entity-simulation-layering.md`
- `gitbook/architecture/entity-simulation-uat.md`
- `gitbook/architecture/mass-navigation-numeric-domain.md`
- `gitbook/architecture/uat-playable-showcase-matrix.md`
- `artifacts/gas-composition-gate.md`
- `artifacts/doc-governance-report.md`

Issue #690 is the only current SSOT. Historical issues #642, #505, #533, #567, #657, #659, #682, and #683 are superseded context, not competing architecture sources.

## Validation

- Core Release build: passed, 0 errors.
- `MassNavigationMod` Release build: passed, 0 warnings, 0 errors.
- `FormationCapabilityShowcaseMod` Release build: passed, 0 warnings, 0 errors.
- Related Presentation tests: 61/61 passed.
- GAS MovePlan lifecycle tests through temporary clean host link: 6/6 passed; link removed after execution.
- Command Router clustered fan-out test through temporary clean host link: 1/1 passed; links removed after execution.
- ArchitectureTests build: passed, 0 errors.
- ArchitectureTests full run: 143 passed, 4 unrelated launcher/dependency failures.
- Formation launcher resolve: passed; ordered mods are `LudotsCoreMod`, `CoreInputMod`, `CameraProfilesMod`, `MassNavigationMod`, `FormationCapabilityShowcaseMod`; diagnostics warnings are empty.
- All changed JSON files parse successfully.
- 10 changed Markdown files pass link and repository-path validation.
- `git diff --check`: passed.

## Known External Blockers

- Full ArchitectureTests has four unrelated failures for missing `UtilityAutocastShowcaseMod`, `ParticipantViewCapabilityMod`, `BrowserReactFlowShowcaseMod`, and `ProgressionScopeShowcaseMod`.
- `GasTests.csproj` cannot build as a whole because unrelated referenced projects/artifacts are missing, including `DepApiMod`, participant/relationship reference assemblies, and Road's missing `RoadNetworkShowcaseData.csproj`.
- NU1900 vulnerability-feed warnings reflect unavailable NuGet vulnerability metadata and are not compilation failures.

These blockers were not repaired because they are outside PR #658 ownership and other work may be in progress there.

## Visual Evidence

No new rendering or UI feature was added. The player surface was reduced by deleting the fake rotation behavior; launcher resolution and behavior tests cover the retained select-anchor/right-click-move workflow. No separate visual review packet is required for this architecture-only closeout.

## Audit Score

- Historical remote head `f9942ef0`: 4.0/10, NO-GO.
- Final branch: 9.0/10, GO subject to normal PR CI rerun.

The remaining deduction is for repository-wide test projects already broken outside this PR, which prevents one clean top-level test command even though all in-scope builds and behavior tests pass.
