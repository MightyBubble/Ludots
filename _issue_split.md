## Summary

When a small obstacle (e.g. a single RTS building) is placed inside a large flat LayeredSpan tile, the ear-clip-with-holes triangulation emits a final **spanning triangle** — a triangle whose three vertices are all outside the hole but whose interior covers the hole probe. The committed `RemoveTrianglesInsideOwnedHoles` silently **drops** this triangle. That keeps the hole open (the 5 hole contracts pass) but **severs the walkable annulus into 2 connected components**, so the north/south corridor through that tile is broken for any agent that must cross the severed region.

Reproduced by `LayeredSpan_MixedBaselineAndObstacleTile_NorthMarch_StringPullsAroundBuilding` (ArchitectureTests):
`Assert.That(CountTriangleConnectedComponents(obstacleTile), Is.EqualTo(1))` -> `components=2 [n=135@(1211,3348); n=124@(5363,2963)] internalEdges=514`.

## Root cause

- `LayeredSpanTriangulationBuilder.FindAndSpliceBridge` reverses the hole ring to CCW before splicing (correct standard ear-clip-with-holes winding). For a *small* hole in a *large* tile, the resulting annulus ear-clip is poorly conditioned: the bridge is long, and the final 3 remaining vertices surround the hole, producing one spanning triangle.
- `RemoveTrianglesInsideOwnedHoles` detects spanning triangles (`TryHoleRingProbe` + `PointInTriangleStrict`) and drops them. Dropping is the wrong fix: it preserves the hole but severs the annulus.

## Desired fix (design task)

Replace the drop with a **spanning-triangle split**: tessellate `Triangle \ Hole` (the annulus portion of the spanning triangle) by re-introducing the hole's contour vertices as Steiner points, bridging the hole into the triangle, and ear-clipping the combined ring. The result tiles only the annulus, preserving both the hole and single-component connectivity.

## What was tried this pass (GLM)

A `SplitSpanningTriangle` helper was implemented (append hole contour reversed to CCW, bridge `a -> H0`, walk hole as a path, return via a single `a_dup`, ear-clip with `IsLocalEar`). It compiled but **filled the hole instead of the annulus** for `StrictDonut` and `ExtremeWorldOrigins`. Empirical winding tests (CCW vs CW hole walk) both filled the hole; a midpoint-visibility bridge selector (`MidpointInTriangleWide` + `PointInRingStrictScaled` scale=2) did not resolve it. The sub-ear-clip's interior came out as the hole interior, not the annulus — the bridge/ring winding convention for a *triangular* outer (vs the main bridge's full-polygon outer) needs a careful re-derivation against `Orient2`/`area2` (this codebase: `Orient2Sign > 0` = math-CCW; `area2 > 0` = Outer).

The attempt was reverted to the committed baseline (reversal + drop) to keep the 5 hole contracts green. Dead helpers were removed.

## Acceptance (UAT)

```gherkin
Scenario: Small building keeps the tile a single walkable component
  Given a flat LayeredSpan tile with a single small obstacle building placed inside it
  When the tile is baked
  Then the hole for the building stays open (no triangle covers the building footprint)
  And the walkable region outside the building is a single connected component
  And a north-march agent can route through the tile around the building
```

```gherkin
Scenario: Existing hole contracts stay green
  Given the spanning-triangle split fix is applied
  Then Triangulation_StrictDonut_CoversOuterMinusHoleNeverFillsHole passes
  And Triangulation_TwoHoles_Succeeds passes
  And Triangulation_AllContourRingEdgesRemainConstraints passes
  And Triangulation_ExtremeWorldOrigins_NoOverflow passes
  And Triangulation_EachCapacityFailsIndependently_OwnerRequiredClearsOutput passes
  And LayeredSpan_MixedBaselineAndObstacleTile_NorthMarch_StringPullsAroundBuilding passes (components=1)
```

## Related latent bug (separate)

`LawsonFlipChart` uses `EdgeVertex(t0, e + 1, ...)` (committed). For `e = 2`, `e + 1 = 3` hits the `_` arm of `EdgeVertex`'s switch and returns `triC` instead of the correct `triA` (i.e. `(e+1)%3`). The mathematically correct `(e + 1) % 3` fix is load-bearing: applying it breaks `StrictDonut`/`ExtremeWorldOrigins`, meaning committed's off-by-one masks a deeper flip bug. File separately before touching Lawson.

## Scope

- Files: `src/Core/Navigation/NavMesh/LayeredSpan/LayeredSpanTriangulationBuilder.cs`
- Contracts: `src/Tests/ArchitectureTests/TriangleSurfaceVerticalSliceContractTests.cs` (`..._NorthMarch_StringPullsAroundBuilding`)
- No fallback / no silent drop. The split must either succeed or hard-fail the bake.
