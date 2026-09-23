# Shared mannequin geometry probe

This temporary renderer experiment compares GPU texture skinning against native CPU skinning once for the entire shared pose, followed by instanced drawing of the resulting vertices. It uses `mannequin_large_walk.glb`, six fully skinned meshes, one animation pose, semantic state 42 mapped to source clip 3. Both main and shadow passes use the same posed vertices. It does not run MassNavigation, Presenter, HUD, effects, terrain, culling, or minimap.

The current probe explicitly corrects native normals after `UpdateModelAnimation`, using the shader's weighted 3x3 transform without translation and uploading normal VBO 2 again. Initial uncorrected results remain under `results-{population}`. New output uses `results-{population}-normal-fix`. Evidence, native source snapshots, DLL hash/disassembly and a CPU-only correction check are under `normal-evidence/`. Corrected GPU/pixel results are pending; the previous visible differences are not accepted as tolerable noise.

The private renderer is generated from the current worktree renderer by `prepare.py`. `snapshot-manifest.json` records source/runtime hashes. All files are under this directory. There are no project references, and building this project does not rebuild product dependencies.

## Build

Run from `C:\001_AI\Ludots-gpu-10k-opt`:

```powershell
python tmp/mannequin-preskin/prepare.py
dotnet build tmp/mannequin-preskin/MannequinPreskin.csproj -c Release --nologo
```

Preparation copies the existing `tmp/mannequin-ab/bin/Release/net9.0` runtime and shaders into a private snapshot. Do not rerun preparation during a measurement sequence: the snapshot should remain identical across 1K, 5K and 10K. Initial preparation and build completed with zero warnings/errors. No GPU execution was performed by the author of this probe.

## Run sequentially

Only one GPU benchmark should run at a time. These commands open a window but do not use, stop or modify any Agent Bridge port.

```powershell
dotnet tmp/mannequin-preskin/bin/Release/net9.0/Ludots.Adapter.Raylib.Tests.dll 1000
python tmp/mannequin-preskin/summarize.py tmp/mannequin-preskin/results-1000-normal-fix
dotnet tmp/mannequin-preskin/bin/Release/net9.0/Ludots.Adapter.Raylib.Tests.dll 5000
python tmp/mannequin-preskin/summarize.py tmp/mannequin-preskin/results-5000-normal-fix
dotnet tmp/mannequin-preskin/bin/Release/net9.0/Ludots.Adapter.Raylib.Tests.dll 10000
python tmp/mannequin-preskin/summarize.py tmp/mannequin-preskin/results-10000-normal-fix
```

Arguments are population, frame count (default 192), warmup frame count (default 64). Population must be 1000, 5000 or 10000. Frame and warmup counts must be divisible by four, warmup at least four, and total greater than warmup. Output directories are overwritten when rerunning the same population; preserve an earlier result directory before repeating if needed.

## Measurement contract

- Schedule is ABBA. Frames 0/1 have identical animation time; frames 2/3 have the next identical time. A uses the original GPU texture skinning. B calls native `UpdateModelAnimation` exactly once and supplies the existing identity-skin sentinel in both passes. Shared bone-uniform optimization is disabled in both modes.
- Baseline restoration uploads original `mesh.vertices` to VBO 0 and `mesh.normals` to VBO 2. Its submission/drain time, bytes and allocation are reported separately. Restoration and its GPU drain happen before baseline timing. Original arrays are checked against startup copies after measurement and after screenshots.
- The experiment synchronizes GPU completion before each frame and after the main pass. `total_sync_ms` includes submission, pose preparation, GPU completion and `EndDrawing`. It is renderer latency under synchronization, not production frame throughput or whole-application FPS.
- Native GL timestamps separately cover pose preparation, shadow draws and main draws. These are GPU timeline intervals; CPU command starvation can contribute gaps. Shadow setup/clear and camera/lighting setup are outside pass intervals but inside total. `glFinish` waits prevent baseline restoration from leaking into the measured pass.
- `preskin_cpu_upload_ms` includes the native function, explicit normal correction and its second normal upload. The correction subset is also recorded as `normal_correction_cpu_upload_ms`. All of this is included in preparation and total. The preparation GPU interval includes vertex/normal uploads, the correction upload, and palette/instance texture uploads.
- Both modes retain the existing pose palette and instance-table creation/write/upload workload. The experiment therefore tests the gain from sharing posed geometry without additionally removing these resources. `geometry_upload_bytes` includes the native position/normal upload (214896 bytes) plus normal correction upload (107448 bytes), totaling 322344 bytes per preskin frame. `normal_correction_upload_bytes` records the correction subset. Texture bytes report palette and instance-table upload. Native instance-matrix buffer uploads are not separately instrumented.
- Main/shadow instance counts mean model instances. Mesh-instance counts count every mesh draw instance, so each is six times population. Each pass must produce six draws and the exact configured population. One model/material batch, one pose and fixed instance capacity are enforced explicitly; excess batches, poses or instances throw.
- Current-thread managed allocated bytes and Gen0/Gen1 deltas are captured per timed frame. Startup/warmup allocation is visible in CSV but excluded by `summarize.py`. This does not measure native allocations or other managed threads.

## Visual evidence

After measurement, three matching-pose pairs (normalized times 0, 0.25 and 0.5) are captured with the identical view. Readback occurs after `EndMode3D`, `glFinish`, and before `EndDrawing`; it uses the framebuffer API instead of desktop-sized screenshots. Metadata records GL vendor/renderer/version, screen and framebuffer dimensions, and initial viewport.

`pixels.json` reports exact equality, changed-pixel fraction, error above 8/255, maximum RGB error, overall RGB mean error, and foreground RGB mean error. Each pair has an amplified difference PNG. An empty foreground throws. Pixel equivalence has not yet been established: CPU and GPU skinning may differ, and any differences must be inspected before promoting this path into product code. The current camera shows central units; all units are submitted, and there is no frustum culling.

Output: `frames.csv`, `metadata.json`, `pixels.json`, six pose PNGs, three difference PNGs, and `summary.json` after the summarizer. Summary deltas are paired baseline minus preskin, so positive means time saved.
