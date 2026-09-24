const contracts = Object.freeze({
  SpriteEmitter: Object.freeze({
    primitive: "sprite_burst",
    signature: "sprite_icon"
  }),
  RibbonEmitter: Object.freeze({
    primitive: "ribbon_trail",
    signature: "ribbon_trail"
  }),
  TrackEmitter: Object.freeze({
    primitive: "track_beam",
    signature: "track_beam"
  }),
  RingEmitter: Object.freeze({
    primitive: "ring_pulse",
    signature: "ring_pulse"
  }),
  ModelEmitter: Object.freeze({
    primitive: "model_shard",
    signature: "model_shard"
  })
});

export function requiredEmitterProfile(assetKind, usage) {
  return contracts[assetKind]?.[usage];
}
