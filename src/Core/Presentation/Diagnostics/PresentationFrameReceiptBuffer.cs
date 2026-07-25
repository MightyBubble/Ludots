using System;
using System.Collections.Generic;
using System.Numerics;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Diagnostics
{
    public readonly record struct PresentationFrameReceiptItem(
        int StableId,
        int TemplateId,
        Vector3 Position,
        Quaternion Rotation,
        Vector3 Scale,
        ProceduralMeshBounds LocalBounds);

    public readonly record struct PresentationTemplateReceiptSummary(
        int TemplateId,
        int SubmittedCount,
        int OnscreenCount,
        float MinimumShortEdgePx,
        float MinimumAreaPx2);

    public sealed class PresentationFrameReceiptBuffer
    {
        private readonly PresentationFrameReceiptItem[] _items;
        private int _count;

        public PresentationFrameReceiptBuffer(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _items = new PresentationFrameReceiptItem[capacity];
        }

        public int Count => _count;

        public int Capacity => _items.Length;

        public void BeginFrame()
        {
            _count = 0;
        }

        public void RecordSubmitted(
            int stableId,
            int templateId,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in ProceduralMeshBounds localBounds)
        {
            if (stableId <= 0)
            {
                throw new InvalidOperationException("Presentation submission receipts require a positive stable id.");
            }
            if (templateId <= 0)
            {
                throw new InvalidOperationException("Presentation submission receipts require a positive performer template id.");
            }
            if (_count >= _items.Length)
            {
                throw new InvalidOperationException(
                    $"Presentation submission receipt buffer capacity {_items.Length} was exceeded.");
            }

            ProceduralMeshBounds resolvedBounds = localBounds.Extents == Vector3.Zero
                ? new ProceduralMeshBounds(Vector3.Zero, Vector3.One * 0.5f)
                : localBounds;
            _items[_count++] = new PresentationFrameReceiptItem(
                stableId,
                templateId,
                position,
                rotation,
                scale,
                resolvedBounds);
        }

        public ReadOnlySpan<PresentationFrameReceiptItem> GetSpan() =>
            new(_items, 0, _count);

        public PresentationTemplateReceiptSummary[] BuildTemplateSummaries(in ProjectionSnapshot projection)
        {
            var instances = new Dictionary<long, InstanceProjection>(_count);
            for (int i = 0; i < _count; i++)
            {
                ref readonly PresentationFrameReceiptItem item = ref _items[i];
                long key = ((long)item.TemplateId << 32) | (uint)item.StableId;
                bool onscreen = TryProjectClippedBounds(in item, in projection, out ScreenBounds bounds);
                if (!instances.TryGetValue(key, out InstanceProjection instance))
                {
                    instances.Add(key, new InstanceProjection(item.TemplateId, onscreen, bounds));
                    continue;
                }

                if (onscreen)
                {
                    instance.Include(in bounds);
                    instances[key] = instance;
                }
            }

            var templates = new Dictionary<int, TemplateProjection>();
            foreach (InstanceProjection instance in instances.Values)
            {
                if (!templates.TryGetValue(instance.TemplateId, out TemplateProjection template))
                {
                    template = new TemplateProjection(instance.TemplateId);
                }
                template.Include(in instance);
                templates[instance.TemplateId] = template;
            }

            var result = new PresentationTemplateReceiptSummary[templates.Count];
            int resultIndex = 0;
            foreach (TemplateProjection template in templates.Values)
            {
                result[resultIndex++] = template.ToSummary();
            }
            Array.Sort(result, static (left, right) => left.TemplateId.CompareTo(right.TemplateId));
            return result;
        }

        private static bool TryProjectClippedBounds(
            in PresentationFrameReceiptItem item,
            in ProjectionSnapshot projection,
            out ScreenBounds bounds)
        {
            bounds = default;
            if (projection.Resolution.X <= 0f || projection.Resolution.Y <= 0f)
            {
                return false;
            }

            Vector3 min = item.LocalBounds.Min;
            Vector3 max = item.LocalBounds.Max;
            Quaternion rotation = Quaternion.Normalize(item.Rotation);
            float left = float.PositiveInfinity;
            float top = float.PositiveInfinity;
            float right = float.NegativeInfinity;
            float bottom = float.NegativeInfinity;
            int projectedCornerCount = 0;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 local = new(
                    (corner & 1) == 0 ? min.X : max.X,
                    (corner & 2) == 0 ? min.Y : max.Y,
                    (corner & 4) == 0 ? min.Z : max.Z);
                Vector3 scaled = Vector3.Multiply(local, item.Scale);
                Vector3 world = item.Position + Vector3.Transform(scaled, rotation);
                Vector4 clip = Vector4.Transform(new Vector4(world, 1f), projection.ViewProjection);
                if (clip.W <= 0.001f)
                {
                    continue;
                }

                float ndcX = clip.X / clip.W;
                float ndcY = clip.Y / clip.W;
                float screenX = (ndcX + 1f) * 0.5f * projection.Resolution.X;
                float screenY = (1f - ndcY) * 0.5f * projection.Resolution.Y;
                left = MathF.Min(left, screenX);
                top = MathF.Min(top, screenY);
                right = MathF.Max(right, screenX);
                bottom = MathF.Max(bottom, screenY);
                projectedCornerCount++;
            }

            if (projectedCornerCount == 0 ||
                right <= 0f || bottom <= 0f ||
                left >= projection.Resolution.X || top >= projection.Resolution.Y)
            {
                return false;
            }

            bounds = new ScreenBounds(
                Math.Clamp(left, 0f, projection.Resolution.X),
                Math.Clamp(top, 0f, projection.Resolution.Y),
                Math.Clamp(right, 0f, projection.Resolution.X),
                Math.Clamp(bottom, 0f, projection.Resolution.Y));
            return bounds.Width > 0f && bounds.Height > 0f;
        }

        private readonly record struct ScreenBounds(float Left, float Top, float Right, float Bottom)
        {
            public float Width => Right - Left;
            public float Height => Bottom - Top;

            public ScreenBounds Union(in ScreenBounds other) => new(
                MathF.Min(Left, other.Left),
                MathF.Min(Top, other.Top),
                MathF.Max(Right, other.Right),
                MathF.Max(Bottom, other.Bottom));
        }

        private struct InstanceProjection
        {
            public InstanceProjection(int templateId, bool onscreen, in ScreenBounds bounds)
            {
                TemplateId = templateId;
                Onscreen = onscreen;
                Bounds = bounds;
            }

            public int TemplateId;
            public bool Onscreen;
            public ScreenBounds Bounds;

            public void Include(in ScreenBounds bounds)
            {
                Bounds = Onscreen ? Bounds.Union(in bounds) : bounds;
                Onscreen = true;
            }
        }

        private struct TemplateProjection
        {
            public TemplateProjection(int templateId)
            {
                TemplateId = templateId;
                SubmittedCount = 0;
                OnscreenCount = 0;
                MinimumShortEdgePx = float.PositiveInfinity;
                MinimumAreaPx2 = float.PositiveInfinity;
            }

            public int TemplateId;
            public int SubmittedCount;
            public int OnscreenCount;
            public float MinimumShortEdgePx;
            public float MinimumAreaPx2;

            public void Include(in InstanceProjection instance)
            {
                SubmittedCount++;
                if (!instance.Onscreen)
                {
                    return;
                }

                OnscreenCount++;
                MinimumShortEdgePx = MathF.Min(
                    MinimumShortEdgePx,
                    MathF.Min(instance.Bounds.Width, instance.Bounds.Height));
                MinimumAreaPx2 = MathF.Min(
                    MinimumAreaPx2,
                    instance.Bounds.Width * instance.Bounds.Height);
            }

            public readonly PresentationTemplateReceiptSummary ToSummary() => new(
                TemplateId,
                SubmittedCount,
                OnscreenCount,
                OnscreenCount > 0 ? MinimumShortEdgePx : 0f,
                OnscreenCount > 0 ? MinimumAreaPx2 : 0f);
        }
    }
}
