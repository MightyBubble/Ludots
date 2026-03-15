using System;
using Arch.Core;

namespace Ludots.Core.UI.EntityCommandPanels
{
    public readonly record struct EntityCommandPanelHandle(int Slot, uint Generation)
    {
        public bool IsValid => Slot >= 0 && Generation != 0;

        public static EntityCommandPanelHandle Invalid { get; } = new(-1, 0);
    }

    public enum EntityCommandPanelAnchorPreset : byte
    {
        TopLeft,
        TopRight,
        BottomLeft,
        BottomRight,
        BottomCenter,
        Center
    }

    public readonly struct EntityCommandPanelAnchor
    {
        public EntityCommandPanelAnchor(EntityCommandPanelAnchorPreset preset, float offsetX, float offsetY)
        {
            Preset = preset;
            OffsetX = offsetX;
            OffsetY = offsetY;
        }

        public EntityCommandPanelAnchorPreset Preset { get; }
        public float OffsetX { get; }
        public float OffsetY { get; }
    }

    public readonly struct EntityCommandPanelSize
    {
        public EntityCommandPanelSize(float widthPx, float heightPx)
        {
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        public float WidthPx { get; }
        public float HeightPx { get; }
    }

    [Flags]
    public enum EntityCommandSlotStateFlags : ushort
    {
        None = 0,
        Empty = 1 << 0,
        Base = 1 << 1,
        FormOverride = 1 << 2,
        GrantedOverride = 1 << 3,
        TemplateBacked = 1 << 4
    }

    public readonly struct EntityCommandPanelOpenRequest
    {
        public Entity TargetEntity { get; init; }
        public string SourceId { get; init; }
        public string InstanceKey { get; init; }
        public EntityCommandPanelAnchor Anchor { get; init; }
        public EntityCommandPanelSize Size { get; init; }
        public int InitialGroupIndex { get; init; }
        public bool StartVisible { get; init; }
    }

    public readonly struct EntityCommandPanelInstanceState
    {
        public EntityCommandPanelInstanceState(
            EntityCommandPanelHandle handle,
            Entity targetEntity,
            string sourceId,
            string instanceKey,
            EntityCommandPanelAnchor anchor,
            EntityCommandPanelSize size,
            int groupIndex,
            bool visible)
        {
            Handle = handle;
            TargetEntity = targetEntity;
            SourceId = sourceId ?? string.Empty;
            InstanceKey = instanceKey ?? string.Empty;
            Anchor = anchor;
            Size = size;
            GroupIndex = groupIndex;
            Visible = visible;
        }

        public EntityCommandPanelHandle Handle { get; }
        public Entity TargetEntity { get; }
        public string SourceId { get; }
        public string InstanceKey { get; }
        public EntityCommandPanelAnchor Anchor { get; }
        public EntityCommandPanelSize Size { get; }
        public int GroupIndex { get; }
        public bool Visible { get; }
    }

    public readonly struct EntityCommandPanelGroupView
    {
        public EntityCommandPanelGroupView(int groupId, string groupLabel, byte slotCount)
        {
            GroupId = groupId;
            GroupLabel = groupLabel ?? string.Empty;
            SlotCount = slotCount;
        }

        public int GroupId { get; }
        public string GroupLabel { get; }
        public byte SlotCount { get; }
    }

    public readonly struct EntityCommandPanelSlotView
    {
        public EntityCommandPanelSlotView(
            int slotIndex,
            int abilityId,
            int templateEntityId,
            EntityCommandSlotStateFlags stateFlags,
            short cooldownPermille,
            short chargesCurrent,
            short chargesMax)
        {
            SlotIndex = slotIndex;
            AbilityId = abilityId;
            TemplateEntityId = templateEntityId;
            StateFlags = stateFlags;
            CooldownPermille = cooldownPermille;
            ChargesCurrent = chargesCurrent;
            ChargesMax = chargesMax;
        }

        public int SlotIndex { get; }
        public int AbilityId { get; }
        public int TemplateEntityId { get; }
        public EntityCommandSlotStateFlags StateFlags { get; }
        public short CooldownPermille { get; }
        public short ChargesCurrent { get; }
        public short ChargesMax { get; }
    }

    public interface IEntityCommandPanelSource
    {
        bool TryGetRevision(Entity target, out uint revision);
        int GetGroupCount(Entity target);
        bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group);
        int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination);
    }

    public interface IEntityCommandPanelSourceRegistry
    {
        void Register(string sourceId, IEntityCommandPanelSource source);
        bool TryGet(string sourceId, out IEntityCommandPanelSource source);
    }

    public interface IEntityCommandPanelHandleStore
    {
        bool TryBind(string alias, EntityCommandPanelHandle handle);
        bool TryGet(string alias, out EntityCommandPanelHandle handle);
        bool Remove(string alias);
    }

    public interface IEntityCommandPanelService
    {
        EntityCommandPanelHandle Open(in EntityCommandPanelOpenRequest request);
        bool Close(EntityCommandPanelHandle handle);
        bool SetVisible(EntityCommandPanelHandle handle, bool visible);
        bool RebindTarget(EntityCommandPanelHandle handle, Entity targetEntity);
        bool SetGroupIndex(EntityCommandPanelHandle handle, int groupIndex);
        bool CycleGroup(EntityCommandPanelHandle handle, int delta);
        bool SetAnchor(EntityCommandPanelHandle handle, in EntityCommandPanelAnchor anchor);
        bool SetSize(EntityCommandPanelHandle handle, in EntityCommandPanelSize size);
        bool TryGetState(EntityCommandPanelHandle handle, out EntityCommandPanelInstanceState state);
    }
}
