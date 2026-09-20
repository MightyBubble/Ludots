using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Scripting;

namespace Ludots.Core.Gameplay.MapTriggers
{
    /// <summary>
    /// Structural parsing for the region volume component setters
    /// (<c>ComponentRegistry</c> authoring, flat JSON per component). Closed field
    /// sets per shape, convexity / degeneracy checks, payload value typing, and tag
    /// resolution all fail closed here. Emission vocabulary and payload-vs-schema
    /// validation are semantic and live in the post-placement bake pass instead.
    /// </summary>
    public static class RegionVolumeComponentAuthoring
    {
        private const string VolumeKeyField = "volumeKey";
        private const string ShapeField = "shape";
        private const string RadiusField = "radiusCm";
        private const string HalfWidthField = "halfWidthCm";
        private const string HalfHeightField = "halfHeightCm";
        private const string PointsField = "points";
        private const string AxField = "ax";
        private const string AyField = "ay";
        private const string BxField = "bx";
        private const string ByField = "by";
        private const string HalfThicknessField = "halfThicknessCm";

        private const string CircleShape = "circle";
        private const string RectShape = "rect";
        private const string PolygonShape = "polygon";
        private const string SegmentShape = "segment";

        public static string ParseVolumeKey(JsonObject obj, string context)
        {
            string volumeKey = RequireTrimmedString(obj, VolumeKeyField, context);
            return volumeKey;
        }

        public static RegionVolumeShape ParseShape(JsonObject obj, string context)
        {
            string shape = RequireTrimmedString(obj, ShapeField, context);
            bool isCircle = string.Equals(shape, CircleShape, StringComparison.Ordinal);
            bool isRect = string.Equals(shape, RectShape, StringComparison.Ordinal);
            bool isPolygon = string.Equals(shape, PolygonShape, StringComparison.Ordinal);
            bool isSegment = string.Equals(shape, SegmentShape, StringComparison.Ordinal);
            if (!isCircle && !isRect && !isPolygon && !isSegment)
            {
                throw new InvalidOperationException(
                    $"{context}.{ShapeField} has unknown shape '{shape}'. Allowed shapes: '{CircleShape}', '{RectShape}', '{PolygonShape}', '{SegmentShape}'.");
            }

            string[] allowed =
                isCircle ? new[] { VolumeKeyField, ShapeField, RadiusField } :
                isRect ? new[] { VolumeKeyField, ShapeField, HalfWidthField, HalfHeightField } :
                isPolygon ? new[] { VolumeKeyField, ShapeField, PointsField } :
                new[] { VolumeKeyField, ShapeField, AxField, AyField, BxField, ByField, HalfThicknessField };
            ValidateFields(obj, context, allowed);

            if (isCircle)
            {
                return new RegionVolumeShape
                {
                    Kind = RegionVolumeShapeKind.Circle,
                    Radius = RequirePositiveFloat(obj, RadiusField, context),
                };
            }

            if (isRect)
            {
                return new RegionVolumeShape
                {
                    Kind = RegionVolumeShapeKind.Rect,
                    HalfWidth = RequirePositiveFloat(obj, HalfWidthField, context),
                    HalfHeight = RequirePositiveFloat(obj, HalfHeightField, context),
                };
            }

            if (isPolygon)
            {
                return new RegionVolumeShape
                {
                    Kind = RegionVolumeShapeKind.Polygon,
                    PolygonPoints = ParseConvexPolygon(RequireArray(obj, PointsField, context), context, PointsField),
                };
            }

            Fix64 ax = RequireFloat(obj, AxField, context);
            Fix64 ay = RequireFloat(obj, AyField, context);
            Fix64 bx = RequireFloat(obj, BxField, context);
            Fix64 by = RequireFloat(obj, ByField, context);
            if (ax == bx && ay == by)
            {
                throw new InvalidOperationException(
                    $"{context}.{AxField}/{AyField} and {BxField}/{ByField} coincide; a zero-length segment has no containment.");
            }

            return new RegionVolumeShape
            {
                Kind = RegionVolumeShapeKind.Segment,
                SegmentA = new Fix64Vec2(ax, ay),
                SegmentB = new Fix64Vec2(bx, by),
                HalfThickness = RequirePositiveFloat(obj, HalfThicknessField, context),
            };
        }

        public static RegionVolumeEmissionCm ParseEmission(JsonObject obj, string context)
        {
            ValidateFields(obj, context, new[] { "enter", "exit", "payload" });

            string? enter = ReadOptionalTrimmedString(obj, "enter", context);
            string? exit = ReadOptionalTrimmedString(obj, "exit", context);
            RegionVolumePayloadEntry[]? payload = null;
            if (obj.TryGetPropertyValue("payload", out JsonNode? payloadNode) && payloadNode != null)
            {
                if (payloadNode is not JsonObject payloadObj)
                {
                    throw new InvalidOperationException(
                        $"{context}.payload must be an object of key to int/float/string.");
                }

                var entries = new List<RegionVolumePayloadEntry>(payloadObj.Count);
                foreach (var kvp in payloadObj)
                {
                    string key = kvp.Key;
                    if (string.IsNullOrWhiteSpace(key) || !string.Equals(key, key.Trim(), StringComparison.Ordinal))
                    {
                        throw new InvalidOperationException(
                            $"{context}.payload keys must be trimmed non-empty strings.");
                    }

                    if (kvp.Value is JsonValue v && v.TryGetValue<int>(out int intValue))
                    {
                        entries.Add(new RegionVolumePayloadEntry { Key = key, Type = RegionVolumePayloadValueType.Int, IntValue = intValue });
                    }
                    else if (kvp.Value is JsonValue f && f.TryGetValue<float>(out float floatValue) && float.IsFinite(floatValue))
                    {
                        entries.Add(new RegionVolumePayloadEntry { Key = key, Type = RegionVolumePayloadValueType.Float, FloatValue = floatValue });
                    }
                    else if (kvp.Value is JsonValue s && s.TryGetValue<string>(out string? stringValue))
                    {
                        entries.Add(new RegionVolumePayloadEntry { Key = key, Type = RegionVolumePayloadValueType.String, StringValue = stringValue });
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"{context}.payload key '{key}' must be an int, float, or string.");
                    }
                }

                payload = entries.Count > 0 ? entries.ToArray() : null;
            }

            if (enter == null && exit == null && payload == null)
            {
                throw new InvalidOperationException(
                    $"{context} declares no 'enter', 'exit', or 'payload'; remove the component or author at least one.");
            }

            return new RegionVolumeEmissionCm
            {
                EnterEvent = new EventKey(enter ?? GameEvents.RegionEntered.Value),
                ExitEvent = new EventKey(exit ?? GameEvents.RegionExited.Value),
                Payload = payload,
            };
        }

        public static GameplayTagContainer ParseTagFilter(JsonArray tags, string context)
        {
            var filter = new GameplayTagContainer();
            for (int i = 0; i < tags.Count; i++)
            {
                if (tags[i] is not JsonValue value ||
                    !value.TryGetValue<string>(out string? tagName) ||
                    string.IsNullOrWhiteSpace(tagName) ||
                    !string.Equals(tagName, tagName.Trim(), StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"{context}.tags[{i}] must be a trimmed non-empty tag name.");
                }

                int tagId = TagRegistry.GetId(tagName);
                if (tagId == TagRegistry.InvalidId)
                {
                    if (!TagRegistry.IsFrozen)
                    {
                        tagId = TagRegistry.Register(tagName);
                    }
                    else
                    {
                        throw new InvalidOperationException(
                            $"{context}.tags[{i}] references unknown tag '{tagName}'.");
                    }
                }

                if (tagId > GameplayTagContainer.MAX_TAG_ID)
                {
                    throw new InvalidOperationException(
                        $"{context}.tags[{i}] references tag '{tagName}' with id {tagId} above the GameplayTagContainer capacity {GameplayTagContainer.MAX_TAG_ID}.");
                }

                filter.AddTag(tagId);
            }

            return filter;
        }

        private static Fix64Vec2[] ParseConvexPolygon(JsonArray points, string context, string field)
        {
            if (points.Count < 3)
            {
                throw new InvalidOperationException($"{context}.{field} needs at least 3 points.");
            }

            var result = new Fix64Vec2[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                if (points[i] is not JsonArray pair || pair.Count != 2)
                {
                    throw new InvalidOperationException(
                        $"{context}.{field}[{i}] must be an [x, y] pair of numbers.");
                }

                result[i] = new Fix64Vec2(
                    RequireFiniteFloat(pair[0], $"{context}.{field}[{i}][0]"),
                    RequireFiniteFloat(pair[1], $"{context}.{field}[{i}][1]"));
            }

            bool sawPositive = false;
            bool sawNegative = false;
            for (int i = 0; i < result.Length; i++)
            {
                Fix64Vec2 a = result[i];
                Fix64Vec2 b = result[(i + 1) % result.Length];
                Fix64Vec2 c = result[(i + 2) % result.Length];
                Fix64 cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                if (cross < Fix64.Zero)
                {
                    sawNegative = true;
                }
                else if (cross > Fix64.Zero)
                {
                    sawPositive = true;
                }
            }

            if (sawPositive && sawNegative)
            {
                throw new InvalidOperationException(
                    $"{context}.{field} is not convex; split concave areas into multiple convex volumes or use a field layer.");
            }

            if (!sawPositive && !sawNegative)
            {
                throw new InvalidOperationException(
                    $"{context}.{field} is degenerate (all points collinear).");
            }

            return result;
        }

        private static void ValidateFields(JsonObject obj, string context, string[] allowed)
        {
            foreach (var kvp in obj)
            {
                bool known = false;
                for (int i = 0; i < allowed.Length; i++)
                {
                    if (string.Equals(kvp.Key, allowed[i], StringComparison.Ordinal))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new InvalidOperationException(
                        $"{context} contains unsupported property '{kvp.Key}'.");
                }
            }
        }

        private static string RequireTrimmedString(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) ||
                node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text))
            {
                throw new InvalidOperationException($"{context}.{field} is required and must be a string.");
            }

            if (string.IsNullOrWhiteSpace(text) || !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context}.{field} must be a trimmed non-empty string.");
            }

            return text;
        }

        private static string? ReadOptionalTrimmedString(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) || node == null)
            {
                return null;
            }

            if (node is not JsonValue value ||
                !value.TryGetValue<string>(out string? text) ||
                string.IsNullOrWhiteSpace(text) ||
                !string.Equals(text, text.Trim(), StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{context}.{field} must be a trimmed non-empty string.");
            }

            return text;
        }

        private static JsonArray RequireArray(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node) || node is not JsonArray array)
            {
                throw new InvalidOperationException($"{context}.{field} is required and must be an array.");
            }

            return array;
        }

        private static Fix64 RequireFloat(JsonObject obj, string field, string context)
        {
            if (!obj.TryGetPropertyValue(field, out JsonNode? node))
            {
                throw new InvalidOperationException($"{context}.{field} is required and must be a finite number.");
            }

            return RequireFiniteFloat(node, $"{context}.{field}");
        }

        private static Fix64 RequirePositiveFloat(JsonObject obj, string field, string context)
        {
            Fix64 value = RequireFloat(obj, field, context);
            if (value <= Fix64.Zero)
            {
                throw new InvalidOperationException($"{context}.{field} must be a positive number.");
            }

            return value;
        }

        private static Fix64 RequireFiniteFloat(JsonNode? node, string context)
        {
            if (node is not JsonValue value ||
                !value.TryGetValue<float>(out float raw) ||
                !float.IsFinite(raw))
            {
                throw new InvalidOperationException($"{context} requires a finite number.");
            }

            return Fix64.FromFloat(raw);
        }
    }
}
