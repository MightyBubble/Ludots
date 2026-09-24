# Effekseer concrete emitter assets

All assets in this directory are generated with the pinned Effekseer `1.80.6` editor and runtime
format `1810`. Every `.efkefc` has the strict shape `Root -> one leaf node` and is declared as one
concrete `SpriteEmitter`, `RibbonEmitter`, `TrackEmitter`, `RingEmitter`, or `ModelEmitter`.

Effekseer owns only the particle generation, movement, and drawing inside that leaf. Performer owns
composition, scope propagation, Behavior, Command, Tween, parameters, and lifecycle. There is no
generic VFX or Effect asset kind and no fallback path.

## Asset groups

The five primitive assets prove each concrete Effekseer renderer path independently:

| Primitive owner | Concrete kind | Runtime source |
| --- | --- | --- |
| `primitive_sprite_emitter` | SpriteEmitter | `laser_sprite_impact_flash.efkefc` |
| `primitive_ribbon_emitter` | RibbonEmitter | `laser_ribbon_energy_trail.efkefc` |
| `primitive_track_emitter` | TrackEmitter | `laser_track_main_beam.efkefc` |
| `primitive_ring_emitter` | RingEmitter | `laser_ring_impact_wave.efkefc` |
| `primitive_model_emitter` | ModelEmitter | `laser_model_energy_shard.efkefc` |

Each of the 29 player-facing semantic showcases owns exactly one additional signature asset named
`semantic_<name>_signature.efkefc`. A signature remains one concrete renderer node; it gives the
semantic a unique, auditable visual identity without moving semantic composition into Effekseer.
Sprite signatures use generated bitmap symbols under `textures/signatures/`.

The authoring SSOT is
`../Authoring/raylib-micro-showcases.authoring.json`. Each emitter declaration contains its concrete
kind, owner showcase, usage, normalized project parameters, pinned format, and published SHA-256.
`scripts/generate-raylib-effekseer-projects.mjs` creates the reviewable `.efkproj` sources from those
parameters. Signature project fingerprints are normalized without their node names and must be
unique, so copying a project and only renaming it is rejected.

## Supported commands

Write all project sources, generated textures, validated runtime binaries, hashes, and generated
Performer configuration:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-raylib-effekseer-assets.ps1 -Mode Write
```

Regenerate everything in an isolated staging package and compare it byte-for-byte with the
published source and runtime assets:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/export-raylib-effekseer-assets.ps1 -Mode Check
```

The export command validates the exact renderer node type, format `1810`, relative resource paths,
resource existence, and the forbidden capability set before publishing. Absolute paths, parent
traversal, missing or empty resources, source/output drift, duplicate signatures, and partial
exports are hard failures.
