using System;
using System.Collections.Generic;
using Arch.Core;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// CommandDeck display modes (WPK-3). Four presentation strategies over one ability-backed
    /// command pipeline — not four backends.
    /// </summary>
    public enum CommandDeckDisplayMode : byte
    {
        /// <summary>Player/faction/base/control-plane commands without a focused entity.</summary>
        Global = 0,

        /// <summary>Commands for one explicitly bound entity/view/command source.</summary>
        Entity = 1,

        /// <summary>Aggregate, filter, and sort commands from a collection/control-plane candidate set.</summary>
        AggregateFiltered = 2,

        /// <summary>Pinned while a visibility condition holds; removed/changed on the next revision when it fails.</summary>
        ConditionalPinned = 3
    }

    /// <summary>
    /// Candidate source kinds for a CommandDeck profile. Registry-style string ids in JSON map to these
    /// known kinds at install; unknown kinds fail fast.
    /// </summary>
    public enum CommandDeckSourceKind : byte
    {
        /// <summary>Local player rep as collection/control-plane owner (no focused entity required).</summary>
        LocalPlayerRep = 0,

        /// <summary>Explicit entity supplied by the binding context.</summary>
        ExplicitEntity = 1,

        /// <summary>EntityCollectionStore addressed by (owner, collectionKey) via sourceRef.</summary>
        EntityCollection = 2,

        /// <summary>ControlPlaneView members for the local player rep and collection key in sourceRef.</summary>
        ControlPlaneView = 3
    }

    /// <summary>JSON root for <c>UI/command_deck_profiles.json</c>.</summary>
    public sealed class CommandDeckProfilesConfig
    {
        public List<CommandDeckProfileDefinition> Profiles { get; set; }
    }

    /// <summary>
    /// One CommandDeck profile declaration. Stable ids only — display mode, source, filter,
    /// aggregation, route, and visibility are all data-driven references.
    /// </summary>
    public sealed class CommandDeckProfileDefinition
    {
        public string Id { get; set; } = string.Empty;
        public string DisplayMode { get; set; } = string.Empty;
        public string SourceKind { get; set; } = string.Empty;
        public string SourceRef { get; set; } = string.Empty;
        public string CommandPanelSourceId { get; set; } = string.Empty;
        public string FilterProfileId { get; set; } = string.Empty;
        public string AggregationProfileId { get; set; } = string.Empty;
        public string RouteProfileId { get; set; } = string.Empty;
        public string VisibilityConditionId { get; set; } = string.Empty;
        public string CategoryTagPrefix { get; set; } = string.Empty;
        public string Topic { get; set; } = string.Empty;
    }

    /// <summary>Installed CommandDeck profile (validated, immutable).</summary>
    public sealed class CommandDeckProfile
    {
        public CommandDeckProfile(
            string id,
            CommandDeckDisplayMode displayMode,
            CommandDeckSourceKind sourceKind,
            string sourceRef,
            string commandPanelSourceId,
            string filterProfileId,
            string aggregationProfileId,
            string routeProfileId,
            string visibilityConditionId,
            string categoryTagPrefix,
            string topic)
        {
            Id = RequireId(id, nameof(id));
            DisplayMode = displayMode;
            SourceKind = sourceKind;
            SourceRef = sourceRef?.Trim() ?? string.Empty;
            CommandPanelSourceId = RequireId(commandPanelSourceId, nameof(commandPanelSourceId));
            FilterProfileId = filterProfileId?.Trim() ?? string.Empty;
            AggregationProfileId = aggregationProfileId?.Trim() ?? string.Empty;
            RouteProfileId = routeProfileId?.Trim() ?? string.Empty;
            VisibilityConditionId = visibilityConditionId?.Trim() ?? string.Empty;
            CategoryTagPrefix = categoryTagPrefix?.Trim() ?? string.Empty;
            Topic = topic?.Trim() ?? string.Empty;
        }

        public string Id { get; }
        public CommandDeckDisplayMode DisplayMode { get; }
        public CommandDeckSourceKind SourceKind { get; }
        public string SourceRef { get; }
        public string CommandPanelSourceId { get; }
        public string FilterProfileId { get; }
        public string AggregationProfileId { get; }
        public string RouteProfileId { get; }
        public string VisibilityConditionId { get; }
        public string CategoryTagPrefix { get; }
        public string Topic { get; }

        private static string RequireId(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException($"{paramName} is required.", paramName);
            }

            string trimmed = value.Trim();
            if (!string.Equals(value, trimmed, StringComparison.Ordinal))
            {
                throw new ArgumentException($"{paramName} must not contain leading or trailing whitespace.", paramName);
            }

            return trimmed;
        }
    }

    /// <summary>
    /// Explicit binding inputs for one projection. Focused/current entity is always an explicit
    /// entity or view owner supplied by the caller.
    /// </summary>
    public readonly struct CommandDeckBindingContext
    {
        public CommandDeckBindingContext(
            Entity localPlayerRep,
            Entity focusedEntity,
            Entity collectionOwner,
            string instanceKey,
            bool visibilityConditionSatisfied)
        {
            LocalPlayerRep = localPlayerRep;
            FocusedEntity = focusedEntity;
            CollectionOwner = collectionOwner;
            InstanceKey = instanceKey ?? string.Empty;
            VisibilityConditionSatisfied = visibilityConditionSatisfied;
        }

        public Entity LocalPlayerRep { get; }
        public Entity FocusedEntity { get; }
        public Entity CollectionOwner { get; }
        public string InstanceKey { get; }
        public bool VisibilityConditionSatisfied { get; }
    }

    /// <summary>One projected CommandDeck entry for DataPlane / Web rendering.</summary>
    public readonly struct CommandDeckEntry
    {
        public CommandDeckEntry(
            int slotIndex,
            int abilityId,
            string actionId,
            string displayLabel,
            string categoryId,
            string status,
            string blockedReason,
            int ownerCount,
            string routeProfileId,
            int routedOwnerEntityId,
            int routedOwnerVersion,
            int routedSlotIndex,
            short lockoutPermille,
            short chargesCurrent,
            short chargesMax)
        {
            SlotIndex = slotIndex;
            AbilityId = abilityId;
            ActionId = actionId ?? string.Empty;
            DisplayLabel = displayLabel ?? string.Empty;
            CategoryId = categoryId ?? string.Empty;
            Status = status ?? string.Empty;
            BlockedReason = blockedReason ?? string.Empty;
            OwnerCount = ownerCount;
            RouteProfileId = routeProfileId ?? string.Empty;
            RoutedOwnerEntityId = routedOwnerEntityId;
            RoutedOwnerVersion = routedOwnerVersion;
            RoutedSlotIndex = routedSlotIndex;
            LockoutPermille = lockoutPermille;
            ChargesCurrent = chargesCurrent;
            ChargesMax = chargesMax;
        }

        public int SlotIndex { get; }
        public int AbilityId { get; }
        public string ActionId { get; }
        public string DisplayLabel { get; }
        public string CategoryId { get; }
        public string Status { get; }
        public string BlockedReason { get; }
        public int OwnerCount { get; }
        public string RouteProfileId { get; }
        public int RoutedOwnerEntityId { get; }
        public int RoutedOwnerVersion { get; }
        public int RoutedSlotIndex { get; }
        public short LockoutPermille { get; }
        public short ChargesCurrent { get; }
        public short ChargesMax { get; }
    }

    /// <summary>DataPlane payload contract for one CommandDeck revision.</summary>
    public sealed class CommandDeckSnapshot
    {
        public CommandDeckSnapshot(
            string profileId,
            CommandDeckDisplayMode displayMode,
            uint revision,
            bool visible,
            IReadOnlyList<CommandDeckEntry> entries)
        {
            ProfileId = profileId ?? string.Empty;
            DisplayMode = displayMode;
            Revision = revision;
            Visible = visible;
            Entries = entries ?? Array.Empty<CommandDeckEntry>();
        }

        public string ProfileId { get; }
        public CommandDeckDisplayMode DisplayMode { get; }
        public uint Revision { get; }
        public bool Visible { get; }
        public IReadOnlyList<CommandDeckEntry> Entries { get; }
    }

    /// <summary>
    /// One aggregation-group member before route selection. Used by
    /// <see cref="CommandDeckRouteResolver"/> so activation is profile-driven, not first-member.
    /// </summary>
    public readonly struct CommandDeckRouteMember
    {
        public CommandDeckRouteMember(Entity owner, int slotIndex)
        {
            Owner = owner;
            SlotIndex = slotIndex;
        }

        public Entity Owner { get; }
        public int SlotIndex { get; }
    }

    /// <summary>Resolved activation target for one aggregated cell.</summary>
    public readonly struct CommandDeckRouteTarget
    {
        public CommandDeckRouteTarget(Entity owner, int slotIndex)
        {
            Owner = owner;
            SlotIndex = slotIndex;
        }

        public Entity Owner { get; }
        public int SlotIndex { get; }
    }

    public static class CommandDeckSourceKindIds
    {
        public const string LocalPlayerRep = "localPlayerRep";
        public const string ExplicitEntity = "explicitEntity";
        public const string EntityCollection = "entityCollection";
        public const string ControlPlaneView = "controlPlaneView";
    }

    public static class CommandDeckDisplayModeIds
    {
        public const string Global = "global";
        public const string Entity = "entity";
        public const string AggregateFiltered = "aggregateFiltered";
        public const string ConditionalPinned = "conditionalPinned";
    }

    public static class CommandDeckVisibilityConditionIds
    {
        public const string Always = "condition.always";
        public const string Never = "condition.never";
        public const string BindingFlag = "condition.binding-flag";
    }
}
