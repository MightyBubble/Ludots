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

    public enum EntityCommandPanelLayoutPreset : byte
    {
        Standard = 0,
        CommandDeck = 1,
        OrderMonitor = 2
    }

    [Flags]
    public enum EntityCommandSlotStateFlags : ushort
    {
        None = 0,
        Empty = 1 << 0,
        Base = 1 << 1,
        FormOverride = 1 << 2,
        GrantedOverride = 1 << 3,
        TemplateBacked = 1 << 4,
        Blocked = 1 << 5,
        Active = 1 << 6,
        PendingTarget = 1 << 7
    }

    public readonly struct EntityCommandPanelOpenRequest
    {
        public Entity TargetEntity { get; init; }
        public string SourceId { get; init; }
        public string InstanceKey { get; init; }
        public EntityCommandPanelAnchor Anchor { get; init; }
        public EntityCommandPanelSize Size { get; init; }
        public EntityCommandPanelLayoutPreset LayoutPreset { get; init; }
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
            EntityCommandPanelLayoutPreset layoutPreset,
            int groupIndex,
            bool visible)
        {
            Handle = handle;
            TargetEntity = targetEntity;
            SourceId = sourceId ?? string.Empty;
            InstanceKey = instanceKey ?? string.Empty;
            Anchor = anchor;
            Size = size;
            LayoutPreset = layoutPreset;
            GroupIndex = groupIndex;
            Visible = visible;
        }

        public EntityCommandPanelHandle Handle { get; }
        public Entity TargetEntity { get; }
        public string SourceId { get; }
        public string InstanceKey { get; }
        public EntityCommandPanelAnchor Anchor { get; }
        public EntityCommandPanelSize Size { get; }
        public EntityCommandPanelLayoutPreset LayoutPreset { get; }
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
            short chargesMax,
            string displayLabel = "",
            string detailLabel = "",
            string actionId = "")
        {
            SlotIndex = slotIndex;
            AbilityId = abilityId;
            TemplateEntityId = templateEntityId;
            StateFlags = stateFlags;
            CooldownPermille = cooldownPermille;
            ChargesCurrent = chargesCurrent;
            ChargesMax = chargesMax;
            DisplayLabel = displayLabel ?? string.Empty;
            DetailLabel = detailLabel ?? string.Empty;
            ActionId = actionId ?? string.Empty;
        }

        public int SlotIndex { get; }
        public int AbilityId { get; }
        public int TemplateEntityId { get; }
        public EntityCommandSlotStateFlags StateFlags { get; }
        public short CooldownPermille { get; }
        public short ChargesCurrent { get; }
        public short ChargesMax { get; }
        public string DisplayLabel { get; }
        public string DetailLabel { get; }
        public string ActionId { get; }
    }

    public enum EntityCommandPanelStatusKind : byte
    {
        ActiveAbility,
        ActiveEffect
    }

    public readonly struct EntityCommandPanelStatusView
    {
        public EntityCommandPanelStatusView(
            EntityCommandPanelStatusKind kind,
            short progressPermille,
            string label = "",
            string detail = "",
            string accentColorHex = "")
        {
            Kind = kind;
            ProgressPermille = progressPermille;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            AccentColorHex = accentColorHex ?? string.Empty;
        }

        public EntityCommandPanelStatusKind Kind { get; }
        public short ProgressPermille { get; }
        public string Label { get; }
        public string Detail { get; }
        public string AccentColorHex { get; }
    }

    public enum EntityCommandPanelQueueStage : byte
    {
        Active,
        Queued,
        Pending
    }

    public readonly struct EntityCommandPanelQueueItemView
    {
        public EntityCommandPanelQueueItemView(
            EntityCommandPanelQueueStage stage,
            string label = "",
            string detail = "",
            string accentColorHex = "")
        {
            Stage = stage;
            Label = label ?? string.Empty;
            Detail = detail ?? string.Empty;
            AccentColorHex = accentColorHex ?? string.Empty;
        }

        public EntityCommandPanelQueueStage Stage { get; }
        public string Label { get; }
        public string Detail { get; }
        public string AccentColorHex { get; }
    }

    public interface IEntityCommandPanelSource
    {
        bool TryGetRevision(Entity target, out uint revision);
        int GetGroupCount(Entity target);
        bool TryGetGroup(Entity target, int groupIndex, out EntityCommandPanelGroupView group);
        int CopySlots(Entity target, int groupIndex, Span<EntityCommandPanelSlotView> destination);
    }

    public interface IEntityCommandPanelSupplementalSource
    {
        int CopyStatuses(Entity target, Span<EntityCommandPanelStatusView> destination);
        int CopyQueueItems(Entity target, Span<EntityCommandPanelQueueItemView> destination);
    }

    public interface IEntityCommandPanelActionSource
    {
        bool ActivateSlot(Entity target, int groupIndex, int slotIndex);
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

    public readonly struct EntityCommandPanelToolbarButtonView
    {
        public EntityCommandPanelToolbarButtonView(string buttonId, string label, bool active, string accentColorHex)
        {
            ButtonId = buttonId ?? string.Empty;
            Label = label ?? string.Empty;
            Active = active;
            AccentColorHex = accentColorHex ?? string.Empty;
        }

        public string ButtonId { get; }
        public string Label { get; }
        public bool Active { get; }
        public string AccentColorHex { get; }
    }

    public interface IEntityCommandPanelToolbarProvider
    {
        bool IsVisible { get; }
        uint Revision { get; }
        string Title { get; }
        string Subtitle { get; }
        int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination);
        void Activate(string buttonId);
    }
}
