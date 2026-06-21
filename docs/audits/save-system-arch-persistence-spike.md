# Save System Arch.Persistence Spike

Issue: #293 / Epic #292.

This spike records the current behavior of `Arch.Persistence` after wiring it into `Ludots.Core`,
plus the SAVE-2 formatter repair that makes Ludots Core components round-trip through the Core serializer.
It also records the SAVE-3 SaveContext header and compatibility gate now available to later save orchestration work.
It is evidence for SAVE-5 entity identity decisions.

## Reused Infrastructure

- `src/Libraries/Arch.Extended/Arch.Persistence`: `ArchBinarySerializer` whole-World binary serializer.
- `src/Libraries/Arch/src/Arch`: repository-local Arch assembly, used consistently by Core and Arch.Persistence.
- `src/Core/Ludots.Core.csproj`: Core consumes Arch.Persistence through a project reference.
- `src/Core/Config/ComponentRegistry.cs`: SSOT for registered Core component types.
- `src/Core/Registry`: shared string/id registry infrastructure, now exposing deterministic mapping snapshots.
- `src/Core/Persistence`: Core-owned formatter registry, unmanaged raw-bytes component formatter, SaveContext header capture, and fail-fast compatibility validation.
- `src/Tests/PersistenceTests`: NUnit characterization tests.

## Characterization Results

Verified by `dotnet test src/Tests/PersistenceTests/PersistenceTests.csproj`.

| Case | Result | Test |
|------|--------|------|
| Empty World | Round-trips as zero entities | `BinaryWorldRoundTripPreservesEmptyWorldShape` |
| Simple blittable components (`WorldPositionCm`, `FacingDirection`) | Component values round-trip when the restored entity is located through a query | `BinaryWorldRoundTripPreservesSimpleBlittableComponents` |
| Managed `Name.Value` string | Round-trips, including Chinese text | `BinaryWorldRoundTripPreservesManagedNameString` |
| Restored entity alive check | Query can enumerate the restored entity and `World.IsAlive(entity)` accepts the enumerated entity | `BinaryWorldRoundTripPreservesRestoredEntityAliveMetadata` |
| Restored entity version metadata | Entity id reuse increments `Entity.Version`, and the version now round-trips through the vendored Arch.Persistence serializer | `BinaryWorldRoundTripPreservesEntityVersionMetadata` |
| `AttributeBuffer` fixed storage with raw Arch serializer | Current serializer fails or does not preserve non-default fixed-buffer values | `BinaryWorldRoundTripCurrentlyFailsOrCorruptsAttributeBufferFixedStorage` |
| `GameplayTagContainer` fixed storage with raw Arch serializer | Current serializer fails or does not preserve non-default fixed-buffer values | `BinaryWorldRoundTripCurrentlyFailsOrCorruptsGameplayTagContainerFixedStorage` |
| `BlackboardEntityBuffer` entity-ref fixed storage with raw Arch serializer | Current serializer fails or does not preserve non-default entity-ref entries | `BinaryWorldRoundTripCurrentlyFailsOrCorruptsEntityRefFixedStorage` |
| `AttributeBuffer` through Core serializer | Non-default fixed-buffer values round-trip | `CoreBinarySerializerPreservesAttributeBufferFixedStorage` |
| `GameplayTagContainer` through Core serializer | Sparse high tag bits round-trip | `CoreBinarySerializerPreservesGameplayTagContainerFixedStorage` |
| `BlackboardEntityBuffer` through Core serializer | Id/Version lanes round-trip for entity-ref entries | `CoreBinarySerializerPreservesEntityRefFixedStorage` |
| Formatter registry coverage | Every `ComponentRegistry` component type has a Core persistence formatter | `CorePersistenceFormatterRegistryCoversComponentRegistryTypes` |
| Unsupported persistent component | Core serializer fails fast instead of falling back to contractless MessagePack | `CoreBinarySerializerRejectsPersistedComponentsWithoutLudotsFormatter` |
| Self-captured SaveContext header | Validates against the same initialized engine | `CapturedSaveContextHeaderValidatesAgainstSameEngine` |
| `schemaVersion` tampering | Fails fast with expected and actual values | `SchemaVersionMismatchFailsFastWithExpectedAndActualValues` |
| `modSetHash` tampering | Fails fast and tells the caller to load the corresponding mod set and map | `ModSetHashMismatchFailsFastWithCorrespondingModAndMapHint` |
| `registryFingerprint` tampering | Fails fast before load | `RegistryFingerprintMismatchFailsFast` |
| Registry fingerprint ordering | Stable across dictionary and mapping enumeration order | `RegistryFingerprintIsStableAcrossDictionaryAndMappingEnumerationOrder` |

## SAVE-2 Formatter Result

- `UnmanagedComponentFormatter<T>` writes the full unmanaged struct as MessagePack binary bytes.
- Entity-ref buffers are covered by the same unmanaged formatter path, including runtime-only fixed-buffer components discovered from loaded Ludots assemblies.
- `NameFormatter` and `MapEntityFormatter` handle managed string-backed components explicitly.
- `LudotsCorePersistenceFormatters.CreateBinarySerializer()` is the Core entry for Arch binary serialization with Ludots formatters installed.
- `LudotsBinaryWorldSerializer` audits every includable entity component before serialization. If any component lacks a Ludots formatter, save fails with `SaveContextException`; there is no contractless fallback path for persisted components.
- `ComponentRegistry.GetRegisteredComponentTypes()` exposes the authoring registry component types in deterministic name order; tests use it as the authoring coverage guard, while world-level formatter audit covers runtime-added components.

## SAVE-3 Header Result

- `SaveContextHeader` captures `schemaVersion`, `modSetHash`, `registryFingerprint`, `mapId`, `tick`, `createdUtc`, and the Core assembly version.
- `SaveContextFactory.Capture(engine)` reuses the initialized `GameEngine`, `ModLoader.LoadedModIds` / `ResolvedModLoadPlan`, `GameSession.CurrentTick`, and `CurrentMapSession.MapId`.
- `SaveContextValidator.Validate(header, engine)` is fail-fast: schema, mod set, and registry fingerprint mismatches throw `SaveContextException`; there is no migration or fallback path.
- `RegistryMapping` and `SnapshotMappings()` expose deterministic name/id snapshots from existing registries instead of introducing a parallel registry.
- `SaveContextHashes.ComputeRegistryFingerprint(...)` sorts both registry names and mappings before hashing so hash output is stable across dictionary enumeration order.

## Gaps for SAVE-5

- Arch.Persistence entity identity is now patched and covered for Ludots save/load.
- Restored entities are enumerable, component-readable, and accepted by `World.IsAlive`.
- Entity refs are normalized to the restored world id and validated against included entities after deserialize.

## Build/Dependency Notes

- `Arch.Persistence.csproj` now references the repository-local `Arch.csproj` instead of the NuGet `Arch` package to avoid mixed Arch assemblies in Core.
- The vendored `Arch.Persistence/Binary.cs` `EntitySlotFormatter` is intentionally patched to write and read `EntityData.Version`. Without this patch, restored entity versions collapse and entity-ref validity breaks after id reuse.
- Upstream Arch.Persistence re-vendor work must preserve or reapply both local changes above before SAVE-5 entity-ref tests are allowed to pass.
- No storage container, participant registry, or snapshot service was introduced in this spike; those belong to later SAVE issues.
