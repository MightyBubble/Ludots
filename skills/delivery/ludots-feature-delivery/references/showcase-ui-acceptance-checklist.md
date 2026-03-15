# Showcase UI Acceptance Checklist

Use this checklist when a Ludots feature changes retained UI, overlay, HUD, showcase presentation, or cross-mod UI takeover behavior.

## 1. Surface Ownership

- Name the owner of each visible surface (`RetainedUi`, `ScreenOverlay`, `WorldHud`, or adapter-specific overlay).
- Do not let multiple controllers compete for the same surface through direct `MountScene(...)` or ad-hoc suppression unless the takeover contract is explicit.
- If a showcase temporarily takes over an existing surface, define acquire, restore, and release points for `MapLoaded`, `MapResumed`, and `MapUnloaded`.
- Repeated collisions are an infrastructure problem; do not normalize one-off suppression flags as the long-term design.

## 2. First-Frame Readability

- The first visible frame must already be readable.
- Default anchors and sizes must avoid clipping at supported resolutions.
- No flashing debug title, placeholder text, or repeated remount flicker is acceptable as a "good enough" first frame.
- Container children must stay inside the intended bounds unless overflow is an explicit design choice.

## 3. Adapter-Visible Acceptance

- Engine-side state is not enough; validate what the player actually sees through the adapter.
- Capture at least one concrete UI-visible artifact when presentation behavior changes.
- Record the exact launch command, mod, and scenario that produced the visible result.

## 4. Interaction Safety

- UI must not break unrelated world interactions unless that lockout is part of the design.
- Verify entity selection, camera movement, and world click behavior while the panel is visible.
- Hidden or closed panels must release focus, takeover state, and transient suppression cleanly.

## 5. Performance Boundary

- Do not remount the full scene every frame.
- Do not allocate `List<T>`, LINQ enumerators, or transient strings in the panel hot path.
- Use SoA buffers, cached queries, and bounded dirty-refresh behavior for large multi-entity displays.
- Multiple visible instances must scale by data change, not by full-host reconstruction.

## 6. Required Evidence

- Headless acceptance evidence for gameplay or logic changes.
- Adapter-visible evidence for UI or presentation changes.
- Takeover / restore evidence for `MapLoaded`, `MapResumed`, and `MapUnloaded` when a surface owner changes.
- A short note describing residual risk if a temporary mitigation is still in place.
