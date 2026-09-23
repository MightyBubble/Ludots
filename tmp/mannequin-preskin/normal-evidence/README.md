# Native normal mismatch

The shipped private `raylib.dll` SHA-256 is `c8d29fbda31417b900bb0220cfb6c288544264a93764f5ea7cf5727feec76994`.

Upstream raylib 5.5 `rmodels.c:2357` transforms normals with `Vector3Transform`. `raymath.h:857-859` includes matrix translation. The baseline shader uses `mat3(skin) * vertexNormal` at `runtime-snapshot/skinning_instanced_pose_texture.vs:98`, excluding translation. The native formula therefore adds `sum(weight * boneTranslation)` to the transformed normal.

This is also present in the actual DLL, not inferred solely from a version string. `native-exports.txt` resolves `UpdateModelAnimation` RVA `0x8ad50`. Its normal-processing block at virtual address `0x18008B065` reads original normals and bone rows; `shufps ... 0FFh` at `0x18008B0CB`, `0x18008B125`, `0x18008B129` selects row translation elements; `addss` at `0x18008B0F4`, `0x18008B13B`, `0x18008B13F` adds them to the three transformed normal components. `native-normal-disassembly.txt` preserves this block.

The CPU-only check calls the actual DLL's pure math entry point `Vector3Transform`, without creating a window or graphics context. For normal `(0,1,0)`, identity linear transform and translation `(2,-3,4)`, the DLL returns `(2,-2,4)`; the shader returns `(0,1,0)`. Sources, source hashes, DLL hash and check output are in `evidence.json`.

The three uncorrected 10K screenshot pairs have exactly identical background masks: XOR is zero pixels for every pose. Background counts are 68433, 72565 and 68360. Foreground colors become darker in all three channels on average. This supports a lighting mismatch and excludes a visible outer-silhouette change at the sampled resolution; it does not independently prove every internal depth value matches.

Wrong normals also affect shadow reception: `skinning_instanced.fs:141` passes the normal to `SampleShadow`; `shadow_sampling.glsl.inc:21` offsets the sample position along that normal. Thus shade differences can include different self-shadow sampling even if the shadow caster geometry is unchanged.

The temporary correction recomputes `animNormals` from original normals and the weighted upper 3x3 bone matrix, preserving the same operation order as the shader and avoiding normalization before the vertex shader. It then uploads normal VBO 2. Original vertex data, vertex uploads, bone palette, shadow geometry and DLL stay unchanged. The extra computation and upload are included in the measured CPU/GPU preparation scope, with separate correction counters.

The new CPU-only `--normal-self-check` passed translation exclusion, weighted rotation and non-unit output checks; 4096 calls allocated zero managed bytes. `normal-self-check.json` preserves its output. Corrected GPU image equality and timing have not been measured. A same-pose rerun must establish how much of the observed pixel mismatch disappears; any remaining systematic difference still requires investigation.
