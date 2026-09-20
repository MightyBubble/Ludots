# Presenter anchor/local offset single consumption entry

## Scenario Card
- Player goal: spawn a presenter with `anchor.offset` plus per-asset `localOffset` outputs and get one stable world position across mesh, surface, and skinned outputs, no matter how many emits run.
- Baseline branch: `codex/issue-1094-anchor-transform-entry` (frozen on 2ca3147930-era main)
- Real fixture: `mods/fixtures/presenter_schema_reference/PresenterSchemaReferenceMod/assets/Presentation/presenters.json` (anchor demonstration moved to `ref_anchor_offset_definition`), `mods/showcases/capability_standard/CapabilityStandardGraphOpsNodeGalleryMod` (duplicated anchor removed from `graphops.hud.health_bar`).

## Defect being removed
Before this change the presenter root transform was written by four writers with disagreeing semantics:
- `InitializeTransform`/batch create wrote `owner + anchor.offset` into `PresenterWorldPosition`.
- `PresenterEntityTransformSyncSystem` overwrote it with the raw owner position (offset silently dropped).
- Bootstrap `ResolveTransform(Batch)` re-based the root on the raw owner and additionally baked the primary AssetBinding's `localOffset`/`localRotation`/`localScale` into the root.
- Every emitter re-added `definition.PositionOffset` (`PresenterAssetEmitRuntime.ResolvePosition`, `EmitSurfaceSourceIfAny`, skinned batch emit).

Net effect: on the first emit the anchor offset was applied twice (init + emit); after the owner moved it was applied once (emit only) — the visual position jumped by `anchor.offset` between emits; for static-stable visuals the primary asset local offset stayed double-applied forever.

## Single-entry contract after this change
- `PresenterWorldPosition` is the resolved root transform SSOT: `anchor base + anchor.offset` for `EntityTransform`, `parent root + anchor.offset` for `InheritParent`, initialized exactly once (`InitializeTransform`, batch create) and maintained by the offset-preserving sync writers.
- Emitters are pure readers: `ResolvePosition` only applies per-slot yDrift; surface `AnchorPosition` is the root itself; skinned batch consumes the same root.
- `AssetBinding.localOffset/localRotation/localScale` compose exactly once at the asset emit stage (`ResolveAssetPosition`/`ResolveRotation`/`ResolveScale`) and never enter the root resolution; `PresenterInstanceTransformOverride` remains the only root-level per-instance composition source.
- `PresenterLocalOffsetConsumption.MarkSlotConsumed` guards one consumption per slot per emit visit and throws a diagnostic naming the presenter definition and slot on a second consumption.
- Loader rejects duplicate root offset sources at load time: `anchor.offset` + `Attachment` behavior on one definition, and `children[i].overrides.transform.localPosition` + child definition `anchor.offset`.

## Timeline
- [T+000] `contract_tests` -> `PresenterGroundingAndGlobalEventTests` ResolveTransform unit tests updated to the root-only contract (asset local no longer composed into root).
- [T+001] `multi_output_test` -> mesh, surface, and skinned outputs share the same resolved root; anchor applied once (`Emit_MeshSurfaceAndSkinnedShareResolvedRootTransform_AnchorAppliedOnce`).
- [T+002] `repeat_emit_test` -> three consecutive emits keep the identical world position; offset never accumulates (`Emit_RepeatedEmit_AnchorOffsetDoesNotAccumulate`).
- [T+003] `sync_test` -> moving the owner twice keeps the resolved root at `owner + anchor.offset` without drift (`TransformSync_MovingOwner_TracksAnchorOffsetWithoutDrift`).
- [T+004] `attachment_test` -> parent attachment positions the child root exactly once; asset localOffset composes once on top (`Emit_AttachedChild_ConsumesAttachmentOnceAndLocalOffsetOnce`).
- [T+005] `grounding_test` -> SnapToGround snaps the resolved root (not the asset point); asset localOffset stays relative to the grounded root (`Grounding_SnapsResolvedRoot_AndKeepsLocalOffsetRelative`).
- [T+006] `platform_test` -> WorldFixed anchor initializes `world + anchor.offset` once and stays stable across bootstrap resolution and repeated emits (`WorldFixedAnchor_RootStaysStableAcrossBootstrapAndEmit`).
- [T+007] `consumed_marker_test` -> double localOffset consumption produces the diagnostic with definition id and slot (`LocalOffsetConsumption_DoubleConsume_ProducesDiagnostic`).
- [T+008] `loader_conflict_tests` -> anchor+attachment and child-override+child-anchor configurations fail to load with explicit messages; anchor-only and attachment-only load (`Load_RejectsAnchorOffsetCombinedWithAttachmentBehavior`, `Load_RejectsChildAnchorOffsetCombinedWithInstanceTransformOverride`, `Load_AcceptsAnchorOffsetWithoutAttachment_AndAttachmentWithoutAnchorOffset`).

## Outcome
- success: yes (targeted suites green; full-suite deltas limited to pre-existing unrelated fixture failures)
- one resolved root transform, four aligned writers, zero emitter-side anchor re-application.
- duplicate root-offset authoring now fails closed at load.

## Summary Stats
- targeted new tests: 10 passed, 0 failed (`PresenterTransformSingleEntryTests`)
- updated contract tests: `PresenterGroundingAndGlobalEventTests` 28 passed
- Core build: 0 errors
- full PresentationTests: see trace.jsonl `full_suite` entries; known pre-existing failures in CrowdPhysicsArena/MassNavigation/GenreInfo are unrelated fixture noise documented in the task brief.

## Known limitations
- The `Showcase slice` (presenter_blacksmith panel) is deferred to the consolidated follow-up; `mods/showcases/presenter_blacksmith/` and `showcase.registry.json` are untouched.
- Runtime-registered definitions (code-created `PresenterDefinition` instances, not loaded through `PresenterDefinitionConfigLoader`) are not covered by the loader conflict rejection; the closed-set rejection only guards `presenters.json` authoring.
