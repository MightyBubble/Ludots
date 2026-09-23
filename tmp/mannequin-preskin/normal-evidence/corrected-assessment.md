# Corrected shared-pose result

The explicit normal correction removes the widespread shading mismatch. Remaining differences are sparse surface-boundary pixels. This supports continuing the experiment, but does not establish production visual equivalence.

All results below use the corrected 10K output, 1280x720, identical camera and pose per pair.

| Normalized pose time | Background-mask XOR | Changed pixels | Pixels with RGB error >8 | Maximum RGB error | RGB mean error | >8 within 2px of an image edge | Largest >8 connected component |
|---|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0 | 420 | 373 (0.04047%) | 224 | 0.04622 | 369/373 (98.93%) | 5 pixels |
| 0.25 | 0 | 153 | 124 (0.01345%) | 222 | 0.01349 | 123/124 (99.19%) | 3 pixels |
| 0.5 | 0 | 495 | 447 (0.04850%) | 206 | 0.05652 | 429/447 (95.97%) | 4 pixels |

Image edges here mean the union of grayscale Canny edges from both images, with thresholds 50/100. This is a diagnostic spatial measure, not a proof about which GPU primitive or shadow sample generated a pixel. The background-mask XOR only proves that no background/foreground coverage differs at the sampled resolution. Occlusion boundaries between soldiers remain inside the foreground.

Largest differences occur where red and blue surfaces exchange ownership of a pixel. The three samples have 302, 88 and 352 such red/blue swaps among their >8 pixels. Examples (zero-based coordinates):

- Time 0, (1168,615): baseline RGB (34,153,255), corrected (144,55,31), maximum channel difference 224. Another maximum is at (868,647).
- Time 0.25, (924,405): baseline (34,153,255), corrected (146,55,33), difference 222.
- Time 0.5, (1278,196): baseline (212,86,49), corrected (36,158,255), difference 206; similar swaps occur at (378,224) and (1065,276).

These are surface selection/coverage differences, not just small per-channel rounding and not adequately described as only a shadow brightness shift. The native position path calculates `sum(weight * (bone * position))`; the GPU calculates `(sum(weight * bone)) * position`. These are algebraically equal but have different floating-point association. Tiny position/depth changes can select another surface at an occlusion edge, producing a large color change in one pixel. Sparse 1-5 pixel clusters and unchanged external silhouettes are consistent with this mechanism. The existing color images cannot prove it is the only cause or bound temporal shimmer while units move.

The minimum next numerical check is to compare native `animVertices` and corrected `animNormals` against an explicit float32 evaluation of the shader formula for every loaded vertex, mesh and sampled clip frame, reporting maximum absolute/relative position and normal errors. A depth or flat surface-ID capture at the same pose would then separate coverage changes from lighting/shadow sampling. Those checks have not been run; no visual acceptance threshold has been granted.

The reported corrected rendering experiment measured main GPU 89.31->64.32ms and shadow GPU 47.46->31.96ms, with CPU preskin+upload 0.402ms and zero current-thread managed allocation/Gen0/Gen1 deltas. This is a renderer-only, synchronized, single-pose experiment on the captured GPU. It establishes neither whole-application FPS nor mixed-animation throughput.

## Minimum contract for mixed idle/walk

1. Resolve semantic animator state through the existing animation-profile/source binding before constructing a pose key. Geometry identity must include model/skin identity and content version plus resolved clip and exact sampled frame. Different animation profiles can share geometry only when their resolved source and pose are equivalent. The current adapter resolves the primary state and normalized time; it does not implement secondary-state blending. Preserve that current behavior explicitly rather than silently treating transition data as a new blend.
2. Each distinct pose needed by the frame must have the correct immutable posed-vertex/normal slice until all its main and shadow draws finish. A cached model has one mutable mesh VBO set. Calling native skinning twice on it while preparing all poses makes the later pose overwrite the earlier pose. The one-pose probe intentionally cannot reveal this defect. A multi-pose implementation needs distinct owned pose storage or an explicitly ordered pose-and-draw lifecycle that also preserves both passes; it cannot reuse a single prepared buffer for concurrent idle and walk instances.
3. Partition instances by actual resolved pose while preserving their IDs, transforms, colors and materials. Main and shadow must consume the same pose/version and instance membership. Reorder input instances and swap which group is idle/walking to prove that the last submitted group does not determine everyone else's animation. Mixed static/rigid/skinned mesh ownership also needs an explicit supported contract; this probe validates only fully skinned mannequin meshes.
4. Configure and preallocate pose, vertex/normal, instance and batch capacities. Exceeding any supported bound must produce an explicit error. No lazy unbounded cache growth, per-frame resizing, silent eviction of in-use pose data, stale-pose draw, or implicit route fallback. Resource release/hot reload must invalidate pose keys and dispose owned GPU buffers after use.
5. Measure costs against distinct pose count K, not only unit count N. This mannequin has 8954 loaded vertices across six meshes. The current corrected experiment uploads 322344 geometry bytes per pose and takes about 0.402ms CPU+upload for K=1. If split into separate pose batches with the same six meshes, two poses can require 12 draws per pass; K poses can require 6K per pass. Different phases of one walk clip can produce many more than two poses. Linear extrapolations are planning bounds, not measured performance.

Minimum acceptance scenarios: 50/50 idle/walk with two distinct poses; same walk clip with at least three phases including loop wrap; shuffled input and reversed pose-group order; stop/start one group while another continues; alternating material/tint assignments; both main and shadow checked; configured capacity and capacity+1; warmed per-frame allocation measured. If transition blending is advertised, its source clips, sample times and blend weight must participate in geometry identity and have a separate supported test.
