from pathlib import Path
import hashlib
import json
import shutil

here = Path(__file__).resolve().parent
root = here.parent.parent
source = root / 'src/Client/Ludots.Raylib.Render/Rendering/RaylibGpuSkinnedBatchRenderer.cs'
runtime = root / 'tmp/mannequin-ab/bin/Release/net9.0'
private = here / 'runtime-snapshot'
private.mkdir(exist_ok=True)
for entry in runtime.iterdir():
    if entry.name == 'runtimes':
        shutil.copytree(entry, private / entry.name, dirs_exist_ok=True)
    elif entry.suffix in ('.dll', '.vs', '.fs', '.inc', '.json') and not entry.name.startswith('Ludots.Adapter.Raylib.Tests'):
        shutil.copy2(entry, private / entry.name)

text = source.read_text(encoding='utf-8-sig')
def replace(old, new, count=1):
    global text
    if text.count(old) != count:
        raise RuntimeError(f'Expected {count} occurrences, found {text.count(old)}: {old[:150]}')
    text = text.replace(old, new)

replace('    public enum RaylibGpuSkinnedSubmitOutcome : byte\n    {\n        Unsupported = 0,\n        Submitted = 1,\n        InFlight = 2,\n    }\n\n', '')
text = text.replace('RaylibGpuSkinnedBatchRenderer', 'CpuPreskinProbeRenderer')
replace('private readonly int _maxModelInstancesPerDraw;', '''private readonly int _maxModelInstancesPerDraw;
        internal bool EnableCpuPreskin { get; set; }
        public int LastPreskinCalls { get; private set; }
        public double LastPreskinCpuUploadMs { get; private set; }
        public long LastPreskinUploadBytes { get; private set; }
        public double LastNormalCorrectionCpuUploadMs { get; private set; }
        public long LastNormalCorrectionUploadBytes { get; private set; }
        public int LastShadowDraws { get; private set; }
        public int LastShadowInstances { get; private set; }
        public int LastShadowMeshInstances { get; private set; }
        public int LastMainMeshInstances { get; private set; }

        public void PrepareFrameGeometry()
        {
            if (_activeGpuSkinnedInstanceBatches.Count != 1 || _dirtyPoseRows.Count != 1)
                throw new InvalidOperationException("Probe requires exactly one model/material batch and one pose.");
            if (_activeGpuSkinnedInstanceBatches[0].Count != _maxModelInstancesPerDraw)
                throw new InvalidOperationException("Probe population must equal its configured capacity.");
            if (EnableSharedPoseUniforms)
                throw new InvalidOperationException("Shared uniform mode is outside this experiment.");
            BuildAndUploadPoseTextures();
        }''')
replace('private readonly Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch> _gpuSkinnedInstanceBatches = new();', 'private readonly Dictionary<GpuSkinnedInstanceBatchKey, GpuSkinnedInstanceBatch> _gpuSkinnedInstanceBatches = new(1);')
replace('new(64);', 'new(1);')
replace('private readonly Dictionary<(int MeshAssetId, int ClipIndex, int FrameIndex), int> _poseRowByKey = new();', 'private readonly Dictionary<(int MeshAssetId, int ClipIndex, int FrameIndex), int> _poseRowByKey = new(1);')
replace('private readonly List<(int PoseRow, GpuSkinnedInstanceBatch Batch, int ClipIndex, int FrameIndex)> _dirtyPoseRows = new();', 'private readonly List<(int PoseRow, GpuSkinnedInstanceBatch Batch, int ClipIndex, int FrameIndex)> _dirtyPoseRows = new(1);')
replace('            LastInstances = 0;', '''            LastPreskinCalls = 0;
            LastPreskinCpuUploadMs = 0;
            LastPreskinUploadBytes = 0;
            LastNormalCorrectionCpuUploadMs = 0;
            LastNormalCorrectionUploadBytes = 0;
            LastShadowDraws = 0;
            LastShadowInstances = 0;
            LastShadowMeshInstances = 0;
            LastMainMeshInstances = 0;
            LastInstances = 0;''')
replace('                batch = new GpuSkinnedInstanceBatch(key);', '''                if (_gpuSkinnedInstanceBatches.Count != 0)
                    throw new InvalidOperationException("Probe batch capacity is one.");
                batch = new GpuSkinnedInstanceBatch(key, _maxModelInstancesPerDraw);''')
replace('                _posePalette ??= new RaylibPoseTexturePalette();', '''                if (_poseRowByKey.Count != 0)
                    throw new InvalidOperationException("Probe pose capacity is one.");
                _posePalette ??= new RaylibPoseTexturePalette(16, _maxModelInstancesPerDraw);''')
replace('                Rl.UpdateModelAnimationBones(model, anim, frameIndex);', '''                if (EnableCpuPreskin)
                {
                    long preskinStart = Stopwatch.GetTimestamp();
                    Rl.UpdateModelAnimation(model, anim, frameIndex);
                    long normalStart = Stopwatch.GetTimestamp();
                    LastNormalCorrectionUploadBytes += NormalCorrection.Apply(model);
                    LastNormalCorrectionCpuUploadMs += Stopwatch.GetElapsedTime(normalStart).TotalMilliseconds;
                    LastPreskinUploadBytes += LastNormalCorrectionUploadBytes;
                    LastPreskinCpuUploadMs += Stopwatch.GetElapsedTime(preskinStart).TotalMilliseconds;
                    LastPreskinCalls++;
                    for (int meshIndex = 0; meshIndex < model.meshCount; meshIndex++)
                        LastPreskinUploadBytes += (long)model.meshes[meshIndex].vertexCount * 6 * sizeof(float);
                }
                else Rl.UpdateModelAnimationBones(model, anim, frameIndex);''')
replace('                DrawBatchShadow(batch, shadow);', '                LastShadowInstances += batch.Count;\n                DrawBatchShadow(batch, shadow);')
replace('float rigidBoneIndex = batch.MeshRigidBoneIndices.Span[meshIndex];', 'float rigidBoneIndex = EnableCpuPreskin ? RaylibGltfMeshSkinBindings.StaticMesh : batch.MeshRigidBoneIndices.Span[meshIndex];')
replace('                                batch.MeshRigidBoneIndices.Span[meshIndex],', '                                EnableCpuPreskin ? RaylibGltfMeshSkinBindings.StaticMesh : batch.MeshRigidBoneIndices.Span[meshIndex],')
replace('                                drawCalls++;', '                                drawCalls++;\n                                LastMainMeshInstances += chunkCount;')
replace('                                EnableSharedPoseUniforms && _dirtyPoseRows.Count == 1);', '                                EnableSharedPoseUniforms && _dirtyPoseRows.Count == 1);\n                            LastShadowDraws++;\n                            LastShadowMeshInstances += chunkCount;')
replace('''                    Array.Resize(ref Transforms, Transforms.Length * 2);
                    Array.Resize(ref PoseRows, PoseRows.Length * 2);
                    Array.Resize(ref Tints, Tints.Length * 2);''', '''                    throw new InvalidOperationException("Probe instance capacity exceeded.");''')
(here / 'CpuPreskinProbeRenderer.cs').write_text(text, encoding='utf-8')
manifest = {'renderer_source': str(source), 'renderer_sha256': hashlib.sha256(source.read_bytes()).hexdigest(),
            'snapshot_source': str(runtime), 'files': {str(p.relative_to(private)): hashlib.sha256(p.read_bytes()).hexdigest() for p in private.rglob('*') if p.is_file()}}
(here / 'snapshot-manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Prepared private renderer and {len(manifest["files"])} runtime files; no GPU work performed.')
