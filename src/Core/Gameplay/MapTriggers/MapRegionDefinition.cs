using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.MapTriggers
{
    public enum MapRegionShape
    {
        Circle,
        Rect
    }

    /// <summary>
    /// Data-declared trigger region authored under map JSON "Regions".
    /// Shape containment is inclusive on the boundary: a position exactly at radiusCm
    /// (circle) or at halfWidthCm/halfHeightCm (rect) counts as inside.
    /// </summary>
    public sealed class MapRegionDefinition
    {
        public const string FieldName = "Regions";
        private const string IdField = "id";
        private const string ShapeField = "shape";
        private const string XField = "x";
        private const string YField = "y";
        private const string RadiusField = "radiusCm";
        private const string HalfWidthField = "halfWidthCm";
        private const string HalfHeightField = "halfHeightCm";
        private const string EntityTagsField = "entityTags";
        private const string CircleShape = "circle";
        private const string RectShape = "rect";

        public string Id { get; }
        public MapRegionShape Shape { get; }
        public Fix64 X { get; }
        public Fix64 Y { get; }
        public Fix64 RadiusCm { get; }
        public Fix64 HalfWidthCm { get; }
        public Fix64 HalfHeightCm { get; }

        /// <summary>Empty list means the region tracks every positioned entity of the map.</summary>
        public List<string> EntityTags { get; } = new List<string>();

        private MapRegionDefinition(
            string id,
            MapRegionShape shape,
            Fix64 x,
            Fix64 y,
            Fix64 radiusCm,
            Fix64 halfWidthCm,
            Fix64 halfHeightCm)
        {
            Id = id;
            Shape = shape;
            X = x;
            Y = y;
            RadiusCm = radiusCm;
            HalfWidthCm = halfWidthCm;
            HalfHeightCm = halfHeightCm;
        }

        public static List<MapRegionDefinition> ParseList(JsonNode? node, string mapId)
        {
            var regions = new List<MapRegionDefinition>();
            if (node == null)
            {
                return regions;
            }

            if (node is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"Map '{mapId}' {FieldName} must be an array of region objects.");
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonObject obj)
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' {FieldName}[{i}] must be an object.");
                }

                MapRegionDefinition region = ParseObject(obj, $"Map '{mapId}' {FieldName}[{i}]");
                if (!seenIds.Add(region.Id))
                {
                    throw new InvalidOperationException(
                        $"Map '{mapId}' {FieldName} has duplicate region id '{region.Id}'.");
                }

                regions.Add(region);
            }

            return regions;
        }

        public static MapRegionDefinition ParseObject(JsonObject obj, string context)
        {
            string shape = ReadRequiredTrimmedString(obj, ShapeField, context);
            if (!string.Equals(shape, CircleShape, StringComparison.Ordinal) &&
                !string.Equals(shape, RectShape, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} has unknown shape '{shape}'. Allowed shapes: '{CircleShape}', '{RectShape}'.");
            }

            bool isCircle = string.Equals(shape, CircleShape, StringComparison.Ordinal);
            string allowedFields = isCircle
                ? $"'{IdField}', '{ShapeField}', '{XField}', '{YField}', '{RadiusField}', '{EntityTagsField}'"
                : $"'{IdField}', '{ShapeField}', '{XField}', '{YField}', '{HalfWidthField}', '{HalfHeightField}', '{EntityTagsField}'";
            foreach (var kvp in obj)
            {
                bool known = string.Equals(kvp.Key, IdField, StringComparison.Ordinal) ||
                             string.Equals(kvp.Key, ShapeField, StringComparison.Ordinal) ||
                             string.Equals(kvp.Key, XField, StringComparison.Ordinal) ||
                             string.Equals(kvp.Key, YField, StringComparison.Ordinal) ||
                             string.Equals(kvp.Key, EntityTagsField, StringComparison.Ordinal) ||
                             (isCircle && string.Equals(kvp.Key, RadiusField, StringComparison.Ordinal)) ||
                             (!isCircle && string.Equals(kvp.Key, HalfWidthField, StringComparison.Ordinal)) ||
                             (!isCircle && string.Equals(kvp.Key, HalfHeightField, StringComparison.Ordinal));
                if (!known)
                {
                    throw new InvalidOperationException(
                        $"{context} has unknown field '{kvp.Key}'. Allowed fields: {allowedFields}.");
                }
            }

            string id = ReadRequiredTrimmedString(obj, IdField, context);
            Fix64 x = ReadRequiredFloat(obj, XField, context);
            Fix64 y = ReadRequiredFloat(obj, YField, context);
            Fix64 radiusCm = Fix64.Zero;
            Fix64 halfWidthCm = Fix64.Zero;
            Fix64 halfHeightCm = Fix64.Zero;

            if (isCircle)
            {
                radiusCm = ReadRequiredPositiveFloat(obj, RadiusField, context);
            }
            else
            {
                halfWidthCm = ReadRequiredPositiveFloat(obj, HalfWidthField, context);
                halfHeightCm = ReadRequiredPositiveFloat(obj, HalfHeightField, context);
            }

            var region = new MapRegionDefinition(id, isCircle ? MapRegionShape.Circle : MapRegionShape.Rect, x, y, radiusCm, halfWidthCm, halfHeightCm);
            ReadOptionalEntityTags(obj, EntityTagsField, context, region.EntityTags);
            return region;
        }

        public bool Contains(in Fix64Vec2 position)
        {
            if (Shape == MapRegionShape.Circle)
            {
                Fix64 dx = position.X - X;
                Fix64 dy = position.Y - Y;
                return dx * dx + dy * dy <= RadiusCm * RadiusCm;
            }

            return Fix64.Abs(position.X - X) <= HalfWidthCm &&
                   Fix64.Abs(position.Y - Y) <= HalfHeightCm;
        }

        private static string ReadRequiredTrimmedString(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text))
            {
                throw new InvalidOperationException(
                    $"{context} requires field '{field}' to be a string.");
            }

            if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"{context} field '{field}' must be a trimmed non-empty string.");
            }

            return text;
        }

        private static Fix64 ReadRequiredFloat(JsonObject obj, string field, string context)
        {
            return Fix64.FromFloat(ReadRequiredFiniteFloat(obj, field, context));
        }

        private static Fix64 ReadRequiredPositiveFloat(JsonObject obj, string field, string context)
        {
            float raw = ReadRequiredFiniteFloat(obj, field, context);
            if (raw <= 0f)
            {
                throw new InvalidOperationException(
                    $"{context} field '{field}' must be a positive number.");
            }

            return Fix64.FromFloat(raw);
        }

        private static float ReadRequiredFiniteFloat(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue value ||
                !value.TryGetValue<float>(out float raw) ||
                !float.IsFinite(raw))
            {
                throw new InvalidOperationException(
                    $"{context} requires field '{field}' to be a finite number.");
            }

            return raw;
        }

        private static void ReadOptionalEntityTags(JsonObject obj, string field, string context, List<string> target)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) || node == null)
            {
                return;
            }

            if (node is not JsonArray array)
            {
                throw new InvalidOperationException(
                    $"{context} field '{field}' must be an array of tag names.");
            }

            for (int i = 0; i < array.Count; i++)
            {
                if (array[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? text))
                {
                    throw new InvalidOperationException(
                        $"{context} field '{field}'[{i}] must be a string.");
                }

                if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context} field '{field}'[{i}] must be a trimmed non-empty tag name.");
                }

                target.Add(text);
            }
        }
    }
}
