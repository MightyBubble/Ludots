using Ludots.Client.WebGpu.Runtime;

namespace Ludots.Adapter.WebGpu;

internal static class WebGpuMeshAssetLoader
{
    public static WebGpuMeshFrame Load(Stream stream, string sourceUri, int key)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);

        string extension = Path.GetExtension(sourceUri);
        if (extension.Equals(".glb", StringComparison.OrdinalIgnoreCase))
        {
            return GltfStaticMeshLoader.Load(stream, key);
        }

        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            return ObjStaticMeshLoader.Load(stream, key);
        }

        throw new InvalidDataException(
            $"The registered WebGPU mesh '{sourceUri}' uses unsupported format '{extension}'.");
    }
}
