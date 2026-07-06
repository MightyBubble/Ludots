using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Ludots.Core.Mathematics;

namespace Ludots.Core.StructureCollision
{
    public static class StructureCollisionAssetJson
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = false
        };

        public static StructureCollisionAsset Read(Stream stream)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            AssetDto dto = JsonSerializer.Deserialize<AssetDto>(stream, Options)
                ?? throw new InvalidOperationException("Structure collision asset JSON is empty.");

            return Build(dto);
        }

        private static StructureCollisionAsset Build(AssetDto dto)
        {
            BoundsDto boundsDto = dto.WorldBounds ?? throw new InvalidOperationException("Structure collision asset requires worldBounds.");
            int version = dto.Version > 0 ? dto.Version : throw new InvalidOperationException("Structure collision asset requires positive version.");
            int chunkSizeCm = dto.ChunkSizeCm > 0 ? dto.ChunkSizeCm : throw new InvalidOperationException("Structure collision asset requires positive chunkSizeCm.");
            float coordinateScale = dto.CoordinateScale ?? throw new InvalidOperationException("Structure collision asset requires coordinateScale.");
            if (!float.IsFinite(coordinateScale) || coordinateScale <= 0f)
            {
                throw new InvalidOperationException("Structure collision asset requires positive coordinateScale.");
            }

            var header = new StructureCollisionHeader(
                version,
                new WorldAabbCm(boundsDto.Xcm, boundsDto.Zcm, boundsDto.WidthCm, boundsDto.HeightCm),
                chunkSizeCm,
                dto.Revision,
                coordinateScale);

            StructureLayerDefinition[] layers = ParseLayers(dto.Layers);
            StructureAgentMaskDefinition[] masks = ParseAgentMasks(dto.AgentMasks);
            var layerById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < layers.Length; i++)
            {
                layerById.Add(layers[i].Id, layers[i].Value);
            }

            var maskById = new Dictionary<string, uint>(StringComparer.Ordinal);
            for (int i = 0; i < masks.Length; i++)
            {
                maskById.Add(masks[i].Id, masks[i].Bits);
            }

            StructureShapeDefinition[] shapes = ParseShapes(dto.Shapes);
            StructureSurfaceDefinition[] surfaces = ParseSurfaces(dto.Surfaces, layerById, maskById);
            return StructureCollisionAssetBuilder.Build(header, layers, masks, shapes, surfaces);
        }

        private static StructureLayerDefinition[] ParseLayers(List<LayerDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
            {
                throw new InvalidOperationException("Structure collision asset requires layers.");
            }

            var result = new StructureLayerDefinition[dtos.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var values = new HashSet<int>();
            for (int i = 0; i < dtos.Count; i++)
            {
                LayerDto dto = dtos[i] ?? throw new InvalidOperationException($"layers[{i}] is null.");
                if (!ids.Add(dto.Id))
                {
                    throw new InvalidOperationException($"Duplicate structure layer id '{dto.Id}'.");
                }

                if (!values.Add(dto.Value))
                {
                    throw new InvalidOperationException($"Duplicate structure layer value '{dto.Value}'.");
                }

                result[i] = new StructureLayerDefinition(dto.Id, dto.Value);
            }

            return result;
        }

        private static StructureAgentMaskDefinition[] ParseAgentMasks(List<AgentMaskDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
            {
                throw new InvalidOperationException("Structure collision asset requires agentMasks.");
            }

            var result = new StructureAgentMaskDefinition[dtos.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < dtos.Count; i++)
            {
                AgentMaskDto dto = dtos[i] ?? throw new InvalidOperationException($"agentMasks[{i}] is null.");
                if (!ids.Add(dto.Id))
                {
                    throw new InvalidOperationException($"Duplicate structure agent mask id '{dto.Id}'.");
                }

                result[i] = new StructureAgentMaskDefinition(dto.Id, dto.Bits);
            }

            return result;
        }

        private static StructureShapeDefinition[] ParseShapes(List<ShapeDto>? dtos)
        {
            if (dtos == null || dtos.Count == 0)
            {
                throw new InvalidOperationException("Structure collision asset requires shapes.");
            }

            var result = new StructureShapeDefinition[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                ShapeDto dto = dtos[i] ?? throw new InvalidOperationException($"shapes[{i}] is null.");
                if (!Enum.TryParse(dto.Kind, ignoreCase: false, out StructureShapeKind kind))
                {
                    throw new InvalidOperationException($"Structure shape '{dto.Id}' has unknown kind '{dto.Kind}'.");
                }

                float planeHeight = dto.PlaneHeightCm ?? dto.HeightCm ?? 0f;
                float minHeight = dto.MinHeightCm ?? dto.HeightCm ?? planeHeight;
                float maxHeight = dto.MaxHeightCm ?? dto.HeightCm ?? planeHeight;
                result[i] = new StructureShapeDefinition
                {
                    Id = dto.Id,
                    Kind = kind,
                    Bounds = dto.Bounds != null ? new WorldAabbCm(dto.Bounds.Xcm, dto.Bounds.Zcm, dto.Bounds.WidthCm, dto.Bounds.HeightCm) : default,
                    Vertices = ParsePoints(dto.Vertices),
                    MinHeightCm = minHeight,
                    MaxHeightCm = maxHeight,
                    PlaneOriginXCm = dto.PlaneOriginXCm,
                    PlaneOriginZCm = dto.PlaneOriginZCm,
                    PlaneHeightCm = planeHeight,
                    PlaneSlopeX = dto.PlaneSlopeX,
                    PlaneSlopeZ = dto.PlaneSlopeZ,
                    CenterXCm = dto.CenterXCm,
                    CenterZCm = dto.CenterZCm,
                    HalfWidthCm = dto.HalfWidthCm,
                    HalfDepthCm = dto.HalfDepthCm,
                    YawRadians = dto.YawRadians,
                    RadiusCm = dto.RadiusCm,
                    SegmentAXCm = dto.SegmentAXCm,
                    SegmentAZCm = dto.SegmentAZCm,
                    SegmentBXCm = dto.SegmentBXCm,
                    SegmentBZCm = dto.SegmentBZCm,
                    SegmentHalfWidthCm = dto.SegmentHalfWidthCm
                };
            }

            return result;
        }

        private static StructureSurfaceDefinition[] ParseSurfaces(
            List<SurfaceDto>? dtos,
            Dictionary<string, int> layerById,
            Dictionary<string, uint> maskById)
        {
            if (dtos == null || dtos.Count == 0)
            {
                throw new InvalidOperationException("Structure collision asset requires surfaces.");
            }

            var result = new StructureSurfaceDefinition[dtos.Count];
            for (int i = 0; i < dtos.Count; i++)
            {
                SurfaceDto dto = dtos[i] ?? throw new InvalidOperationException($"surfaces[{i}] is null.");
                if (!Enum.TryParse(dto.Kind, ignoreCase: false, out StructureSurfaceKind kind))
                {
                    throw new InvalidOperationException($"Structure surface '{dto.Id}' has unknown kind '{dto.Kind}'.");
                }

                if (!layerById.TryGetValue(dto.LayerId, out int layerId))
                {
                    throw new InvalidOperationException($"Structure surface '{dto.Id}' references unknown layer id '{dto.LayerId}'.");
                }

                if (!maskById.TryGetValue(dto.AgentMaskId, out uint agentMask))
                {
                    throw new InvalidOperationException($"Structure surface '{dto.Id}' references unknown agent mask '{dto.AgentMaskId}'.");
                }

                result[i] = new StructureSurfaceDefinition
                {
                    SurfaceId = dto.Id,
                    Kind = kind,
                    Flags = ParseFlags(dto.Flags, dto.Id),
                    LayerId = layerId,
                    AgentMask = agentMask,
                    ShapeId = dto.ShapeId,
                    Bounds = dto.Bounds != null ? new WorldAabbCm(dto.Bounds.Xcm, dto.Bounds.Zcm, dto.Bounds.WidthCm, dto.Bounds.HeightCm) : null,
                    MinHeightCm = dto.MinHeightCm,
                    MaxHeightCm = dto.MaxHeightCm,
                    SourcePrefabId = dto.SourcePrefabId,
                    SourcePartId = dto.SourcePartId
                };
            }

            return result;
        }

        private static StructurePointCm[] ParsePoints(List<PointDto>? points)
        {
            if (points == null || points.Count == 0)
            {
                return Array.Empty<StructurePointCm>();
            }

            var result = new StructurePointCm[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                PointDto dto = points[i] ?? throw new InvalidOperationException($"vertices[{i}] is null.");
                result[i] = new StructurePointCm(dto.Xcm, dto.Zcm);
            }

            return result;
        }

        private static StructureSurfaceFlags ParseFlags(List<string>? flags, int surfaceId)
        {
            if (flags == null || flags.Count == 0)
            {
                return StructureSurfaceFlags.None;
            }

            StructureSurfaceFlags result = StructureSurfaceFlags.None;
            for (int i = 0; i < flags.Count; i++)
            {
                string flag = flags[i];
                if (!Enum.TryParse(flag, ignoreCase: false, out StructureSurfaceFlags parsed))
                {
                    throw new InvalidOperationException($"Structure surface '{surfaceId}' has unknown flag '{flag}'.");
                }

                result |= parsed;
            }

            return result;
        }

        private sealed class AssetDto
        {
            [JsonPropertyName("version")]
            public int Version { get; set; }

            [JsonPropertyName("worldBounds")]
            public BoundsDto? WorldBounds { get; set; }

            [JsonPropertyName("chunkSizeCm")]
            public int ChunkSizeCm { get; set; }

            [JsonPropertyName("revision")]
            public int Revision { get; set; }

            [JsonPropertyName("coordinateScale")]
            public float? CoordinateScale { get; set; }

            [JsonPropertyName("layers")]
            public List<LayerDto>? Layers { get; set; }

            [JsonPropertyName("agentMasks")]
            public List<AgentMaskDto>? AgentMasks { get; set; }

            [JsonPropertyName("shapes")]
            public List<ShapeDto>? Shapes { get; set; }

            [JsonPropertyName("surfaces")]
            public List<SurfaceDto>? Surfaces { get; set; }
        }

        private sealed class BoundsDto
        {
            [JsonPropertyName("xcm")]
            public int Xcm { get; set; }

            [JsonPropertyName("zcm")]
            public int Zcm { get; set; }

            [JsonPropertyName("widthCm")]
            public int WidthCm { get; set; }

            [JsonPropertyName("heightCm")]
            public int HeightCm { get; set; }
        }

        private sealed class LayerDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("value")]
            public int Value { get; set; }
        }

        private sealed class AgentMaskDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("bits")]
            public uint Bits { get; set; }
        }

        private sealed class ShapeDto
        {
            [JsonPropertyName("id")]
            public string Id { get; set; } = string.Empty;

            [JsonPropertyName("kind")]
            public string Kind { get; set; } = string.Empty;

            [JsonPropertyName("bounds")]
            public BoundsDto? Bounds { get; set; }

            [JsonPropertyName("vertices")]
            public List<PointDto>? Vertices { get; set; }

            [JsonPropertyName("heightCm")]
            public float? HeightCm { get; set; }

            [JsonPropertyName("minHeightCm")]
            public float? MinHeightCm { get; set; }

            [JsonPropertyName("maxHeightCm")]
            public float? MaxHeightCm { get; set; }

            [JsonPropertyName("planeOriginXCm")]
            public float PlaneOriginXCm { get; set; }

            [JsonPropertyName("planeOriginZCm")]
            public float PlaneOriginZCm { get; set; }

            [JsonPropertyName("planeHeightCm")]
            public float? PlaneHeightCm { get; set; }

            [JsonPropertyName("planeSlopeX")]
            public float PlaneSlopeX { get; set; }

            [JsonPropertyName("planeSlopeZ")]
            public float PlaneSlopeZ { get; set; }

            [JsonPropertyName("centerXCm")]
            public float CenterXCm { get; set; }

            [JsonPropertyName("centerZCm")]
            public float CenterZCm { get; set; }

            [JsonPropertyName("halfWidthCm")]
            public float HalfWidthCm { get; set; }

            [JsonPropertyName("halfDepthCm")]
            public float HalfDepthCm { get; set; }

            [JsonPropertyName("yawRadians")]
            public float YawRadians { get; set; }

            [JsonPropertyName("radiusCm")]
            public float RadiusCm { get; set; }

            [JsonPropertyName("segmentAXCm")]
            public float SegmentAXCm { get; set; }

            [JsonPropertyName("segmentAZCm")]
            public float SegmentAZCm { get; set; }

            [JsonPropertyName("segmentBXCm")]
            public float SegmentBXCm { get; set; }

            [JsonPropertyName("segmentBZCm")]
            public float SegmentBZCm { get; set; }

            [JsonPropertyName("segmentHalfWidthCm")]
            public float SegmentHalfWidthCm { get; set; }
        }

        private sealed class PointDto
        {
            [JsonPropertyName("xcm")]
            public float Xcm { get; set; }

            [JsonPropertyName("zcm")]
            public float Zcm { get; set; }
        }

        private sealed class SurfaceDto
        {
            [JsonPropertyName("id")]
            public int Id { get; set; }

            [JsonPropertyName("kind")]
            public string Kind { get; set; } = string.Empty;

            [JsonPropertyName("flags")]
            public List<string>? Flags { get; set; }

            [JsonPropertyName("layerId")]
            public string LayerId { get; set; } = string.Empty;

            [JsonPropertyName("agentMaskId")]
            public string AgentMaskId { get; set; } = string.Empty;

            [JsonPropertyName("shapeId")]
            public string ShapeId { get; set; } = string.Empty;

            [JsonPropertyName("bounds")]
            public BoundsDto? Bounds { get; set; }

            [JsonPropertyName("minHeightCm")]
            public float? MinHeightCm { get; set; }

            [JsonPropertyName("maxHeightCm")]
            public float? MaxHeightCm { get; set; }

            [JsonPropertyName("sourcePrefabId")]
            public int SourcePrefabId { get; set; }

            [JsonPropertyName("sourcePartId")]
            public int SourcePartId { get; set; }
        }
    }
}
