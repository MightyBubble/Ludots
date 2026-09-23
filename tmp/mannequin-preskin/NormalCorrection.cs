using Raylib_cs;
using Rl = Raylib_cs.Raylib;

internal static unsafe class NormalCorrection
{
    public static long Apply(Model model)
    {
        long uploadedBytes = 0;
        for (int index = 0; index < model.meshCount; index++)
        {
            Mesh mesh = model.meshes[index];
            if (mesh.normals == null || mesh.animNormals == null || mesh.boneMatrices == null ||
                mesh.boneIds == null || mesh.boneWeights == null || mesh.vboId == null || mesh.vboId[2] == 0)
                throw new InvalidOperationException("Normal correction requires complete skin buffers and a normal VBO.");
            Recalculate(mesh);
            int bytes = checked(mesh.vertexCount * 3 * sizeof(float));
            Rl.UpdateMeshBuffer(mesh, 2, mesh.animNormals, bytes, 0);
            uploadedBytes += bytes;
        }
        return uploadedBytes;
    }

    internal static void Recalculate(Mesh mesh)
    {
        for (int vertex = 0; vertex < mesh.vertexCount; vertex++)
        {
                float xx = 0, xy = 0, xz = 0, yx = 0, yy = 0, yz = 0, zx = 0, zy = 0, zz = 0;
                for (int influence = 0; influence < 4; influence++)
                {
                    int slot = vertex * 4 + influence;
                    float weight = mesh.boneWeights[slot];
                    if (weight == 0) continue;
                    RaylibMatrix bone = mesh.boneMatrices[mesh.boneIds[slot]];
                    xx += weight * bone.m0; xy += weight * bone.m4; xz += weight * bone.m8;
                    yx += weight * bone.m1; yy += weight * bone.m5; yz += weight * bone.m9;
                    zx += weight * bone.m2; zy += weight * bone.m6; zz += weight * bone.m10;
                }
                int component = vertex * 3;
                float x = mesh.normals[component], y = mesh.normals[component + 1], z = mesh.normals[component + 2];
                mesh.animNormals[component] = xx * x + xy * y + xz * z;
                mesh.animNormals[component + 1] = yx * x + yy * y + yz * z;
                mesh.animNormals[component + 2] = zx * x + zy * y + zz * z;
        }
    }

    public static void SelfCheck()
    {
        float* original = stackalloc float[9] { 0, 1, 0, 1, 0, 0, 2, 0, 0 };
        float* animated = stackalloc float[9];
        byte* ids = stackalloc byte[12] { 0, 0, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0 };
        float* weights = stackalloc float[12] { 1, 0, 0, 0, 0.25f, 0.75f, 0, 0, 0.5f, 0.5f, 0, 0 };
        RaylibMatrix* bones = stackalloc RaylibMatrix[2];
        bones[0] = new RaylibMatrix { m0 = 1, m5 = 1, m10 = 1, m15 = 1, m12 = 2, m13 = -3, m14 = 4 };
        bones[1] = new RaylibMatrix { m4 = -1, m1 = 1, m10 = 1, m15 = 1, m12 = 11, m13 = 13, m14 = 17 };
        var mesh = new Mesh { vertexCount = 3, normals = original, animNormals = animated,
            boneIds = ids, boneWeights = weights, boneMatrices = bones, boneCount = 2 };
        Recalculate(mesh);
        ReadOnlySpan<float> expected = [0, 1, 0, 0.25f, 0.75f, 0, 1, 1, 0];
        if (!new ReadOnlySpan<float>(animated, 9).SequenceEqual(expected))
            throw new InvalidOperationException("Normal correction failed translation exclusion, weighted rotation or unnormalized output checks.");
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int iteration = 0; iteration < 4096; iteration++) Recalculate(mesh);
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (allocated != 0) throw new InvalidOperationException("Normal correction allocated managed memory.");
        Console.WriteLine("{\"status\":\"passed\",\"gpu_initialized\":false,\"translation_excluded\":true,\"weighted_rotation_correct\":true,\"output_preserves_length\":true,\"iterations\":4096,\"allocated_bytes\":0}");
    }
}
