# Fog Of War Showcase Acceptance

- layers: ground/air/detection with independent resolution
- apertures: cone and line rasterization with vertical and line-of-sight rules
- projection: LiveVisible, Known/LastKnown, HiddenWithSource, and aspect mask
- generator: DenyDominates writes Denied
- detection: true sight reveals detection-layer occupant
- snapshot: capture/diff reports changed cell
- sharing: relationship-gated merge contributes allied explored cells
