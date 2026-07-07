using System;
using System.Threading.Tasks;
using Arch.Core;
using CoreInputMod.Systems;
using InteractionShowcaseMod;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.EntityCollections;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Registry;
using Ludots.Core.Scripting;
using SuperweaponContextShowcaseMod.UI;

namespace SuperweaponContextShowcaseMod.Runtime
{
    internal sealed class SuperweaponContextShowcaseRuntime
    {
        private const string AutoConfirmFrameEnvKey = "LUDOTS_SUPERWEAPON_CONTEXT_AUTO_CONFIRM_FRAME";

        private bool _pendingTargetCommit;
        private bool _showcaseHudSuppressed;
        private int _autoConfirmFrame;
        private int _activeFrame;
        private readonly SuperweaponContextShowcasePanelController _panelController;

        public SuperweaponContextShowcaseState State { get; } = new();

        public SuperweaponContextShowcaseRuntime()
        {
            _panelController = new SuperweaponContextShowcasePanelController(State);
        }

        public Task HandleMapFocusedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine == null)
            {
                return Task.CompletedTask;
            }

            if (!string.Equals(engine.CurrentMapSession?.MapId.Value, InteractionShowcaseIds.HubMapId, StringComparison.OrdinalIgnoreCase))
            {
                Disable(engine);
                return Task.CompletedTask;
            }

            Enable(engine);
            return Task.CompletedTask;
        }

        public Task HandleMapUnloadedAsync(ScriptContext context)
        {
            var engine = context.GetEngine();
            if (engine != null)
            {
                Disable(engine);
            }

            return Task.CompletedTask;
        }

        private void Enable(GameEngine engine)
        {
            State.LocalPlayer = ResolveLocalPlayer(engine);
            State.Commander = ResolveNamedEntity(engine, InteractionShowcaseIds.CommanderName);
            State.Arcweaver = ResolveNamedEntity(engine, InteractionShowcaseIds.ArcweaverName);
            State.Vanguard = ResolveNamedEntity(engine, InteractionShowcaseIds.VanguardName);
            State.AbilityId = AbilityIdRegistry.GetId(SuperweaponContextShowcaseIds.AbilityId);

            if (State.LocalPlayer == Entity.Null ||
                State.Commander == Entity.Null ||
                State.Arcweaver == Entity.Null ||
                State.Vanguard == Entity.Null ||
                State.AbilityId <= 0)
            {
                Disable(engine);
                return;
            }

            int slotIndex = EnsureCommanderAbility(engine);
            StartSuperweaponExec(engine, slotIndex);

            State.IsActive = true;
            State.RoutedTargetCount = 0;
            State.ConfirmInputObserved = false;
            State.ConfirmEventPublished = false;
            State.ConfirmEventCount = 0;
            _activeFrame = 0;
            _autoConfirmFrame = ReadEnvIntOrDefault(AutoConfirmFrameEnvKey, -1);
            _pendingTargetCommit = true;
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = true;
            SuppressNonEssentialHud(engine);
            BumpRevision();
        }

        public void Update(GameEngine engine)
        {
            if (!State.IsActive ||
                State.LocalPlayer == Entity.Null ||
                State.Commander == Entity.Null ||
                !engine.World.IsAlive(State.Commander))
            {
                return;
            }

            var stack = engine.GetService(CoreServiceKeys.InteractionContextStack)
                ?? throw new InvalidOperationException("Superweapon context showcase requires InteractionContextStack.");
            if (!TryGetActiveSuperweaponFrame(stack, out _))
            {
                return;
            }

            if (_pendingTargetCommit)
            {
                CommitShowcaseTargets(engine);
                _pendingTargetCommit = false;
                BumpRevision();
            }
        }

        public void RefreshPanel(GameEngine engine)
        {
            _panelController.MountOrRefresh(engine);
        }

        public void UpdateInput(GameEngine engine)
        {
            if (!State.IsActive ||
                State.Commander == Entity.Null ||
                !engine.World.IsAlive(State.Commander) ||
                !engine.World.Has<AbilityExecInstance>(State.Commander) ||
                State.ConfirmEventPublished)
            {
                return;
            }

            var stack = engine.GetService(CoreServiceKeys.InteractionContextStack)
                ?? throw new InvalidOperationException("Superweapon context showcase requires InteractionContextStack.");
            if (!TryGetActiveSuperweaponFrame(stack, out _))
            {
                return;
            }

            if (TryRunAutoConfirm(engine))
            {
                return;
            }

            if (engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                throw new InvalidOperationException("Superweapon context showcase requires AuthoritativeInput.");
            }

            if (!input.PressedThisFrame(SuperweaponContextShowcaseIds.ConfirmActionId))
            {
                return;
            }

            PublishConfirmEvent(engine);
        }

        private bool TryRunAutoConfirm(GameEngine engine)
        {
            if (_autoConfirmFrame <= 0 || State.ConfirmEventPublished)
            {
                return false;
            }

            _activeFrame++;
            if (_activeFrame < _autoConfirmFrame)
            {
                return false;
            }

            PublishConfirmEvent(engine);
            return true;
        }

        private void PublishConfirmEvent(GameEngine engine)
        {
            State.ConfirmInputObserved = true;
            State.ConfirmEventPublished = true;
            State.ConfirmEventCount++;
            engine.EventBus.Publish(new GameplayEvent
            {
                TagId = TagRegistry.GetId(SuperweaponContextShowcaseIds.CompletedEventTag),
                Source = State.Commander
            });
            BumpRevision();
        }

        private void Disable(GameEngine engine)
        {
            if (State.Commander != Entity.Null && engine.World.IsAlive(State.Commander))
            {
                var orderTypeRegistry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                    ?? throw new InvalidOperationException("Superweapon context showcase requires OrderTypeRegistry.");
                OrderSubmitter.CancelAll(engine.World, State.Commander, orderTypeRegistry);
            }

            State.IsActive = false;
            State.RoutedTargetCount = 0;
            State.ConfirmInputObserved = false;
            State.ConfirmEventPublished = false;
            State.ConfirmEventCount = 0;
            _activeFrame = 0;
            _autoConfirmFrame = -1;
            _pendingTargetCommit = false;
            engine.GlobalContext[InteractionShowcaseIds.SuppressUiPanelKey] = false;
            RestoreSuppressedHud(engine);
            _panelController.Clear();
            BumpRevision();
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

        private int EnsureCommanderAbility(GameEngine engine)
        {
            if (State.AbilityId <= 0 || State.Commander == Entity.Null || !engine.World.IsAlive(State.Commander))
            {
                return -1;
            }

            if (!engine.World.Has<AbilityStateBuffer>(State.Commander))
            {
                engine.World.Add(State.Commander, new AbilityStateBuffer());
            }

            ref AbilityStateBuffer abilities = ref engine.World.Get<AbilityStateBuffer>(State.Commander);
            for (int i = 0; i < abilities.Count; i++)
            {
                if (abilities.Get(i).AbilityId == State.AbilityId)
                {
                    return i;
                }
            }

            int slotIndex = abilities.Count;
            if (slotIndex >= AbilityStateBuffer.CAPACITY)
            {
                slotIndex = AbilityStateBuffer.CAPACITY - 1;
                if (!engine.World.Has<GrantedSlotBuffer>(State.Commander))
                {
                    engine.World.Add(State.Commander, new GrantedSlotBuffer());
                }

                ref GrantedSlotBuffer granted = ref engine.World.Get<GrantedSlotBuffer>(State.Commander);
                granted.Grant(slotIndex, State.AbilityId, sourceTagId: 0);
                return slotIndex;
            }

            abilities.AddAbility(State.AbilityId);
            return slotIndex;
        }

        private void StartSuperweaponExec(GameEngine engine, int slotIndex)
        {
            if (State.Commander == Entity.Null || !engine.World.IsAlive(State.Commander) || slotIndex < 0)
            {
                return;
            }

            if (!engine.World.Has<OrderBuffer>(State.Commander))
            {
                engine.World.Add(State.Commander, OrderBuffer.CreateEmpty());
            }

            if (!engine.World.Has<BlackboardIntBuffer>(State.Commander))
            {
                engine.World.Add(State.Commander, new BlackboardIntBuffer());
            }

            var orderQueue = engine.GetService(CoreServiceKeys.OrderQueue)
                ?? throw new InvalidOperationException("Superweapon context showcase requires OrderQueue.");
            int castStartOrderTypeId = ResolveOrderTypeId(engine, "castAbility.Start");
            var order = new Order
            {
                Actor = State.Commander,
                OrderTypeId = castStartOrderTypeId,
                Args = new OrderArgs
                {
                    I0 = slotIndex
                }
            };

            if (!orderQueue.TryEnqueue(in order))
            {
                throw new InvalidOperationException("Superweapon context showcase failed to enqueue castAbility.Start.");
            }
        }

        private void CommitShowcaseTargets(GameEngine engine)
        {
            var writer = engine.GetService(CoreServiceKeys.ContextBoundCollectionWriter)
                ?? throw new InvalidOperationException("Superweapon context showcase requires ContextBoundCollectionWriter.");

            Span<Entity> targets = stackalloc Entity[2];
            targets[0] = State.Arcweaver;
            targets[1] = State.Vanguard;
            writer.CommitCast(State.LocalPlayer, targets, EntityCollectionSourceKind.UiAcquisition);
            State.RoutedTargetCount = targets.Length;
        }

        private bool TryGetActiveSuperweaponFrame(InteractionContextStack stack, out InteractionContextFrame frame)
        {
            if (!stack.TryPeek(out frame))
            {
                return false;
            }

            return frame.ContextEntity == State.Commander &&
                   frame.ContextId == stack.ContextIdRegistry.GetId(SuperweaponContextShowcaseIds.ContextProfileId);
        }

        private static int ResolveOrderTypeId(GameEngine engine, string orderTypeName)
        {
            var registry = engine.GetService(CoreServiceKeys.OrderTypeRegistry)
                ?? throw new InvalidOperationException("Superweapon context showcase requires OrderTypeRegistry.");
            int id = registry.GetId(orderTypeName);
            if (id <= 0)
            {
                throw new InvalidOperationException($"Order type '{orderTypeName}' is not registered.");
            }

            return id;
        }

        private static Entity ResolveLocalPlayer(GameEngine engine)
        {
            return engine.TryGetService(CoreServiceKeys.LocalPlayerEntity, out Entity localPlayer)
                ? localPlayer
                : Entity.Null;
        }

        private static Entity ResolveNamedEntity(GameEngine engine, string entityName)
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

        private void BumpRevision()
        {
            unchecked
            {
                State.Revision++;
                if (State.Revision == 0)
                {
                    State.Revision = 1;
                }
            }
        }

        private static int ReadEnvIntOrDefault(string key, int defaultValue)
        {
            return int.TryParse(Environment.GetEnvironmentVariable(key), out int value)
                ? value
                : defaultValue;
        }
    }
}
