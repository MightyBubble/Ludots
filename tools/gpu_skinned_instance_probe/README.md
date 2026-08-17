# GPU Skinned Instance Probe

Proves **real GPU bone skinning + `DrawMeshInstanced`** (not VAT).

```bash
export LD_LIBRARY_PATH=src/Platforms/Desktop:$LD_LIBRARY_PATH
dotnet run --project tools/gpu_skinned_instance_probe -c Release -- \
  /path/to/retargeted.glb /tmp/out 500 120
```

Requires shaders `skinning_instanced.vs/.fs` beside the binary (copied from `src/Platforms/Desktop/`).
