# Siege Relation/GAS Crosscheck Audit

Date: 2026-04-06

## 1. Scope

This audit recalibrates the siege-infrastructure design after PR86 mainline integration and checks whether the current RFC direction is supported by independent model review and Ludots repo facts.

The target is not a prototype port. The target is:

- one formal Ludots siege mod
- the minimum missing shared infrastructure required to support it cleanly

## 2. Provider Execution Status

| Provider | Status | Audit disposition |
|---|---|---|
| Claude | Valid | Counted as usable challenge evidence |
| Gemini | Blocked | Not counted as convergence evidence |

Gemini was blocked in the current environment because:

- `C:\Users\sietg\.gemini\settings.json` does not exist
- no `GEMINI_API_KEY` / `GOOGLE_API_KEY` / `GOOGLE_GENAI_API_KEY` environment variable is present
- direct `npx @google/gemini-cli` execution requires one of those auth paths before it will answer

Therefore the older repo artifact that treated Gemini as completed must not be used as authoritative convergence evidence.

## 3. Repo-Verified Constraints

The following facts are treated as hard constraints:

- Ludots already has a relation spine through `ChildOf` and `RelationOps`; this is the existing parent-child truth and must be reused.
- Ludots already has `FacingDirection` and `Rotation2D`; siege design must reuse current facing/orientation primitives instead of inventing a parallel rotation model.
- Ludots already has `Navigation2DRuntime`; siege design must reuse the current graph/navigation runtime instead of rebuilding pathing.
- PR86 `RtsRelationRuntimeSystem` already proves that relation-based attachment is not hypothetical. It already covers builder attach, morph attach, ungarrison, child snap-to-host, and selection sync, but it remains mod-local and tag-driven.
- The external prototype stores many siege rules as ad hoc JS object state:
  - `garrison.js`: rotated entry zones, `garrisonedUnits`, capacity checks
  - `siegeLadder.js`: `attachedToBuildingId`, `attachedSlotIndex`, `ladderState`, `garrisonedUnits`, `deployTimer`
  - `wallClimb.js`: fixed climb slots, wait queues, wall occupancy, ownership transfer

## 4. Claude Crosscheck

Claude independently converged on four points that align with the current RFC:

- topology, occupancy, and route state must stay outside GAS
- tag-rule mapping is stronger than inventing siege-specific preset types
- AI dashboard should be generic inspection infrastructure, not prototype-only UI
- authoritative wall/gate/tunnel/trench state must not be encoded as tags

Claude also suggested a more conservative promotion boundary:

- promote only a small slot/occupancy layer first
- leave everything else mod-local until a second consumer appears

## 5. Ludots Adoption Decision

Claude is directionally right on the GAS boundary but too conservative on the shared-infra cut for Ludots specifically.

That conservatism would make sense if Ludots had no prior attachment runtime. It no longer holds after PR86, because Ludots now already has multiple concrete attachment semantics living on the same relation spine:

- builder attach
- morph attach
- ungarrison/detach
- host-child transform sync

Once relation-based attachment is already serving multiple semantics, the next missing piece is not “another mod-local slot record”. The next missing piece is a shared semantic layer above the existing relation spine.

## 6. Final Architecture Verdict

### 6.1 Core Shared Infra

Promote these into shared infrastructure now:

- `AttachmentBinding`
- `AttachmentSlotBuffer`
- `AttachmentOccupancyBuffer`
- `AttachmentSemantic`
- `AttachmentReservation`
- `OrientationPolicy`
- slot-local / target-facing resolver utilities
- topology-neutral attachment validation and reservation helpers

Reason:

- these are already cross-mechanic needs inside siege
- relation semantics, slot identity, occupancy, and orientation are not siege-only concepts
- PR86 already proves Ludots has more than one consumer pattern for relation-based attachment

### 6.2 Reusable Capability Mods

Keep these as reusable capability layers above Core:

- `FortificationTopologyMod`
  - wall segment, face, gate side, tunnel node, trench lane descriptors
  - topology queries
  - route invalidation bridge
- `SiegeAuthoringBridgeMod`
  - Base44/Web editor DTO bridge
  - Ludots-native import/export
- `RuntimeInspectorSiegeMod`
  - attachment/topology/occupancy/orientation snapshots
  - AI dashboard and developer-side inspector feeds

### 6.3 Siege Mod

Keep these strictly in the siege gameplay mod:

- climb rules and timings
- ladder attach/deploy cadence
- gate capture timings and failure rules
- trench fill cost and interruption rules
- tunnel usage rules
- siege-engine interaction matrix

## 7. GAS Boundary

Use GAS for:

- permissions
- state windows
- costs
- timers
- buffs/debuffs
- triggering relation or occupancy mutations after structured validation has already succeeded

Do not use GAS or tags as the source of truth for:

- wall adjacency
- gate inner/outer topology
- tunnel connectivity
- trench geometry state
- slot occupancy truth
- attachment anchor identity

## 8. Adjustment To RFC-0060

The RFC direction remains correct.

The main adjustment after Claude crosscheck is not a reversal of the architecture. The adjustment is sharper wording around observability:

- AI dashboard should be backed by a neutral inspection-provider contract
- attachment, topology, GAS, and planner state should expose read-only snapshots through that contract
- dashboard UI remains a consumer, never the owner of gameplay truth

## 9. Audit Conclusion

The current Ludots siege design should proceed with this split:

1. Core shared infra for relation semantics, slot/occupancy, reservation, and orientation
2. Reusable fortification topology and inspection capability mods
3. Siege-specific rule/state machines kept in the formal siege mod

The design must explicitly reject these anti-patterns:

- topology-as-tags
- presentation-anchor-as-gameplay-anchor
- siege-specific GAS preset proliferation
- rebuilding navigation or relation storage that Ludots already has

Gemini remains a real blocker, not a soft warning. Until Gemini auth is configured in the current environment, no document should claim Claude+Gemini convergence.
