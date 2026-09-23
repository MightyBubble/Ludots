using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Ludots.Client.WebGpu.Runtime;

public interface IWebGpuFrameSource
{
    WebGpuCameraFrame Camera { get; }

    WebGpuFrameDiagnostics Diagnostics { get; }

    int WorldBatchCount { get; }

    WebGpuWorldBatchFrame GetWorldBatch(int index);

    WebGpuMeshFrame GroundOverlayMesh { get; }

    ReadOnlySpan<WebGpuScreenInstance> UnderUiScreenInstances { get; }

    /// <summary>Static world-HUD bar instances (pixel size/ratio/colors + anchor index). Movement does not rewrite this lane.</summary>
    ReadOnlySpan<WebGpuWorldBarInstance> WorldHudBarInstances { get; }

    /// <summary>Static world-HUD glyph instances (local pixel geometry/UV/color + anchor index).</summary>
    ReadOnlySpan<WebGpuWorldGlyphInstance> WorldHudGlyphInstances { get; }

    /// <summary>Dynamic world-HUD anchors. Movement updates only this lane; camera projection uses <see cref="Camera"/>.</summary>
    ReadOnlySpan<WebGpuWorldHudAnchor> WorldHudAnchors { get; }

    ReadOnlySpan<WebGpuScreenInstance> TopMostScreenInstances { get; }

    WebGpuFontAtlas TextAtlas { get; }

    ReadOnlySpan<WebGpuGlyphInstance> UnderUiTextGlyphInstances { get; }

    ReadOnlySpan<WebGpuGlyphInstance> TopMostTextGlyphInstances { get; }

    WebGpuHudUploadHint WorldHudUploadHint { get; }

    int TerrainChunkCount { get; }

    WebGpuMeshFrame GetTerrainChunk(int index);

    void Update(float deltaSeconds, uint viewportWidth, uint viewportHeight);

    void ReportWorldHudUploadTiming(
        int barUploadBytes,
        double barUploadMilliseconds,
        int anchorUploadBytes,
        double anchorUploadMilliseconds,
        int glyphUploadBytes,
        double glyphUploadMilliseconds);
}

/// <summary>
/// Coalesced dirty upload ranges for retained world-anchored HUD GPU buffers.
/// Counts of zero mean that lane is unchanged and must not be rewritten.
/// </summary>
public readonly record struct WebGpuHudUploadHint(
    int BarUploadStart,
    int BarUploadCount,
    bool BarFullUpload,
    int AnchorUploadStart,
    int AnchorUploadCount,
    bool AnchorFullUpload,
    int GlyphUploadStart,
    int GlyphUploadCount,
    bool GlyphFullUpload)
{
    public int AnchorUploadBytes => checked(AnchorUploadCount * Unsafe.SizeOf<WebGpuWorldHudAnchor>());
    public int BarUploadBytes => checked(BarUploadCount * Unsafe.SizeOf<WebGpuWorldBarInstance>());
    public int GlyphUploadBytes => checked(GlyphUploadCount * Unsafe.SizeOf<WebGpuWorldGlyphInstance>());
}

public readonly record struct WebGpuFrameDiagnostics(
    double TotalTickMilliseconds,
    double SimulationMilliseconds,
    double PresentationMilliseconds,
    double PerformerEmitMilliseconds,
    double PerformerTransformSyncMilliseconds,
    double PerformerBehaviorMilliseconds,
    double FrameBuildMilliseconds,
    int SingleVisualFastEmitCount,
    WebGpuHudFrameDiagnostics Hud = default);

public readonly record struct WebGpuHudFrameDiagnostics(
    byte BuildPath,
    double HudCpuBuildMilliseconds,
    double BarBuildMilliseconds,
    double TextResolveMilliseconds,
    double GlyphWriteMilliseconds,
    int ActivePerformerCount,
    int BarInstanceCount,
    int BarResidentBytes,
    int BarUploadBytes,
    int LabelAnchorCount,
    int AnchorResidentBytes,
    int AnchorUploadBytes,
    int GlyphCount,
    int GlyphResidentBytes,
    int GlyphUploadBytes,
    int BarInstancesBuilt,
    int TextsResolved,
    int GlyphsWritten,
    int PositionOnlyBarsUpdated,
    int PositionOnlyTextsUpdated,
    int ContentDirtyBars,
    int ContentDirtyTexts,
    int RemovedCount,
    int FullRebuildCount,
    double BarUploadMilliseconds,
    double AnchorUploadMilliseconds,
    double GlyphUploadMilliseconds,
    int UnderUiScreenUploadBytes = 0,
    int UnderUiGlyphUploadBytes = 0,
    double UnderUiScreenUploadMilliseconds = 0d,
    double UnderUiGlyphUploadMilliseconds = 0d);

[StructLayout(LayoutKind.Sequential)]
public readonly struct WebGpuCameraFrame
{
    public WebGpuCameraFrame(Matrix4x4 viewProjection, Vector2 viewportSize)
    {
        ViewProjection = viewProjection;
        ViewportSize = viewportSize;
        Padding = Vector2.Zero;
    }

    public readonly Matrix4x4 ViewProjection;
    public readonly Vector2 ViewportSize;
    public readonly Vector2 Padding;
}

public readonly struct WebGpuMeshFrame
{
    public WebGpuMeshFrame(
        int key,
        int revision,
        ReadOnlyMemory<WebGpuWorldVertex> vertices,
        ReadOnlyMemory<uint> indices)
    {
        Key = key;
        Revision = revision;
        Vertices = vertices;
        Indices = indices;
    }

    public int Key { get; }

    public int Revision { get; }

    public ReadOnlyMemory<WebGpuWorldVertex> Vertices { get; }

    public ReadOnlyMemory<uint> Indices { get; }

    public bool IsEmpty => Vertices.IsEmpty || Indices.IsEmpty;
}

public readonly struct WebGpuWorldBatchFrame
{
    private readonly ReadOnlyMemory<WebGpuWorldInstance> _instances;

    public WebGpuWorldBatchFrame(
        int batchKey,
        WebGpuMeshFrame mesh,
        ReadOnlyMemory<WebGpuWorldInstance> instances,
        int instanceCount)
    {
        if (batchKey <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(batchKey));
        }

        if ((uint)instanceCount > (uint)instances.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(instanceCount));
        }

        BatchKey = batchKey;
        Mesh = mesh;
        _instances = instances;
        InstanceCount = instanceCount;
    }

    public int BatchKey { get; }

    public WebGpuMeshFrame Mesh { get; }

    public int InstanceCount { get; }

    public ReadOnlySpan<WebGpuWorldInstance> Instances => _instances.Span[..InstanceCount];
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuWorldVertex
{
    public Vector3 Position;
    public Vector3 Normal;
    public Vector4 Color;
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuWorldInstance
{
    public Vector3 Position;
    public Vector3 Scale;
    public Quaternion Rotation;
    public Vector4 Color;
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuScreenInstance
{
    public Vector2 CenterPx;
    public Vector2 HalfSizePx;
    public Vector4 Color;
    public Vector4 ClipRectPx;
    public float RotationRad;
    public float Shape;
    public float ClipShape;
    public float Padding;
}

/// <summary>
/// Static GPU bar instance. World position is resolved through <see cref="AnchorIndex"/> into the world-HUD anchor buffer.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct WebGpuWorldBarInstance
{
    public Vector2 HalfSizePx;
    public float HealthRatio;
    public float AnchorIndex;
    public Vector4 BackgroundColor;
    public Vector4 ForegroundColor;
    public Vector2 PaddingPx;
    public Vector2 Reserved;
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuWorldGlyphInstance
{
    public Vector2 LocalOffsetPx;
    public Vector2 HalfSizePx;
    public Vector4 Color;
    public Vector4 UvRect;
    public float AnchorIndex;
    public float Padding0;
    public float Padding1;
}

[StructLayout(LayoutKind.Sequential)]
public struct WebGpuWorldHudAnchor
{
    public Vector3 WorldPosition;
    /// <summary>1 when the retained HUD item is live; GPU may use this to discard without rewriting static lanes.</summary>
    public float Visibility;
}
