# Spatial Geometry SSOT

Formal source: `gitbook/architecture/spatial-geometry-ssot.md`.

This companion path exists because older issue references pointed to `docs/architecture/spatial_geometry_ssot.md`. The authoritative rules are maintained in GitBook:

- authoring SSOT: `ManifestationObstacleIntent2D`, `ManifestationObstaclePolygon2D`, `ObstacleGeometryProfile2D`
- runtime sink: `Collider2D` / `CompoundCollider2D`, `NavObstacle2D` / `NavCompoundObstacle2D`
- static cache dirty entry: `ManifestationObstacleBridge2DDirty`, `Physics2DStaticBodyDirty`
- broadphase lane split: dynamic descriptors rebuild per step, static descriptors rebuild only on dirty/static body version changes
