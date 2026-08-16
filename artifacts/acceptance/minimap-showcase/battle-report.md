# Scenario: minimap-showcase

## Header
- build: GasTests / MinimapShowcase_WritesMarkerOnlyAcceptanceArtifacts
- map: minimap_showcase
- source: core MinimapMarkerBuffer
- screenshots: `screens/001_rts_marker_overview.svg`, `screens/002_camera_marker_window.svg`, `screens/003_camera_marker_zoom.svg`

## Timeline
[T+001] RTS preset draws all authored presenter markers directly from the core marker buffer.
[T+002] Follow-camera preset keeps the camera target centered and clips markers outside the local window.
[T+003] Zooming in increases screen-space distance between the same authored markers.

## Outcome
- result: success
- failure_branch: minimap marker projection failed, camera preset did not clip to local markers, or render hot path exceeded allocation budget
- rts_visible: 20/20
- camera_visible: 12/20
- zoom_visible: 6/20

## Summary Stats
- marker_pool: 20
- median_tick_ms: 0.685
- max_tick_ms: 0.918
