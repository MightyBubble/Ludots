using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using EntityCommandPanelMod.Runtime;
using EntityInfoPanelsMod;
using EntityInfoPanelsMod.Commands;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Commands;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Scripting;
using Ludots.Core.UI.EntityCommandPanels;

namespace EntityCommandPanelShowcaseMod.Runtime
{
    internal sealed class EntityCommandPanelShowcaseRuntime
    {
        private const string AggregationAlias = EntityCommandPanelShowcaseIds.AggregationAlias;
        private const string ByTemplateProfileId = EntityCommandPanelShowcaseIds.ByTemplateProfileId;
        private const string ByFamilyProfileId = EntityCommandPanelShowcaseIds.ByFamilyProfileId;
        private const string ByAbilityIdProfileId = EntityCommandPanelShowcaseIds.ByAbilityIdProfileId;
        private const string ArcweaverAlias = "showcase.arcweaver";
        private const string VanguardAlias = "showcase.vanguard";
        private const string CommanderAlias = "showcase.commander";
        private const string FormsAlias = "showcase.forms";
        private const string FocusAlias = "showcase.focus";
        private const string AutoProfileTimelineEnvKey = "LUDOTS_ENTITY_COMMAND_PANEL_AUTO_PROFILE_TIMELINE";
        private const int AutoProfileSegmentFrames = 90;

        private readonly AggregationProfileToolbarProvider _aggregationToolbar = new();
        private IEntityCommandPanelToolbarProvider? _previousToolbarProvider;
        private bool _toolbarInstalled;
        private bool _showcaseHudSuppressed;
        private int _autoProfileTimelineFrame;

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (!InteractionShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                DisableShowcase(context, engine);
                return Task.CompletedTask;
            }

            EnableShowcase(context, engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            DisableShowcase(context, engine);
            return Task.CompletedTask;
        }

        public void Update(GameEngine engine)
        {
            if (engine == null || !InteractionShowcaseIds.IsShowcaseMap(engine.CurrentMapSession?.MapId.Value))
            {
                return;
            }

            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = true;
            TryPublishAggregationCommandCollection(engine, out _);
            UpdateAutoProfileTimeline();
        }

        private void EnableShowcase(ScriptContext context, GameEngine engine)
        {
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = true;
            SuppressNonEssentialHud(engine);
            CloseInteractionEntityInfoPanels(context);
            ClosePinnedPanels(context);
            _autoProfileTimelineFrame = 0;
            InstallAggregationProfileToolbar(engine);
            if (ReadEnvBoolOrDefault(AutoProfileTimelineEnvKey, defaultValue: false))
            {
                _aggregationToolbar.ActivateByIndex(0);
            }

            TryPublishAggregationCommandCollection(engine, out _);
            _aggregationToolbar.SetVisible(false);
        }

        private void DisableShowcase(ScriptContext context, GameEngine engine)
        {
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = false;
            RestoreSuppressedHud(engine);
            UninstallAggregationProfileToolbar(engine);
            ClosePinnedPanels(context);
            _autoProfileTimelineFrame = 0;
        }

        private void UpdateAutoProfileTimeline()
        {
            if (!ReadEnvBoolOrDefault(AutoProfileTimelineEnvKey, defaultValue: false))
            {
                return;
            }

            _autoProfileTimelineFrame++;
            int profileIndex = Math.Min(
                (_autoProfileTimelineFrame - 1) / AutoProfileSegmentFrames,
                AggregationProfileToolbarProvider.ProfileCount - 1);
            _aggregationToolbar.ActivateByIndex(profileIndex);
        }

        private static void ClosePinnedPanels(ScriptContext context)
        {
            Execute(context, new CloseEntityCommandPanelCommand(ArcweaverAlias));
            Execute(context, new CloseEntityCommandPanelCommand(VanguardAlias));
            Execute(context, new CloseEntityCommandPanelCommand(CommanderAlias));
            Execute(context, new CloseEntityCommandPanelCommand(FormsAlias));
            Execute(context, new CloseEntityCommandPanelCommand(FocusAlias));
            Execute(context, new CloseEntityCommandPanelCommand(AggregationAlias));
        }

        private void SuppressNonEssentialHud(GameEngine engine)
        {
            if (_showcaseHudSuppressed)
            {
                return;
            }

            engine.GlobalContext[ViewModeSwitchSystem.ViewModeHudEnabledKey] = false;
            engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = false;
            _showcaseHudSuppressed = true;
        }

        private void RestoreSuppressedHud(GameEngine engine)
        {
            if (!_showcaseHudSuppressed)
            {
                return;
            }

            engine.GlobalContext[ViewModeSwitchSystem.ViewModeHudEnabledKey] = true;
            engine.GlobalContext[SkillBarOverlaySystem.SkillBarEnabledKey] = true;
            _showcaseHudSuppressed = false;
        }

        private static bool ReadEnvBoolOrDefault(string key, bool defaultValue)
        {
            string? raw = Environment.GetEnvironmentVariable(key);
            if (string.IsNullOrWhiteSpace(raw))
            {
                return defaultValue;
            }

            return raw.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   raw.Equals("on", StringComparison.OrdinalIgnoreCase);
        }

        private void InstallAggregationProfileToolbar(GameEngine engine)
        {
            CollectionGasEntityCommandPanelSource source = ResolveCollectionSource(engine);
            _aggregationToolbar.Bind(source);
            _aggregationToolbar.SetVisible(false);

            if (_toolbarInstalled)
            {
                return;
            }

            IEntityCommandPanelToolbarProvider? existing = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider);
            if (!ReferenceEquals(existing, _aggregationToolbar))
            {
                _previousToolbarProvider = existing;
            }

            engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, _aggregationToolbar);
            _toolbarInstalled = true;
        }

        private void UninstallAggregationProfileToolbar(GameEngine engine)
        {
            _aggregationToolbar.SetVisible(false);
            if (!_toolbarInstalled)
            {
                return;
            }

            IEntityCommandPanelToolbarProvider? current = engine.GetService(CoreServiceKeys.EntityCommandPanelToolbarProvider);
            if (ReferenceEquals(current, _aggregationToolbar))
            {
                if (_previousToolbarProvider != null)
                {
                    engine.SetService(CoreServiceKeys.EntityCommandPanelToolbarProvider, _previousToolbarProvider);
                }
                else
                {
                    engine.RemoveService(CoreServiceKeys.EntityCommandPanelToolbarProvider);
                }
            }

            _previousToolbarProvider = null;
            _toolbarInstalled = false;
        }

        private static CollectionGasEntityCommandPanelSource ResolveCollectionSource(GameEngine engine)
        {
            var registry = engine.GetService(CoreServiceKeys.EntityCommandPanelSourceRegistry)
                ?? throw new InvalidOperationException("EntityCommandPanelSourceRegistry must be registered before the showcase publishes aggregation profiles.");
            if (!registry.TryGet(CollectionGasEntityCommandPanelSource.SourceId, out IEntityCommandPanelSource source))
            {
                throw new InvalidOperationException(
                    $"Entity command panel source '{CollectionGasEntityCommandPanelSource.SourceId}' is not registered.");
            }

            return source as CollectionGasEntityCommandPanelSource
                ?? throw new InvalidOperationException(
                    $"Entity command panel source '{CollectionGasEntityCommandPanelSource.SourceId}' must be CollectionGasEntityCommandPanelSource.");
        }

        private static bool TryPublishAggregationCommandCollection(GameEngine engine, out Entity aggregationOwner)
        {
            aggregationOwner = Entity.Null;
            if (!engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer) ||
                localPlayer == Entity.Null ||
                !engine.World.IsAlive(localPlayer))
            {
                return false;
            }

            var collections = engine.GetService(CoreServiceKeys.EntityCollectionStore)
                ?? throw new InvalidOperationException("EntityCollectionStore must be registered before the showcase publishes aggregation collections.");

            Span<Entity> members = stackalloc Entity[3];
            int count = 0;
            AddIfResolved(engine, InteractionShowcaseIds.ArcweaverName, members, ref count);
            AddIfResolved(engine, InteractionShowcaseIds.VanguardName, members, ref count);
            AddIfResolved(engine, InteractionShowcaseIds.CommanderName, members, ref count);
            if (count == 0)
            {
                return false;
            }

            var descriptor = EntityCollectionDescriptor.Create(
                EntityCollectionKeys.CommandSource,
                EntityCollectionSourceKind.Explicit,
                EntityCollectionRoleKind.CommandSource,
                localPlayer,
                members[0],
                "M6 Aggregation Profiles",
                $"{count.ToString(System.Globalization.CultureInfo.InvariantCulture)} showcase command owners");
            collections.Replace(localPlayer, descriptor, members[..count]);
            aggregationOwner = localPlayer;
            return true;
        }

        private static void AddIfResolved(GameEngine engine, string entityName, Span<Entity> destination, ref int count)
        {
            if ((uint)count >= (uint)destination.Length)
            {
                return;
            }

            Entity entity = ResolveTargetEntity(engine, entityName);
            if (entity != Entity.Null)
            {
                destination[count++] = entity;
            }
        }

        private static void CloseInteractionEntityInfoPanels(ScriptContext context)
        {
            if (context.Get(EntityInfoPanelServiceKeys.HandleStore) is not EntityInfoPanelHandleStore handles)
            {
                return;
            }

            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedComponentUiHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedGasUiHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.SelectedGasOverlayHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.ArcweaverOverlayHandleKey);
            CloseEntityInfoHandle(context, handles, InteractionShowcaseIds.VanguardOverlayHandleKey);
        }

        private static void CloseEntityInfoHandle(
            ScriptContext context,
            EntityInfoPanelHandleStore handles,
            string handleKey)
        {
            if (!handles.TryGet(handleKey, out _))
            {
                return;
            }

            new CloseEntityInfoPanelCommand
            {
                HandleSlotKey = handleKey
            }.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private static Entity ResolveTargetEntity(GameEngine engine, string entityName)
        {
            Entity result = Entity.Null;
            var query = new QueryDescription().WithAll<Name>();
            engine.World.Query(in query, (Entity entity, ref Name name) =>
            {
                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    result = entity;
                }
            });
            return result;
        }

        private static void Execute(ScriptContext context, GameCommand command)
        {
            command.ExecuteAsync(context).GetAwaiter().GetResult();
        }

        private sealed class AggregationProfileToolbarProvider : IEntityCommandPanelToolbarProvider
        {
            private static readonly AggregationProfileOption[] Profiles =
            {
                new("profile.by_template", "Template", ByTemplateProfileId, "#6EC6FF"),
                new("profile.by_family", "Family", ByFamilyProfileId, "#F6D37A"),
                new("profile.by_ability_id", "Ability", ByAbilityIdProfileId, "#9EE493")
            };

            public static int ProfileCount => Profiles.Length;

            private CollectionGasEntityCommandPanelSource? _source;
            private int _activeIndex = 1;
            private uint _revision = 1;
            private bool _visible;

            public bool IsVisible => _visible;
            public uint Revision => _revision;
            public string Title => "Aggregation Profile";
            public string Subtitle => $"M6/P3 grouping: {Profiles[_activeIndex].Label}";
            public string ActiveProfileLabel => Profiles[_activeIndex].Label;

            public void Bind(CollectionGasEntityCommandPanelSource source)
            {
                _source = source ?? throw new ArgumentNullException(nameof(source));
                ApplyActiveProfile();
            }

            public void SetVisible(bool visible)
            {
                if (_visible == visible)
                {
                    return;
                }

                _visible = visible;
                BumpRevision();
            }

            public int CopyButtons(Span<EntityCommandPanelToolbarButtonView> destination)
            {
                int count = Math.Min(destination.Length, Profiles.Length);
                for (int i = 0; i < count; i++)
                {
                    AggregationProfileOption profile = Profiles[i];
                    destination[i] = new EntityCommandPanelToolbarButtonView(
                        profile.ButtonId,
                        profile.Label,
                        i == _activeIndex,
                        profile.AccentColorHex);
                }

                return count;
            }

            public void Activate(string buttonId)
            {
                for (int i = 0; i < Profiles.Length; i++)
                {
                    if (!string.Equals(Profiles[i].ButtonId, buttonId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ActivateByIndex(i);
                    return;
                }

                throw new InvalidOperationException($"Unknown entity command panel aggregation profile button '{buttonId}'.");
            }

            public void ActivateByIndex(int index)
            {
                if ((uint)index >= (uint)Profiles.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                if (_activeIndex == index)
                {
                    return;
                }

                _activeIndex = index;
                ApplyActiveProfile();
                BumpRevision();
            }

            private void ApplyActiveProfile()
            {
                if (_source == null)
                {
                    throw new InvalidOperationException("Aggregation profile toolbar is not bound to a collection source.");
                }

                _source.SetAggregationProfile(Profiles[_activeIndex].ProfileId);
            }

            private void BumpRevision()
            {
                unchecked
                {
                    _revision++;
                    if (_revision == 0)
                    {
                        _revision = 1;
                    }
                }
            }

            private readonly struct AggregationProfileOption
            {
                public AggregationProfileOption(string buttonId, string label, string profileId, string accentColorHex)
                {
                    ButtonId = buttonId;
                    Label = label;
                    ProfileId = profileId;
                    AccentColorHex = accentColorHex;
                }

                public string ButtonId { get; }
                public string Label { get; }
                public string ProfileId { get; }
                public string AccentColorHex { get; }
            }
        }

    }
}
