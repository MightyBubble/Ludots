using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Numerics;
using System.Security.Cryptography;
using Ludots.Core.Presentation.Assets;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Diagnostics
{
    public readonly record struct PresentationFrameReceiptItem(
        int OwnerStableId,
        int VisualStableId,
        int TemplateId,
        Vector3 WorldPosition,
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

    public readonly record struct PresentationOnscreenStateReceipt(
        int SubmissionCount,
        string StateSha256);

    public readonly record struct PresentationOnscreenInstanceReceipt(
        int OwnerStableId,
        int VisualStableId,
        int TemplateId,
        int WorldXCm,
        int WorldYCm,
        float ScreenLeftPx,
        float ScreenTopPx,
        float ScreenRightPx,
        float ScreenBottomPx)
    {
        public float ShortEdgePx =>
            MathF.Min(ScreenRightPx - ScreenLeftPx, ScreenBottomPx - ScreenTopPx);

        public float AreaPx2 =>
            (ScreenRightPx - ScreenLeftPx) * (ScreenBottomPx - ScreenTopPx);
    }

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
            int ownerStableId,
            int visualStableId,
            int templateId,
            in Vector3 worldPosition,
            in Vector3 position,
            in Quaternion rotation,
            in Vector3 scale,
            in ProceduralMeshBounds localBounds)
        {
            if (ownerStableId == 0 && visualStableId == 0 && templateId == 0)
            {
                return;
            }
            if (ownerStableId <= 0)
            {
                throw new InvalidOperationException("Presentation submission receipts require a positive owner stable id.");
            }
            if (visualStableId <= 0)
            {
                throw new InvalidOperationException("Presentation submission receipts require a positive visual stable id.");
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
            if (localBounds.Extents.X <= 0f ||
                localBounds.Extents.Y <= 0f ||
                localBounds.Extents.Z <= 0f)
            {
                throw new InvalidOperationException(
                    "Presentation submission receipts require explicit positive local bounds.");
            }

            _items[_count++] = new PresentationFrameReceiptItem(
                ownerStableId,
                visualStableId,
                templateId,
                worldPosition,
                position,
                rotation,
                scale,
                localBounds);
        }

        public ReadOnlySpan<PresentationFrameReceiptItem> GetSpan() =>
            new(_items, 0, _count);

        public PresentationTemplateReceiptSummary[] BuildTemplateSummaries(in ProjectionSnapshot projection)
        {
            var instances = new Dictionary<long, InstanceProjection>(_count);
            for (int i = 0; i < _count; i++)
            {
                ref readonly PresentationFrameReceiptItem item = ref _items[i];
                long key = ((long)item.TemplateId << 32) | (uint)item.VisualStableId;
                bool onscreen = TryProjectClippedBounds(in item, in projection, out ScreenBounds bounds);
                if (!instances.TryGetValue(key, out InstanceProjection instance))
                {
                    instances.Add(key, new InstanceProjection(in item, onscreen, in bounds));
                    continue;
                }

                instance.IncludeSource(in item);
                if (onscreen)
                {
                    instance.Include(in bounds);
                }
                instances[key] = instance;
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

        public PresentationOnscreenInstanceReceipt[] BuildOnscreenInstanceReceipts(
            in ProjectionSnapshot projection)
        {
            var instances = new Dictionary<long, InstanceProjection>(_count);
            for (int i = 0; i < _count; i++)
            {
                ref readonly PresentationFrameReceiptItem item = ref _items[i];
                long key = ((long)item.TemplateId << 32) | (uint)item.VisualStableId;
                bool onscreen = TryProjectClippedBounds(in item, in projection, out ScreenBounds bounds);
                if (!instances.TryGetValue(key, out InstanceProjection instance))
                {
                    instances.Add(key, new InstanceProjection(in item, onscreen, in bounds));
                    continue;
                }

                instance.IncludeSource(in item);
                if (onscreen)
                {
                    instance.Include(in bounds);
                }
                instances[key] = instance;
            }

            var result = new PresentationOnscreenInstanceReceipt[instances.Count];
            int resultIndex = 0;
            foreach (InstanceProjection instance in instances.Values)
            {
                if (instance.Onscreen)
                {
                    result[resultIndex++] = instance.ToOnscreenReceipt();
                }
            }
            if (resultIndex != result.Length)
            {
                Array.Resize(ref result, resultIndex);
            }
            Array.Sort(
                result,
                static (left, right) =>
                {
                    int comparison = left.TemplateId.CompareTo(right.TemplateId);
                    if (comparison != 0) return comparison;
                    comparison = left.OwnerStableId.CompareTo(right.OwnerStableId);
                    return comparison != 0
                        ? comparison
                        : left.VisualStableId.CompareTo(right.VisualStableId);
                });
            return result;
        }

        public PresentationOnscreenStateReceipt BuildOnscreenStateReceipt(in ProjectionSnapshot projection) =>
            BuildOnscreenStateReceipt(in projection, templateId: 0, filterByTemplate: false);

        public PresentationOnscreenStateReceipt BuildOnscreenStateReceipt(
            in ProjectionSnapshot projection,
            int templateId)
        {
            if (templateId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(templateId));
            }

            return BuildOnscreenStateReceipt(in projection, templateId, filterByTemplate: true);
        }

        private PresentationOnscreenStateReceipt BuildOnscreenStateReceipt(
            in ProjectionSnapshot projection,
            int templateId,
            bool filterByTemplate)
        {
            var onscreen = new PresentationFrameReceiptItem[_count];
            int onscreenCount = 0;
            for (int i = 0; i < _count; i++)
            {
                ref readonly PresentationFrameReceiptItem item = ref _items[i];
                if (filterByTemplate && item.TemplateId != templateId)
                {
                    continue;
                }
                if (!TryProjectClippedBounds(in item, in projection, out _))
                {
                    continue;
                }

                onscreen[onscreenCount++] = item;
            }

            Array.Sort(
                onscreen,
                index: 0,
                length: onscreenCount,
                comparer: PresentationFrameReceiptItemComparer.Instance);
            using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            Span<byte> field = stackalloc byte[sizeof(int)];
            BinaryPrimitives.WriteInt32LittleEndian(field, onscreenCount);
            hash.AppendData(field);
            for (int i = 0; i < onscreenCount; i++)
            {
                AppendCanonical(hash, field, in onscreen[i]);
            }

            return new PresentationOnscreenStateReceipt(
                onscreenCount,
                Convert.ToHexString(hash.GetHashAndReset()));
        }

        private static void AppendCanonical(
            IncrementalHash hash,
            Span<byte> field,
            in PresentationFrameReceiptItem item)
        {
            AppendInt32(hash, field, item.OwnerStableId);
            AppendInt32(hash, field, item.VisualStableId);
            AppendInt32(hash, field, item.TemplateId);
            Vector3 worldPosition = item.WorldPosition;
            Vector3 position = item.Position;
            Quaternion rotation = item.Rotation;
            Vector3 scale = item.Scale;
            AppendVector3(hash, field, in worldPosition);
            AppendVector3(hash, field, in position);
            AppendQuaternion(hash, field, in rotation);
            AppendVector3(hash, field, in scale);
            Vector3 min = item.LocalBounds.Min;
            Vector3 max = item.LocalBounds.Max;
            AppendVector3(hash, field, in min);
            AppendVector3(hash, field, in max);
        }

        private static void AppendVector3(IncrementalHash hash, Span<byte> field, in Vector3 value)
        {
            AppendSingle(hash, field, value.X);
            AppendSingle(hash, field, value.Y);
            AppendSingle(hash, field, value.Z);
        }

        private static void AppendQuaternion(IncrementalHash hash, Span<byte> field, in Quaternion value)
        {
            AppendSingle(hash, field, value.X);
            AppendSingle(hash, field, value.Y);
            AppendSingle(hash, field, value.Z);
            AppendSingle(hash, field, value.W);
        }

        private static void AppendSingle(IncrementalHash hash, Span<byte> field, float value) =>
            AppendInt32(hash, field, BitConverter.SingleToInt32Bits(value));

        private static void AppendInt32(IncrementalHash hash, Span<byte> field, int value)
        {
            BinaryPrimitives.WriteInt32LittleEndian(field, value);
            hash.AppendData(field);
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
                if (clip.W <= 0.001f || clip.Z < 0f || clip.Z > clip.W)
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
            public InstanceProjection(
                in PresentationFrameReceiptItem item,
                bool onscreen,
                in ScreenBounds bounds)
            {
                OwnerStableId = item.OwnerStableId;
                VisualStableId = item.VisualStableId;
                TemplateId = item.TemplateId;
                Onscreen = onscreen;
                Bounds = bounds;
                WorldPosition = item.WorldPosition;
            }

            public int OwnerStableId;
            public int VisualStableId;
            public int TemplateId;
            public bool Onscreen;
            public ScreenBounds Bounds;
            public Vector3 WorldPosition;

            public void IncludeSource(in PresentationFrameReceiptItem item)
            {
                if (item.OwnerStableId != OwnerStableId)
                {
                    throw new InvalidOperationException(
                        $"Presentation receipt parts for visualStableId={VisualStableId}, templateId={TemplateId} disagree on the owner stable id.");
                }
                if (item.WorldPosition != WorldPosition)
                {
                    throw new InvalidOperationException(
                        $"Presentation receipt parts for ownerStableId={OwnerStableId}, visualStableId={VisualStableId}, templateId={TemplateId} disagree on the entity world position.");
                }
            }

            public void Include(in ScreenBounds bounds)
            {
                Bounds = Onscreen ? Bounds.Union(in bounds) : bounds;
                Onscreen = true;
            }

            public readonly PresentationOnscreenInstanceReceipt ToOnscreenReceipt()
            {
                return new PresentationOnscreenInstanceReceipt(
                    OwnerStableId,
                    VisualStableId,
                    TemplateId,
                    checked((int)MathF.Round(WorldPosition.X * 100f)),
                    checked((int)MathF.Round(WorldPosition.Z * 100f)),
                    Bounds.Left,
                    Bounds.Top,
                    Bounds.Right,
                    Bounds.Bottom);
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

        private sealed class PresentationFrameReceiptItemComparer : IComparer<PresentationFrameReceiptItem>
        {
            public static readonly PresentationFrameReceiptItemComparer Instance = new();

            public int Compare(PresentationFrameReceiptItem left, PresentationFrameReceiptItem right)
            {
                int comparison = left.TemplateId.CompareTo(right.TemplateId);
                if (comparison != 0) return comparison;
                comparison = left.OwnerStableId.CompareTo(right.OwnerStableId);
                if (comparison != 0) return comparison;
                comparison = left.VisualStableId.CompareTo(right.VisualStableId);
                if (comparison != 0) return comparison;
                comparison = Compare(left.WorldPosition, right.WorldPosition);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Position, right.Position);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Rotation, right.Rotation);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Scale, right.Scale);
                if (comparison != 0) return comparison;
                Vector3 leftMin = left.LocalBounds.Min;
                Vector3 rightMin = right.LocalBounds.Min;
                comparison = Compare(leftMin, rightMin);
                if (comparison != 0) return comparison;
                Vector3 leftMax = left.LocalBounds.Max;
                Vector3 rightMax = right.LocalBounds.Max;
                return Compare(leftMax, rightMax);
            }

            private static int Compare(Vector3 left, Vector3 right)
            {
                int comparison = Compare(left.X, right.X);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Y, right.Y);
                return comparison != 0 ? comparison : Compare(left.Z, right.Z);
            }

            private static int Compare(Quaternion left, Quaternion right)
            {
                int comparison = Compare(left.X, right.X);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Y, right.Y);
                if (comparison != 0) return comparison;
                comparison = Compare(left.Z, right.Z);
                return comparison != 0 ? comparison : Compare(left.W, right.W);
            }

            private static int Compare(float left, float right) =>
                BitConverter.SingleToInt32Bits(left).CompareTo(BitConverter.SingleToInt32Bits(right));
        }
    }
}
