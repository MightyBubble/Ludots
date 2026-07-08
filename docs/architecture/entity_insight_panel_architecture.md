# Entity Insight Panel Architecture

## Current Boundary

Entity insight panels read explicit entity collections and sampled panel state. They do not own
command authority, input arbitration, or a private entity-group store.

Current data sources:

- Entity identity and template data from ECS components.
- Query / display groups from `EntityCollectionStore`.
- The default player command group from `EntityCollectionKeys.CommandSource`.
- Sampled insight state from `EntityInfoPanelService`.
- Text, icon, and profile data from catalog assets.

User-facing "selection" text may remain in a panel label when it describes the default command
group to the player. Implementation code must still name the data source as a command source or
explicit entity collection.

## Reuse Checklist

- Registry: template key, attribute, tag, text, and profile registries.
- Pipeline: runtime spawn writes stable template keys for profile lookup.
- Pipeline: `EntityCollectionStore` carries query and display collections.
- Pipeline: `EntityInfoPanelService` samples ECS / GAS data into fixed slots before UI render.
- Mod: `mods/capabilities/entityinfo/EntityInfoPanelsMod/` owns reusable insight sampling and UI
  composition.
- Showcase: `mods/showcases/info_panels/GenreInfoShowcaseMod/` owns theme examples and playable
  acceptance only.

## SSOT Rules

- Do not cache a second authoritative current-actor list in UI controllers.
- Do not branch panel data models by renderer backend.
- Do not fallback from a missing collection to an implicit selected-provider path.
- Do not put business vocabulary into Core panel infrastructure.
- Do not use hardcoded entity names, UI ids, or text strings to choose panel semantics.

## Runtime Flow

1. Runtime spawn materializes entities and writes stable template keys.
2. Input or showcase setup publishes explicit entity collections.
3. `EntityInfoPanelService` samples a bounded collection window into reusable state slots.
4. The UI composer renders sampled state and localized text tokens.
5. Renderer adapters display the same retained scene for Raylib, Skia, or Web UI surfaces.

## Performance Boundary

The hot path is collection lookup plus fixed-slot sampling. UI tree rebuilding can allocate when
state changes, but authority and sampled panel data must remain bounded and explicit.

Current reference documents:

- `docs/architecture/entity_collection_query_infrastructure.md`
- `docs/architecture/webui_dataplane_architecture.md`
- `docs/audits/rfc_0065_pr581_workflow_closeout.md`
