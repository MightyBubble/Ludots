using System;
using System.Collections.Generic;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Registry;
using Ludots.Core.UI.EntityCommandPanels;

namespace Ludots.Core.UI.CommandDeck
{
    /// <summary>
    /// Installs and looks up CommandDeck profiles (WPK-3). Unknown display/source kinds and missing
    /// referenced aggregation/route/filter profile ids fail fast at install.
    /// </summary>
    public sealed class CommandDeckProfileRegistry
    {
        private readonly StringIntRegistry _profileIds;
        private readonly AbilityAggregationProfileRegistry? _aggregationProfiles;
        private readonly CastDispatchProfileRegistry? _routeProfiles;
        private readonly FilterProfileRegistry? _filterProfiles;
        private CommandDeckProfile?[] _profiles = new CommandDeckProfile?[8];

        public CommandDeckProfileRegistry(
            StringIntRegistry profileIdRegistry,
            AbilityAggregationProfileRegistry? aggregationProfiles = null,
            CastDispatchProfileRegistry? routeProfiles = null,
            FilterProfileRegistry? filterProfiles = null)
        {
            _profileIds = profileIdRegistry ?? throw new ArgumentNullException(nameof(profileIdRegistry));
            _aggregationProfiles = aggregationProfiles;
            _routeProfiles = routeProfiles;
            _filterProfiles = filterProfiles;
        }

        public StringIntRegistry ProfileIdRegistry => _profileIds;

        public void Install(CommandDeckProfilesConfig config)
        {
            ArgumentNullException.ThrowIfNull(config);
            CommandDeckProfileConfigLoader.Validate(config, nameof(CommandDeckProfilesConfig));
            for (int i = 0; i < config.Profiles.Count; i++)
            {
                InstallProfile(config.Profiles[i]);
            }
        }

        public bool IsInstalled(int profileId)
        {
            return profileId > 0 && profileId < _profiles.Length && _profiles[profileId] != null;
        }

        public bool TryGet(string profileId, out CommandDeckProfile profile)
        {
            profile = null!;
            if (!_profileIds.TryGetId(profileId, out int id) || !IsInstalled(id))
            {
                return false;
            }

            profile = _profiles[id]!;
            return true;
        }

        public CommandDeckProfile Require(string profileId)
        {
            if (!TryGet(profileId, out CommandDeckProfile profile))
            {
                throw new InvalidOperationException($"CommandDeck profile '{profileId}' is not installed.");
            }

            return profile;
        }

        public IReadOnlyList<CommandDeckProfile> CopyInstalled()
        {
            var list = new List<CommandDeckProfile>();
            for (int i = 1; i < _profiles.Length; i++)
            {
                if (_profiles[i] != null)
                {
                    list.Add(_profiles[i]!);
                }
            }

            return list;
        }

        private void InstallProfile(CommandDeckProfileDefinition definition)
        {
            CommandDeckDisplayMode displayMode = ParseDisplayMode(definition.Id, definition.DisplayMode);
            CommandDeckSourceKind sourceKind = ParseSourceKind(definition.Id, definition.SourceKind);
            ValidateModeRequirements(definition, displayMode, sourceKind);
            ValidateReferences(definition, displayMode);

            int id = _profileIds.Register(definition.Id);
            if (id >= _profiles.Length)
            {
                Array.Resize(ref _profiles, Math.Max(id + 1, _profiles.Length * 2));
            }

            if (_profiles[id] != null)
            {
                throw new InvalidOperationException($"CommandDeck profile '{definition.Id}' is already installed.");
            }

            _profiles[id] = new CommandDeckProfile(
                definition.Id,
                displayMode,
                sourceKind,
                definition.SourceRef ?? string.Empty,
                definition.CommandPanelSourceId,
                definition.FilterProfileId ?? string.Empty,
                definition.AggregationProfileId ?? string.Empty,
                definition.RouteProfileId ?? string.Empty,
                definition.VisibilityConditionId ?? string.Empty,
                definition.CategoryTagPrefix ?? string.Empty,
                definition.Topic ?? string.Empty);
        }

        private void ValidateModeRequirements(
            CommandDeckProfileDefinition definition,
            CommandDeckDisplayMode displayMode,
            CommandDeckSourceKind sourceKind)
        {
            switch (displayMode)
            {
                case CommandDeckDisplayMode.Global:
                    if (sourceKind is not (CommandDeckSourceKind.SolePossessedRep
                        or CommandDeckSourceKind.EntityCollection
                        or CommandDeckSourceKind.ControlPlaneView))
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' global mode requires solePossessedRep, entityCollection, or controlPlaneView sourceKind.");
                    }

                    break;

                case CommandDeckDisplayMode.Entity:
                    if (sourceKind != CommandDeckSourceKind.ExplicitEntity)
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' entity mode requires explicitEntity sourceKind.");
                    }

                    break;

                case CommandDeckDisplayMode.AggregateFiltered:
                    if (string.IsNullOrWhiteSpace(definition.AggregationProfileId))
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' aggregateFiltered mode requires aggregationProfileId.");
                    }

                    if (string.IsNullOrWhiteSpace(definition.RouteProfileId))
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' aggregateFiltered mode requires routeProfileId.");
                    }

                    if (sourceKind is not (CommandDeckSourceKind.EntityCollection
                        or CommandDeckSourceKind.ControlPlaneView
                        or CommandDeckSourceKind.SolePossessedRep))
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' aggregateFiltered mode requires a collection/control-plane sourceKind.");
                    }

                    break;

                case CommandDeckDisplayMode.ConditionalPinned:
                    if (string.IsNullOrWhiteSpace(definition.VisibilityConditionId))
                    {
                        throw new InvalidOperationException(
                            $"CommandDeck profile '{definition.Id}' conditionalPinned mode requires visibilityConditionId.");
                    }

                    break;
            }

            if (sourceKind is CommandDeckSourceKind.EntityCollection or CommandDeckSourceKind.ControlPlaneView
                or CommandDeckSourceKind.SolePossessedRep)
            {
                if (string.IsNullOrWhiteSpace(definition.SourceRef))
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' sourceKind '{definition.SourceKind}' requires sourceRef (collection/query key).");
                }
            }
        }

        private void ValidateReferences(CommandDeckProfileDefinition definition, CommandDeckDisplayMode displayMode)
        {
            if (!string.IsNullOrWhiteSpace(definition.AggregationProfileId))
            {
                if (_aggregationProfiles == null)
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references aggregationProfileId '{definition.AggregationProfileId}' but AbilityAggregationProfileRegistry was not supplied.");
                }

                if (!_aggregationProfiles.ProfileIdRegistry.TryGetId(definition.AggregationProfileId, out int aggId) ||
                    !_aggregationProfiles.IsInstalled(aggId))
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references unknown aggregation profile '{definition.AggregationProfileId}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.RouteProfileId))
            {
                if (_routeProfiles == null)
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references routeProfileId '{definition.RouteProfileId}' but CastDispatchProfileRegistry was not supplied.");
                }

                if (!_routeProfiles.ProfileIdRegistry.TryGetId(definition.RouteProfileId, out int routeId) ||
                    !_routeProfiles.IsInstalled(routeId))
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references unknown route profile '{definition.RouteProfileId}'.");
                }
            }

            if (!string.IsNullOrWhiteSpace(definition.FilterProfileId))
            {
                if (_filterProfiles == null)
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references filterProfileId '{definition.FilterProfileId}' but FilterProfileRegistry was not supplied.");
                }

                if (!_filterProfiles.ProfileIdRegistry.TryGetId(definition.FilterProfileId, out int filterId) ||
                    !_filterProfiles.IsInstalled(filterId))
                {
                    throw new InvalidOperationException(
                        $"CommandDeck profile '{definition.Id}' references unknown filter profile '{definition.FilterProfileId}'.");
                }
            }

            if (displayMode == CommandDeckDisplayMode.ConditionalPinned &&
                string.IsNullOrWhiteSpace(definition.VisibilityConditionId))
            {
                throw new InvalidOperationException(
                    $"CommandDeck profile '{definition.Id}' conditionalPinned mode requires visibilityConditionId.");
            }
        }

        private static CommandDeckDisplayMode ParseDisplayMode(string profileId, string value)
        {
            return value switch
            {
                CommandDeckDisplayModeIds.Global => CommandDeckDisplayMode.Global,
                CommandDeckDisplayModeIds.Entity => CommandDeckDisplayMode.Entity,
                CommandDeckDisplayModeIds.AggregateFiltered => CommandDeckDisplayMode.AggregateFiltered,
                CommandDeckDisplayModeIds.ConditionalPinned => CommandDeckDisplayMode.ConditionalPinned,
                _ => throw new InvalidOperationException(
                    $"CommandDeck profile '{profileId}' has unknown displayMode '{value}'.")
            };
        }

        private static CommandDeckSourceKind ParseSourceKind(string profileId, string value)
        {
            return value switch
            {
                CommandDeckSourceKindIds.SolePossessedRep => CommandDeckSourceKind.SolePossessedRep,
                CommandDeckSourceKindIds.ExplicitEntity => CommandDeckSourceKind.ExplicitEntity,
                CommandDeckSourceKindIds.EntityCollection => CommandDeckSourceKind.EntityCollection,
                CommandDeckSourceKindIds.ControlPlaneView => CommandDeckSourceKind.ControlPlaneView,
                _ => throw new InvalidOperationException(
                    $"CommandDeck profile '{profileId}' has unknown sourceKind '{value}'.")
            };
        }
    }
}
