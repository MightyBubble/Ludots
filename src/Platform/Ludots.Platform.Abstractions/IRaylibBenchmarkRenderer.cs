using System.Numerics;

namespace Ludots.Platform.Abstractions
{
    public interface IRaylibBenchmarkRenderer
    {
        void SetScene(in RaylibBenchmarkScene scene);

        bool SetActiveInstanceCount(int count);

        int GetActiveInstanceCount();

        RaylibBenchmarkStats LastStats { get; }
    }

    public readonly struct RaylibBenchmarkScene
    {
        public RaylibBenchmarkScene(
            bool enabled,
            ReadOnlyMemory<RaylibBenchmarkInstance> instances,
            int initialActiveInstanceCount,
            RaylibBenchmarkMaterialPalette palette,
            RaylibBenchmarkCamera camera,
            string label)
        {
            Enabled = enabled;
            Instances = instances;
            InitialActiveInstanceCount = initialActiveInstanceCount;
            Palette = palette;
            Camera = camera;
            Label = label ?? string.Empty;
        }

        public bool Enabled { get; }

        public ReadOnlyMemory<RaylibBenchmarkInstance> Instances { get; }

        public int InitialActiveInstanceCount { get; }

        public RaylibBenchmarkMaterialPalette Palette { get; }

        public RaylibBenchmarkCamera Camera { get; }

        public string Label { get; }
    }

    public readonly struct RaylibBenchmarkInstance
    {
        public RaylibBenchmarkInstance(
            int meshAssetId,
            int materialId,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            Vector4 color)
        {
            MeshAssetId = meshAssetId;
            MaterialId = materialId;
            Position = position;
            Rotation = rotation;
            Scale = scale;
            Color = color;
        }

        public int MeshAssetId { get; }

        public int MaterialId { get; }

        public Vector3 Position { get; }

        public Quaternion Rotation { get; }

        public Vector3 Scale { get; }

        public Vector4 Color { get; }
    }

    public readonly struct RaylibBenchmarkMaterialPalette
    {
        public RaylibBenchmarkMaterialPalette(Vector4 defaultColor, ReadOnlyMemory<RaylibBenchmarkMaterialColor> colors)
        {
            DefaultColor = defaultColor;
            Colors = colors;
        }

        public Vector4 DefaultColor { get; }

        public ReadOnlyMemory<RaylibBenchmarkMaterialColor> Colors { get; }
    }

    public readonly struct RaylibBenchmarkMaterialColor
    {
        public RaylibBenchmarkMaterialColor(int materialId, Vector4 color)
        {
            MaterialId = materialId;
            Color = color;
        }

        public int MaterialId { get; }

        public Vector4 Color { get; }
    }

    public readonly struct RaylibBenchmarkCamera
    {
        public RaylibBenchmarkCamera(Vector3 position, Vector3 target, float fovY)
        {
            Position = position;
            Target = target;
            FovY = fovY;
        }

        public Vector3 Position { get; }

        public Vector3 Target { get; }

        public float FovY { get; }
    }

    public readonly struct RaylibBenchmarkStats
    {
        public RaylibBenchmarkStats(
            bool active,
            int instanceCount,
            int bucketCount,
            int visibleCount,
            double cpuBuildMs,
            double cpuDrawMs)
        {
            Active = active;
            InstanceCount = instanceCount;
            BucketCount = bucketCount;
            VisibleCount = visibleCount;
            CpuBuildMs = cpuBuildMs;
            CpuDrawMs = cpuDrawMs;
        }

        public bool Active { get; }

        public int InstanceCount { get; }

        public int BucketCount { get; }

        public int VisibleCount { get; }

        public double CpuBuildMs { get; }

        public double CpuDrawMs { get; }
    }
}
