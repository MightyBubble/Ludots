# Entity attachment topology measurements — 2026-09-09

The base is origin/main ffd11358f7929544c9f1f193b96aeb1eb28616d3. Baseline test commit: 53c30221d3. This report covers AttachmentPositionSyncSystem only. It does not establish whole-machine FPS or resolve navigation membership ownership.

## Reproduction and scope

Release, DOTNET_TieredCompilation=0, 16 warmup updates, 33 measured updates. Each scene has the stated number of explicitly attached entities, in independent chains of the given maximum length. Roots move before each measured update. Final leaf positions and zero update allocations are asserted. Setup, authoring and topology-change costs are outside the measured steady-state interval.

The first baseline run passed 11/12 tests and failed the logical-ancestor spatial-depth assertion. After implementation, the focused attachment, transaction and capability regression set passed 43/43. See before/topology-before.trx and after/topology-functional.trx.

The stock GasTests project cannot compile because GasCore/DeferredTriggerTests.cs lines 42–44 and 57 resolve Throws to Assert.Throws (CS0119). This existing file was introduced by 0261842da8. The local focused-gas-tests.targets excludes exactly that unrelated source file; it must be specified explicitly and is not imported by project defaults. Thus these results are focused tests, not a successful complete GasTests build.

```powershell
dotnet test src/Tests/GasTests/GasTests.csproj -c Release --no-restore -p:WarningLevel=0 -p:CustomAfterMicrosoftCommonTargets=<absolute-repo>/artifacts/evidence/entity-attachment-motion/focused-gas-tests.targets --filter 'FullyQualifiedName~AttachmentPositionScaleTests|FullyQualifiedName~AttachmentPositionSyncSystemTests|FullyQualifiedName~EntityAttachmentTests|FullyQualifiedName~EntityAttachmentCapabilityAcceptanceTests'
```

## A/B observations

A1 is the initial baseline, B1 is the first optimized run, B2 repeats the optimized binary, A2 restores the saved original Ludots.Core.dll into this worktree's test output and runs the same scale tests. The optimized binary was restored in a finally block. No other worktree's binaries were changed. All four scale runs passed nine cases with zero measured allocations.

| Attached entities | Maximum chain length | A1 median ms | B1 median ms | B2 median ms | A2 median ms |
| ---: | ---: | ---: | ---: | ---: | ---: |
| 1000 | 1 | 0.134500 | 0.051400 | 0.046700 | 0.048000 |
| 1000 | 32 | 0.642400 | 0.051300 | 0.048700 | 0.304100 |
| 1000 | 128 | 1.364700 | 0.065400 | 0.057100 | 0.893500 |
| 5000 | 1 | 0.348600 | 0.229100 | 0.264400 | 0.292100 |
| 5000 | 32 | 3.010200 | 0.245900 | 0.249300 | 1.178000 |
| 5000 | 128 | 6.976000 | 0.255300 | 0.241300 | 4.184800 |
| 10000 | 1 | 0.843200 | 0.477900 | 0.470900 | 0.524400 |
| 10000 | 32 | 6.332500 | 0.508200 | 0.480700 | 2.376200 |
| 10000 | 128 | 14.221000 | 0.542900 | 0.490500 | 8.114300 |

Raw CSV files include P95 and allocations in before/, after/, b2/ and a2/. The one-level control varies across runs; do not quote a universal speedup factor. The 10k, 128-level baseline spans 8.11–14.22 ms while optimized samples span 0.49–0.54 ms. This supports removal of depth-dependent rescans in this workload.

## Implementation boundary

The existing declared scratch capacity bounds all arrays and the preallocated entity-to-slot dictionary. Complete Entity identity is used. Chunk/Span iteration collects only explicit attachments. Cached order is invalidated by entity identity, parent identity or count changes; changed local pose is still consumed each update. Order construction processes each attachment and direct dependency once, detects cycles before writes, and preserves sibling ordering from the snapshot.

The current navigation Suspend/Restore coupling, schedule order, previous-pose interpolation, implicit template children placement and orphan structural cleanup remain to be migrated in the broader entity motion contract. This optimization does not claim those issues are fixed. Presenter changes remain in PR #1486.
