using System;
using System.Numerics;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Engine;
using Ludots.Core.Gameplay.Components;
using Ludots.Core.Gameplay.GAS;
using Ludots.Core.Gameplay.GAS.Components;
using Ludots.Core.Gameplay.GAS.Orders;
using Ludots.Core.Gameplay.GAS.Registry;
using Ludots.Core.Gameplay.Teams;
using Ludots.Core.Map;
using Ludots.Core.Scripting;

namespace InteractionShowcaseMod.Systems
{
    internal sealed class InteractionShowcaseAutoplaySystem : ISystem<float>
    {
        private const int FailureEventTimeoutTicks = 24;
        private const int DamageApplyTimeoutTicks = 48;
        private const float C1CastRangeCm = 600f;
        private const float C2StartAllyHealth = 200f;
        private const float C2HostileHealth = 400f;
        private const float C2DeadAllyHealth = 0f;
        private const float C3StartHostileMoveSpeed = 200f;
        private const float C3StartFriendlyMoveSpeed = 180f;
        private const float C3ExpectedHostileMoveSpeed = 80f;
        private const float C3ExpectedFriendlyMoveSpeed = 260f;

        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly int _attackDamageAttributeId;
        private readonly int _baseDamageAttributeId;
        private readonly int _healthAttributeId;
        private readonly int _armorAttributeId;
        private readonly int _manaAttributeId;
        private readonly int _moveSpeedAttributeId;
        private readonly int _empoweredTagId;
        private readonly int _silencedTagId;
        private readonly int _untargetableTagId;
        private readonly int _c3PolymorphedTagId;
        private readonly int _c3HastedTagId;
        private readonly int _c1DamageAmountKeyId;
        private readonly int _c1FinalDamageKeyId;
        private readonly QueryDescription _nameQuery = new QueryDescription().WithAll<Name>();

        private MapSession? _lastMapSession;
        private int _scenarioTick;
        private int _stepStartTick;
        private int _castSubmittedTick = -1;
        private int _lastConsumedFailTick = -1;

        private B1ShowcaseStep _b1Step;
        private C1ShowcaseStep _c1Step;
        private C2ShowcaseStep _c2Step;
        private C3ShowcaseStep _c3Step;
        private bool _castSubmitted;
        private bool _buffObserved;
        private bool _buffExpired;
        private bool _c1DamageApplied;
        private bool _c2Initialized;
        private bool _c2HealApplied;
        private bool _c3Initialized;
        private bool _c3HostilePolymorphApplied;
        private bool _c3FriendlyHasteApplied;
        private int _c1DamageAppliedTick = -1;
        private int _c2HealAppliedTick = -1;
        private int _c3HostilePolymorphAppliedTick = -1;
        private int _c3FriendlyHasteAppliedTick = -1;
        private float _c1DamageAmount;
        private float _c1FinalDamage;
        private float _c2HealAmount;
        private float _c3HostileMoveSpeed;
        private float _c3FriendlyMoveSpeed;
        private string _c1LastAttemptTargetName = string.Empty;
        private string _c2LastAttemptTargetName = string.Empty;
        private string _c3LastAttemptTargetName = string.Empty;

        public InteractionShowcaseAutoplaySystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
            _attackDamageAttributeId = AttributeRegistry.Register("AttackDamage");
            _baseDamageAttributeId = AttributeRegistry.Register("BaseDamage");
            _healthAttributeId = AttributeRegistry.Register("Health");
            _armorAttributeId = AttributeRegistry.Register("Armor");
            _manaAttributeId = AttributeRegistry.Register("Mana");
            _moveSpeedAttributeId = AttributeRegistry.Register("MoveSpeed");
            _empoweredTagId = TagRegistry.Register("Status.Empowered");
            _silencedTagId = TagRegistry.Register("Status.Silenced");
            _untargetableTagId = TagRegistry.Register("Status.Untargetable");
            _c3PolymorphedTagId = TagRegistry.Register("Status.Polymorphed");
            _c3HastedTagId = TagRegistry.Register("Status.Hasted");
            _c1DamageAmountKeyId = ConfigKeyRegistry.Register("Interaction.C1.DamageAmount");
            _c1FinalDamageKeyId = ConfigKeyRegistry.Register("Interaction.C1.FinalDamage");
        }

        public void Initialize()
        {
        }

        public void BeforeUpdate(in float dt)
        {
        }

        public void AfterUpdate(in float dt)
        {
        }

        public void Dispose()
        {
        }

        public void Update(in float dt)
        {
            MapSession? currentMapSession = _engine.CurrentMapSession;
            if (!ReferenceEquals(currentMapSession, _lastMapSession))
            {
                ResetRuntimeState(clearGlobals: true);
                _lastMapSession = currentMapSession;
            }

            string mapId = currentMapSession?.MapId.Value ?? string.Empty;
            if (!InteractionShowcaseIds.IsShowcaseMap(mapId))
            {
                ResetRuntimeState(clearGlobals: true);
                return;
            }

            _scenarioTick++;

            if (string.Equals(mapId, InteractionShowcaseIds.B1SelfBuffMapId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateB1Scenario();
                return;
            }

            if (string.Equals(mapId, InteractionShowcaseIds.C1HostileUnitDamageMapId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateC1Scenario();
                return;
            }

            if (string.Equals(mapId, InteractionShowcaseIds.C2FriendlyUnitHealMapId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateC2Scenario();
                return;
            }

            if (string.Equals(mapId, InteractionShowcaseIds.C3AnyUnitConditionalMapId, StringComparison.OrdinalIgnoreCase))
            {
                UpdateC3Scenario();
            }
        }

        private void UpdateB1Scenario()
        {
            bool hasHero = TryFindNamedEntity(InteractionShowcaseIds.HeroName, out Entity hero);
            float attackDamage = hasHero ? ReadAttribute(hero, _attackDamageAttributeId) : 0f;
            float mana = hasHero ? ReadAttribute(hero, _manaAttributeId) : 0f;
            bool hasEmpoweredTag = hasHero && HasTag(hero, _empoweredTagId);
            int empoweredCount = hasHero ? ReadTagCount(hero, _empoweredTagId) : 0;
            bool buffActive = attackDamage > 100.001f || empoweredCount > 0 || hasEmpoweredTag;

            if (buffActive)
            {
                _buffObserved = true;
            }
            else if (_buffObserved)
            {
                _buffExpired = true;
            }

            if (hasHero)
            {
                AdvanceB1Scenario(hero, buffActive);
                attackDamage = ReadAttribute(hero, _attackDamageAttributeId);
                mana = ReadAttribute(hero, _manaAttributeId);
                hasEmpoweredTag = HasTag(hero, _empoweredTagId);
                empoweredCount = ReadTagCount(hero, _empoweredTagId);
            }

            PublishB1RuntimeState(hasHero, attackDamage, mana, hasEmpoweredTag, empoweredCount);
        }

        private void UpdateC1Scenario()
        {
            bool hasHero = TryFindNamedEntity(InteractionShowcaseIds.HeroName, out Entity hero);
            bool hasPrimary = TryFindNamedEntity(InteractionShowcaseIds.C1PrimaryTargetName, out Entity primaryTarget);
            bool hasInvalid = TryFindNamedEntity(InteractionShowcaseIds.C1InvalidTargetName, out Entity invalidTarget);
            bool hasFar = TryFindNamedEntity(InteractionShowcaseIds.C1FarTargetName, out Entity farTarget);

            float heroBaseDamage = hasHero ? ReadAttribute(hero, _baseDamageAttributeId) : 0f;
            float heroMana = hasHero ? ReadAttribute(hero, _manaAttributeId) : 0f;
            float primaryHealth = hasPrimary ? ReadAttribute(primaryTarget, _healthAttributeId) : 0f;
            float primaryArmor = hasPrimary ? ReadAttribute(primaryTarget, _armorAttributeId) : 0f;
            float invalidHealth = hasInvalid ? ReadAttribute(invalidTarget, _healthAttributeId) : 0f;
            float farHealth = hasFar ? ReadAttribute(farTarget, _healthAttributeId) : 0f;
            _c1DamageAmount = hasPrimary ? ReadBlackboardFloat(primaryTarget, _c1DamageAmountKeyId) : 0f;
            _c1FinalDamage = hasPrimary ? ReadBlackboardFloat(primaryTarget, _c1FinalDamageKeyId) : 0f;

            if (!_c1DamageApplied &&
                hasPrimary &&
                primaryHealth <= 300.001f &&
                _c1DamageAmount >= 299.999f &&
                _c1FinalDamage >= 199.999f)
            {
                _c1DamageApplied = true;
                _c1DamageAppliedTick = _engine.GameSession.CurrentTick;
            }

            if (hasHero)
            {
                AdvanceC1Scenario(
                    hero,
                    hasPrimary ? primaryTarget : Entity.Null,
                    hasInvalid ? invalidTarget : Entity.Null,
                    hasFar ? farTarget : Entity.Null);
            }

            PublishC1RuntimeState(hasHero, heroBaseDamage, heroMana, primaryHealth, primaryArmor, invalidHealth, farHealth);
        }

        private void UpdateC2Scenario()
        {
            bool hasHero = TryFindNamedEntity(InteractionShowcaseIds.HeroName, out Entity hero);
            bool hasAlly = TryFindNamedEntity(InteractionShowcaseIds.C2AllyTargetName, out Entity allyTarget);
            bool hasHostile = TryFindNamedEntity(InteractionShowcaseIds.C2HostileTargetName, out Entity hostileTarget);
            bool hasDeadAlly = TryFindNamedEntity(InteractionShowcaseIds.C2DeadAllyTargetName, out Entity deadAllyTarget);

            if (!_c2Initialized)
            {
                if (hasAlly)
                {
                    SetAttributeCurrent(allyTarget, _healthAttributeId, C2StartAllyHealth);
                }

                if (hasHostile)
                {
                    SetAttributeCurrent(hostileTarget, _healthAttributeId, C2HostileHealth);
                }

                if (hasDeadAlly)
                {
                    SetAttributeCurrent(deadAllyTarget, _healthAttributeId, C2DeadAllyHealth);
                }

                _c2Initialized = hasAlly && hasHostile && hasDeadAlly;
            }

            float heroMana = hasHero ? ReadAttribute(hero, _manaAttributeId) : 0f;
            float allyHealth = hasAlly ? ReadAttribute(allyTarget, _healthAttributeId) : 0f;
            float hostileHealth = hasHostile ? ReadAttribute(hostileTarget, _healthAttributeId) : 0f;
            float deadAllyHealth = hasDeadAlly ? ReadAttribute(deadAllyTarget, _healthAttributeId) : 0f;

            if (!_c2HealApplied && hasAlly && allyHealth >= 349.999f)
            {
                _c2HealApplied = true;
                _c2HealAppliedTick = _engine.GameSession.CurrentTick;
                _c2HealAmount = allyHealth - C2StartAllyHealth;
            }

            if (hasHero && hasAlly && hasHostile && hasDeadAlly)
            {
                AdvanceC2Scenario(
                    hero,
                    allyTarget,
                    hostileTarget,
                    deadAllyTarget);
            }

            PublishC2RuntimeState(hasHero, heroMana, allyHealth, hostileHealth, deadAllyHealth);
        }

        private void UpdateC3Scenario()
        {
            bool hasHero = TryFindNamedEntity(InteractionShowcaseIds.HeroName, out Entity hero);
            bool hasHostile = TryFindNamedEntity(InteractionShowcaseIds.C3HostileTargetName, out Entity hostileTarget);
            bool hasFriendly = TryFindNamedEntity(InteractionShowcaseIds.C3FriendlyTargetName, out Entity friendlyTarget);

            if (!_c3Initialized)
            {
                if (hasHostile)
                {
                    SetAttributeCurrent(hostileTarget, _moveSpeedAttributeId, C3StartHostileMoveSpeed);
                }

                if (hasFriendly)
                {
                    SetAttributeCurrent(friendlyTarget, _moveSpeedAttributeId, C3StartFriendlyMoveSpeed);
                }

                _c3Initialized = hasHostile && hasFriendly;
            }

            float heroMana = hasHero ? ReadAttribute(hero, _manaAttributeId) : 0f;
            float hostileMoveSpeed = hasHostile ? ReadAttribute(hostileTarget, _moveSpeedAttributeId) : 0f;
            float friendlyMoveSpeed = hasFriendly ? ReadAttribute(friendlyTarget, _moveSpeedAttributeId) : 0f;
            bool hostilePolymorphActive = hasHostile && HasTag(hostileTarget, _c3PolymorphedTagId);
            int hostilePolymorphCount = hasHostile ? ReadTagCount(hostileTarget, _c3PolymorphedTagId) : 0;
            bool friendlyHasteActive = hasFriendly && HasTag(friendlyTarget, _c3HastedTagId);
            int friendlyHasteCount = hasFriendly ? ReadTagCount(friendlyTarget, _c3HastedTagId) : 0;

            if (!_c3HostilePolymorphApplied &&
                hostilePolymorphActive &&
                hostilePolymorphCount > 0 &&
                hostileMoveSpeed <= C3ExpectedHostileMoveSpeed + 0.001f)
            {
                _c3HostilePolymorphApplied = true;
                _c3HostilePolymorphAppliedTick = _engine.GameSession.CurrentTick;
            }

            if (!_c3FriendlyHasteApplied &&
                friendlyHasteActive &&
                friendlyHasteCount > 0 &&
                friendlyMoveSpeed >= C3ExpectedFriendlyMoveSpeed - 0.001f)
            {
                _c3FriendlyHasteApplied = true;
                _c3FriendlyHasteAppliedTick = _engine.GameSession.CurrentTick;
            }

            if (hasHero && hasHostile && hasFriendly)
            {
                AdvanceC3Scenario(hero, hostileTarget, friendlyTarget);
            }

            PublishC3RuntimeState(
                hasHero,
                heroMana,
                hostileMoveSpeed,
                friendlyMoveSpeed,
                hostilePolymorphActive,
                hostilePolymorphCount,
                friendlyHasteActive,
                friendlyHasteCount);
        }

        private void AdvanceB1Scenario(Entity hero, bool buffActive)
        {
            switch (_b1Step)
            {
                case B1ShowcaseStep.Warmup:
                    if (_scenarioTick >= 5 && TrySubmitSelfCast(hero))
                    {
                        _castSubmitted = true;
                        _castSubmittedTick = _scenarioTick;
                        TransitionTo(B1ShowcaseStep.OrderSubmitted);
                    }
                    break;

                case B1ShowcaseStep.OrderSubmitted:
                    if (buffActive)
                    {
                        TransitionTo(B1ShowcaseStep.BuffActive);
                    }
                    break;

                case B1ShowcaseStep.BuffActive:
                    if (_buffObserved && !buffActive)
                    {
                        TransitionTo(B1ShowcaseStep.BuffExpired);
                    }
                    break;

                case B1ShowcaseStep.BuffExpired:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        ApplyTag(hero, _silencedTagId, add: true);
                        TransitionTo(B1ShowcaseStep.SilencedSetup);
                    }
                    break;

                case B1ShowcaseStep.SilencedSetup:
                    if (_scenarioTick - _stepStartTick >= 6 && TrySubmitSelfCast(hero))
                    {
                        TransitionTo(B1ShowcaseStep.SilencedAttemptSubmitted);
                    }
                    break;

                case B1ShowcaseStep.SilencedAttemptSubmitted:
                    if (ConsumeFailure("BlockedByTag"))
                    {
                        TransitionTo(B1ShowcaseStep.SilencedBlocked);
                        break;
                    }
                    ThrowIfFailureTimedOut("BlockedByTag");
                    break;

                case B1ShowcaseStep.SilencedBlocked:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        ApplyTag(hero, _silencedTagId, add: false);
                        SetAttributeBase(hero, _manaAttributeId, 0f);
                        TransitionTo(B1ShowcaseStep.InsufficientManaSetup);
                    }
                    break;

                case B1ShowcaseStep.InsufficientManaSetup:
                    if (_scenarioTick - _stepStartTick >= 6 && TrySubmitSelfCast(hero))
                    {
                        TransitionTo(B1ShowcaseStep.InsufficientManaAttemptSubmitted);
                    }
                    break;

                case B1ShowcaseStep.InsufficientManaAttemptSubmitted:
                    if (ConsumeFailure("InsufficientResource"))
                    {
                        TransitionTo(B1ShowcaseStep.InsufficientManaBlocked);
                        break;
                    }
                    ThrowIfFailureTimedOut("InsufficientResource");
                    break;

                case B1ShowcaseStep.InsufficientManaBlocked:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(B1ShowcaseStep.Complete);
                    }
                    break;
            }
        }

        private void AdvanceC1Scenario(Entity hero, Entity primaryTarget, Entity invalidTarget, Entity farTarget)
        {
            switch (_c1Step)
            {
                case C1ShowcaseStep.Warmup:
                    if (_scenarioTick >= 5 && TrySubmitC1Cast(hero, primaryTarget))
                    {
                        _castSubmitted = true;
                        _castSubmittedTick = _scenarioTick;
                        TransitionTo(C1ShowcaseStep.OrderSubmitted);
                    }
                    break;

                case C1ShowcaseStep.OrderSubmitted:
                    if (_c1DamageApplied)
                    {
                        TransitionTo(C1ShowcaseStep.DamageApplied);
                        break;
                    }
                    ThrowIfDamageTimedOut(primaryTarget);
                    break;

                case C1ShowcaseStep.DamageApplied:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C1ShowcaseStep.InvalidTargetSetup);
                    }
                    break;

                case C1ShowcaseStep.InvalidTargetSetup:
                    if (_scenarioTick - _stepStartTick >= 6)
                    {
                        TrySubmitC1Cast(hero, invalidTarget);
                        TransitionTo(C1ShowcaseStep.InvalidTargetBlocked);
                    }
                    break;

                case C1ShowcaseStep.InvalidTargetBlocked:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C1ShowcaseStep.OutOfRangeSetup);
                    }
                    break;

                case C1ShowcaseStep.OutOfRangeSetup:
                    if (_scenarioTick - _stepStartTick >= 6)
                    {
                        TrySubmitC1Cast(hero, farTarget);
                        TransitionTo(C1ShowcaseStep.OutOfRangeBlocked);
                    }
                    break;

                case C1ShowcaseStep.OutOfRangeBlocked:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C1ShowcaseStep.Complete);
                    }
                    break;
            }
        }

        private void AdvanceC2Scenario(Entity hero, Entity allyTarget, Entity hostileTarget, Entity deadAllyTarget)
        {
            switch (_c2Step)
            {
                case C2ShowcaseStep.Warmup:
                    if (_scenarioTick >= 5 && TrySubmitC2Cast(hero, allyTarget))
                    {
                        _castSubmitted = true;
                        _castSubmittedTick = _scenarioTick;
                        TransitionTo(C2ShowcaseStep.OrderSubmitted);
                    }
                    break;

                case C2ShowcaseStep.OrderSubmitted:
                    if (_c2HealApplied)
                    {
                        TransitionTo(C2ShowcaseStep.HealApplied);
                        break;
                    }
                    ThrowIfC2HealTimedOut(allyTarget);
                    break;

                case C2ShowcaseStep.HealApplied:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C2ShowcaseStep.HostileTargetSetup);
                    }
                    break;

                case C2ShowcaseStep.HostileTargetSetup:
                    if (_scenarioTick - _stepStartTick >= 6)
                    {
                        if (TrySubmitC2Cast(hero, hostileTarget))
                        {
                            throw new InvalidOperationException("C2 hostile-target guard unexpectedly allowed enqueue.");
                        }

                        TransitionTo(C2ShowcaseStep.HostileTargetBlocked);
                    }
                    break;

                case C2ShowcaseStep.HostileTargetBlocked:
                    if (ConsumeFailure("InvalidTarget", InteractionShowcaseIds.C2HostileTargetName))
                    {
                        TransitionTo(C2ShowcaseStep.DeadAllySetup);
                        break;
                    }
                    ThrowIfC2FailureTimedOut("InvalidTarget", InteractionShowcaseIds.C2HostileTargetName);
                    break;

                case C2ShowcaseStep.DeadAllySetup:
                    if (_scenarioTick - _stepStartTick >= 6)
                    {
                        if (TrySubmitC2Cast(hero, deadAllyTarget))
                        {
                            throw new InvalidOperationException("C2 dead-ally guard unexpectedly allowed enqueue.");
                        }

                        TransitionTo(C2ShowcaseStep.DeadAllyBlocked);
                    }
                    break;

                case C2ShowcaseStep.DeadAllyBlocked:
                    if (ConsumeFailure("InvalidTarget", InteractionShowcaseIds.C2DeadAllyTargetName))
                    {
                        TransitionTo(C2ShowcaseStep.Complete);
                        break;
                    }
                    ThrowIfC2FailureTimedOut("InvalidTarget", InteractionShowcaseIds.C2DeadAllyTargetName);
                    break;
            }
        }

        private void AdvanceC3Scenario(Entity hero, Entity hostileTarget, Entity friendlyTarget)
        {
            switch (_c3Step)
            {
                case C3ShowcaseStep.Warmup:
                    if (_scenarioTick >= 5 && TrySubmitC3Cast(hero, hostileTarget))
                    {
                        _castSubmitted = true;
                        _castSubmittedTick = _scenarioTick;
                        TransitionTo(C3ShowcaseStep.HostileOrderSubmitted);
                    }
                    break;

                case C3ShowcaseStep.HostileOrderSubmitted:
                    if (_c3HostilePolymorphApplied)
                    {
                        TransitionTo(C3ShowcaseStep.HostilePolymorphApplied);
                        break;
                    }
                    ThrowIfC3BranchTimedOut(
                        expectedBranch: "hostile polymorph",
                        step: _c3Step.ToString(),
                        expectedTargetName: InteractionShowcaseIds.C3HostileTargetName,
                        moveSpeed: ReadAttribute(hostileTarget, _moveSpeedAttributeId),
                        activeTagCount: ReadTagCount(hostileTarget, _c3PolymorphedTagId));
                    break;

                case C3ShowcaseStep.HostilePolymorphApplied:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C3ShowcaseStep.FriendlyOrderSetup);
                    }
                    break;

                case C3ShowcaseStep.FriendlyOrderSetup:
                    if (_scenarioTick - _stepStartTick >= 6 && TrySubmitC3Cast(hero, friendlyTarget))
                    {
                        _castSubmitted = true;
                        _castSubmittedTick = _scenarioTick;
                        TransitionTo(C3ShowcaseStep.FriendlyOrderSubmitted);
                    }
                    break;

                case C3ShowcaseStep.FriendlyOrderSubmitted:
                    if (_c3FriendlyHasteApplied)
                    {
                        TransitionTo(C3ShowcaseStep.FriendlyHasteApplied);
                        break;
                    }
                    ThrowIfC3BranchTimedOut(
                        expectedBranch: "friendly haste",
                        step: _c3Step.ToString(),
                        expectedTargetName: InteractionShowcaseIds.C3FriendlyTargetName,
                        moveSpeed: ReadAttribute(friendlyTarget, _moveSpeedAttributeId),
                        activeTagCount: ReadTagCount(friendlyTarget, _c3HastedTagId));
                    break;

                case C3ShowcaseStep.FriendlyHasteApplied:
                    if (_scenarioTick - _stepStartTick >= 24)
                    {
                        TransitionTo(C3ShowcaseStep.Complete);
                    }
                    break;
            }
        }

        private bool TryFindNamedEntity(string entityName, out Entity entity)
        {
            Entity found = Entity.Null;
            _world.Query(in _nameQuery, (Entity current, ref Name name) =>
            {
                if (found != Entity.Null)
                {
                    return;
                }

                if (string.Equals(name.Value, entityName, StringComparison.OrdinalIgnoreCase))
                {
                    found = current;
                }
            });

            entity = found;
            return entity != Entity.Null && _world.IsAlive(entity);
        }

        private bool TrySubmitSelfCast(Entity hero)
        {
            var orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue);
            if (orderQueue == null)
            {
                return false;
            }

            int castAbilityOrderTypeId = _engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            return orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                Actor = hero,
                Target = hero,
                Args = new OrderArgs
                {
                    I0 = InteractionShowcaseIds.B1SelfBuffSlot
                }
            });
        }

        private bool TrySubmitC1Cast(Entity hero, Entity target)
        {
            _c1LastAttemptTargetName = ReadName(target);
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c1LastAttemptTargetName;

            if (!IsValidC1Target(hero, target, out string reason))
            {
                PublishCustomFailure(reason);
                return false;
            }

            var orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue);
            if (orderQueue == null)
            {
                PublishCustomFailure("OrderQueueMissing");
                return false;
            }

            int castAbilityOrderTypeId = _engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                Actor = hero,
                Target = target,
                Args = new OrderArgs
                {
                    I0 = InteractionShowcaseIds.C1HostileUnitDamageSlot
                }
            });

            if (!enqueued)
            {
                PublishCustomFailure("OrderQueueRejected");
            }

            return enqueued;
        }

        private bool TrySubmitC2Cast(Entity hero, Entity target)
        {
            _c2LastAttemptTargetName = ReadName(target);
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c2LastAttemptTargetName;

            if (!IsValidC2Target(hero, target, out string reason))
            {
                PublishCustomFailure(reason, _c2LastAttemptTargetName);
                return false;
            }

            var orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue);
            if (orderQueue == null)
            {
                PublishCustomFailure("OrderQueueMissing", _c2LastAttemptTargetName);
                return false;
            }

            int castAbilityOrderTypeId = _engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                Actor = hero,
                Target = target,
                Args = new OrderArgs
                {
                    I0 = InteractionShowcaseIds.C2FriendlyUnitHealSlot
                }
            });

            if (!enqueued)
            {
                PublishCustomFailure("OrderQueueRejected", _c2LastAttemptTargetName);
            }

            return enqueued;
        }

        private bool TrySubmitC3Cast(Entity hero, Entity target)
        {
            _c3LastAttemptTargetName = ReadName(target);
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c3LastAttemptTargetName;

            if (!IsValidC3Target(target, out string reason))
            {
                PublishCustomFailure(reason, _c3LastAttemptTargetName);
                return false;
            }

            var orderQueue = _engine.GetService(CoreServiceKeys.OrderQueue);
            if (orderQueue == null)
            {
                PublishCustomFailure("OrderQueueMissing", _c3LastAttemptTargetName);
                return false;
            }

            int castAbilityOrderTypeId = _engine.MergedConfig.Constants.OrderTypeIds["castAbility"];
            bool enqueued = orderQueue.TryEnqueue(new Order
            {
                OrderTypeId = castAbilityOrderTypeId,
                Actor = hero,
                Target = target,
                Args = new OrderArgs
                {
                    I0 = InteractionShowcaseIds.C3AnyUnitConditionalSlot
                }
            });

            if (!enqueued)
            {
                PublishCustomFailure("OrderQueueRejected", _c3LastAttemptTargetName);
            }

            return enqueued;
        }

        private bool IsValidC1Target(Entity hero, Entity target, out string reason)
        {
            if (target == Entity.Null || !_world.IsAlive(target))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (!IsHostile(hero, target))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (ReadAttribute(target, _healthAttributeId) <= 0.001f || HasTag(target, _untargetableTagId))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (DistanceCm(hero, target) > C1CastRangeCm)
            {
                reason = "OutOfRange";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool IsValidC2Target(Entity hero, Entity target, out string reason)
        {
            if (target == Entity.Null || !_world.IsAlive(target))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (!IsFriendly(hero, target))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (ReadAttribute(target, _healthAttributeId) <= 0.001f || HasTag(target, _untargetableTagId))
            {
                reason = "InvalidTarget";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private bool IsValidC3Target(Entity target, out string reason)
        {
            if (target == Entity.Null || !_world.IsAlive(target))
            {
                reason = "InvalidTarget";
                return false;
            }

            if (ReadAttribute(target, _healthAttributeId) <= 0.001f || HasTag(target, _untargetableTagId))
            {
                reason = "InvalidTarget";
                return false;
            }

            reason = string.Empty;
            return true;
        }

        private float ReadAttribute(Entity entity, int attributeId)
        {
            if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return 0f;
            }

            return _world.Get<AttributeBuffer>(entity).GetCurrent(attributeId);
        }

        private float ReadBlackboardFloat(Entity entity, int keyId)
        {
            if (!_world.IsAlive(entity) || !_world.Has<BlackboardFloatBuffer>(entity))
            {
                return 0f;
            }

            ref var buffer = ref _world.Get<BlackboardFloatBuffer>(entity);
            return buffer.TryGet(keyId, out float value) ? value : 0f;
        }

        private bool HasTag(Entity entity, int tagId)
        {
            if (!_world.IsAlive(entity) || !_world.Has<GameplayTagContainer>(entity))
            {
                return false;
            }

            if (_engine.GetService(CoreServiceKeys.TagOps) is not TagOps tagOps)
            {
                return false;
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            return tagOps.HasTag(ref tags, tagId, TagSense.Effective);
        }

        private int ReadTagCount(Entity entity, int tagId)
        {
            if (!_world.IsAlive(entity) || !_world.Has<TagCountContainer>(entity))
            {
                return 0;
            }

            return _world.Get<TagCountContainer>(entity).GetCount(tagId);
        }

        private void ApplyTag(Entity entity, int tagId, bool add)
        {
            if (_engine.GetService(CoreServiceKeys.TagOps) is not TagOps tagOps)
            {
                return;
            }

            if (!_world.Has<GameplayTagContainer>(entity))
            {
                _world.Add(entity, new GameplayTagContainer());
            }
            if (!_world.Has<TagCountContainer>(entity))
            {
                _world.Add(entity, new TagCountContainer());
            }
            if (!_world.Has<DirtyFlags>(entity))
            {
                _world.Add(entity, new DirtyFlags());
            }

            ref var tags = ref _world.Get<GameplayTagContainer>(entity);
            ref var counts = ref _world.Get<TagCountContainer>(entity);
            ref var dirtyFlags = ref _world.Get<DirtyFlags>(entity);

            if (add)
            {
                tagOps.AddTag(ref tags, ref counts, tagId, ref dirtyFlags);
            }
            else
            {
                tagOps.RemoveTag(ref tags, ref counts, tagId, ref dirtyFlags);
            }
        }

        private void SetAttributeBase(Entity entity, int attributeId, float value)
        {
            if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return;
            }

            ref var attributes = ref _world.Get<AttributeBuffer>(entity);
            attributes.SetBase(attributeId, value);
        }

        private void SetAttributeCurrent(Entity entity, int attributeId, float value)
        {
            if (!_world.IsAlive(entity) || !_world.Has<AttributeBuffer>(entity))
            {
                return;
            }

            ref var attributes = ref _world.Get<AttributeBuffer>(entity);
            attributes.SetCurrent(attributeId, value);
        }

        private bool IsHostile(Entity source, Entity target)
        {
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                return false;
            }

            int sourceTeamId = _world.Has<Team>(source) ? _world.Get<Team>(source).Id : 0;
            int targetTeamId = _world.Has<Team>(target) ? _world.Get<Team>(target).Id : 0;
            return TeamManager.GetRelationship(sourceTeamId, targetTeamId) == TeamRelationship.Hostile;
        }

        private bool IsFriendly(Entity source, Entity target)
        {
            if (!_world.IsAlive(source) || !_world.IsAlive(target))
            {
                return false;
            }

            int sourceTeamId = _world.Has<Team>(source) ? _world.Get<Team>(source).Id : 0;
            int targetTeamId = _world.Has<Team>(target) ? _world.Get<Team>(target).Id : 0;
            return TeamManager.GetRelationship(sourceTeamId, targetTeamId) == TeamRelationship.Friendly;
        }

        private float DistanceCm(Entity a, Entity b)
        {
            if (!_world.IsAlive(a) || !_world.IsAlive(b) ||
                !_world.Has<WorldPositionCm>(a) || !_world.Has<WorldPositionCm>(b))
            {
                return float.MaxValue;
            }

            Vector2 aPos = _world.Get<WorldPositionCm>(a).Value.ToVector2();
            Vector2 bPos = _world.Get<WorldPositionCm>(b).Value.ToVector2();
            return Vector2.Distance(aPos, bPos);
        }

        private void PublishCustomFailure(string reason)
        {
            PublishCustomFailure(reason, _c1LastAttemptTargetName);
        }

        private void PublishCustomFailure(string reason, string lastAttemptTargetName)
        {
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailReason] = reason;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailTick] = _engine.GameSession.CurrentTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailAttribute] = string.Empty;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastCastFailDelta] = 0f;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = lastAttemptTargetName;
        }

        private bool ConsumeFailure(string expectedReason)
        {
            int failTick = ReadInt(InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            if (failTick <= _lastConsumedFailTick)
            {
                return false;
            }

            string reason = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty);
            if (!string.Equals(reason, expectedReason, StringComparison.Ordinal))
            {
                return false;
            }

            _lastConsumedFailTick = failTick;
            return true;
        }

        private bool ConsumeFailure(string expectedReason, string expectedTargetName)
        {
            int failTick = ReadInt(InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            if (failTick <= _lastConsumedFailTick)
            {
                return false;
            }

            string reason = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, string.Empty);
            string lastAttemptTargetName = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, string.Empty);
            if (!string.Equals(reason, expectedReason, StringComparison.Ordinal) ||
                !string.Equals(lastAttemptTargetName, expectedTargetName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            _lastConsumedFailTick = failTick;
            return true;
        }

        private void ThrowIfFailureTimedOut(string expectedReason)
        {
            int waitedTicks = _scenarioTick - _stepStartTick;
            if (waitedTicks < FailureEventTimeoutTicks)
            {
                return;
            }

            string currentReason = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            int failTick = ReadInt(InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            throw new InvalidOperationException(
                $"B1 showcase timed out after {waitedTicks} ticks waiting for cast failure '{expectedReason}' during step '{_b1Step}'. Last failure='{currentReason}' at tick {failTick}.");
        }

        private void ThrowIfDamageTimedOut(Entity target)
        {
            int waitedTicks = _scenarioTick - _stepStartTick;
            if (waitedTicks < DamageApplyTimeoutTicks)
            {
                return;
            }

            float health = ReadAttribute(target, _healthAttributeId);
            throw new InvalidOperationException(
                $"C1 showcase timed out after {waitedTicks} ticks waiting for mitigated damage. health={health:F1} damageAmount={_c1DamageAmount:F1} finalDamage={_c1FinalDamage:F1}.");
        }

        private void ThrowIfC2HealTimedOut(Entity allyTarget)
        {
            int waitedTicks = _scenarioTick - _stepStartTick;
            if (waitedTicks < DamageApplyTimeoutTicks)
            {
                return;
            }

            float health = ReadAttribute(allyTarget, _healthAttributeId);
            throw new InvalidOperationException(
                $"C2 showcase timed out after {waitedTicks} ticks waiting for heal application. health={health:F1} healAmount={_c2HealAmount:F1}.");
        }

        private void ThrowIfC2FailureTimedOut(string expectedReason, string expectedTargetName)
        {
            int waitedTicks = _scenarioTick - _stepStartTick;
            if (waitedTicks < FailureEventTimeoutTicks)
            {
                return;
            }

            string currentReason = ReadString(InteractionShowcaseRuntimeKeys.LastCastFailReason, "None");
            string currentTarget = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, "(none)");
            int failTick = ReadInt(InteractionShowcaseRuntimeKeys.LastCastFailTick, -1);
            throw new InvalidOperationException(
                $"C2 showcase timed out after {waitedTicks} ticks waiting for cast failure '{expectedReason}' on '{expectedTargetName}' during step '{_c2Step}'. Last failure='{currentReason}' target='{currentTarget}' at tick {failTick}.");
        }

        private void ThrowIfC3BranchTimedOut(
            string expectedBranch,
            string step,
            string expectedTargetName,
            float moveSpeed,
            int activeTagCount)
        {
            int waitedTicks = _scenarioTick - _stepStartTick;
            if (waitedTicks < DamageApplyTimeoutTicks)
            {
                return;
            }

            string currentTarget = ReadString(InteractionShowcaseRuntimeKeys.LastAttemptTargetName, "(none)");
            throw new InvalidOperationException(
                $"C3 showcase timed out after {waitedTicks} ticks waiting for {expectedBranch} on '{expectedTargetName}' during step '{step}'. lastTarget='{currentTarget}' moveSpeed={moveSpeed:F1} activeTagCount={activeTagCount}.");
        }

        private void PublishB1RuntimeState(bool hasHero, float attackDamage, float mana, bool hasEmpoweredTag, int empoweredCount)
        {
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ActiveScenarioId] = InteractionShowcaseIds.B1SelfBuffScenarioId;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.Stage] = ResolveB1Stage();
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ScriptTick] = _scenarioTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroPresent] = hasHero;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroAttackDamage] = attackDamage;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroMana] = mana;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroEmpoweredTag] = hasEmpoweredTag;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroEmpoweredTagCount] = empoweredCount;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmitted] = _castSubmitted;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmittedTick] = _castSubmittedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.BuffObserved] = _buffObserved;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.BuffExpired] = _buffExpired;
        }

        private void PublishC1RuntimeState(
            bool hasHero,
            float heroBaseDamage,
            float heroMana,
            float primaryHealth,
            float primaryArmor,
            float invalidHealth,
            float farHealth)
        {
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ActiveScenarioId] = InteractionShowcaseIds.C1HostileUnitDamageScenarioId;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.Stage] = ResolveC1Stage();
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ScriptTick] = _scenarioTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroPresent] = hasHero;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroBaseDamage] = heroBaseDamage;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroMana] = heroMana;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.PrimaryTargetHealth] = primaryHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.PrimaryTargetArmor] = primaryArmor;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.InvalidTargetHealth] = invalidHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.FarTargetHealth] = farHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.DamageAmount] = _c1DamageAmount;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.FinalDamage] = _c1FinalDamage;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.DamageApplied] = _c1DamageApplied;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.DamageAppliedTick] = _c1DamageAppliedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmitted] = _castSubmitted;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmittedTick] = _castSubmittedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c1LastAttemptTargetName;
        }

        private void PublishC2RuntimeState(
            bool hasHero,
            float heroMana,
            float allyHealth,
            float hostileHealth,
            float deadAllyHealth)
        {
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ActiveScenarioId] = InteractionShowcaseIds.C2FriendlyUnitHealScenarioId;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.Stage] = ResolveC2Stage();
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ScriptTick] = _scenarioTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroPresent] = hasHero;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroMana] = heroMana;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2AllyTargetHealth] = allyHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2HostileTargetHealth] = hostileHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2DeadAllyTargetHealth] = deadAllyHealth;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2HealAmount] = _c2HealAmount;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2HealApplied] = _c2HealApplied;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C2HealAppliedTick] = _c2HealAppliedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmitted] = _castSubmitted;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmittedTick] = _castSubmittedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c2LastAttemptTargetName;
        }

        private void PublishC3RuntimeState(
            bool hasHero,
            float heroMana,
            float hostileMoveSpeed,
            float friendlyMoveSpeed,
            bool hostilePolymorphActive,
            int hostilePolymorphCount,
            bool friendlyHasteActive,
            int friendlyHasteCount)
        {
            _c3HostileMoveSpeed = hostileMoveSpeed;
            _c3FriendlyMoveSpeed = friendlyMoveSpeed;

            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ActiveScenarioId] = InteractionShowcaseIds.C3AnyUnitConditionalScenarioId;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.Stage] = ResolveC3Stage();
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.ScriptTick] = _scenarioTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroPresent] = hasHero;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.HeroMana] = heroMana;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed] = hostileMoveSpeed;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed] = friendlyMoveSpeed;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive] = hostilePolymorphActive;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount] = hostilePolymorphCount;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied] = _c3HostilePolymorphApplied;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3HostilePolymorphAppliedTick] = _c3HostilePolymorphAppliedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive] = friendlyHasteActive;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount] = friendlyHasteCount;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied] = _c3FriendlyHasteApplied;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.C3FriendlyHasteAppliedTick] = _c3FriendlyHasteAppliedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmitted] = _castSubmitted;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.CastSubmittedTick] = _castSubmittedTick;
            _engine.GlobalContext[InteractionShowcaseRuntimeKeys.LastAttemptTargetName] = _c3LastAttemptTargetName;
        }

        private string ResolveB1Stage()
        {
            return _b1Step switch
            {
                B1ShowcaseStep.Warmup => "warmup",
                B1ShowcaseStep.OrderSubmitted => "order_submitted",
                B1ShowcaseStep.BuffActive => "buff_active",
                B1ShowcaseStep.BuffExpired => "buff_expired",
                B1ShowcaseStep.SilencedSetup => "silenced_setup",
                B1ShowcaseStep.SilencedAttemptSubmitted => "silenced_attempt_submitted",
                B1ShowcaseStep.SilencedBlocked => "silenced_blocked",
                B1ShowcaseStep.InsufficientManaSetup => "insufficient_mana_setup",
                B1ShowcaseStep.InsufficientManaAttemptSubmitted => "insufficient_mana_attempt_submitted",
                B1ShowcaseStep.InsufficientManaBlocked => "insufficient_mana_blocked",
                B1ShowcaseStep.Complete => "complete",
                _ => "warmup",
            };
        }

        private string ResolveC1Stage()
        {
            return _c1Step switch
            {
                C1ShowcaseStep.Warmup => "warmup",
                C1ShowcaseStep.OrderSubmitted => "order_submitted",
                C1ShowcaseStep.DamageApplied => "damage_applied",
                C1ShowcaseStep.InvalidTargetSetup => "invalid_target_setup",
                C1ShowcaseStep.InvalidTargetBlocked => "invalid_target_blocked",
                C1ShowcaseStep.OutOfRangeSetup => "out_of_range_setup",
                C1ShowcaseStep.OutOfRangeBlocked => "out_of_range_blocked",
                C1ShowcaseStep.Complete => "complete",
                _ => "warmup",
            };
        }

        private string ResolveC2Stage()
        {
            return _c2Step switch
            {
                C2ShowcaseStep.Warmup => "warmup",
                C2ShowcaseStep.OrderSubmitted => "order_submitted",
                C2ShowcaseStep.HealApplied => "heal_applied",
                C2ShowcaseStep.HostileTargetSetup => "hostile_target_setup",
                C2ShowcaseStep.HostileTargetBlocked => "hostile_target_blocked",
                C2ShowcaseStep.DeadAllySetup => "dead_ally_setup",
                C2ShowcaseStep.DeadAllyBlocked => "dead_ally_blocked",
                C2ShowcaseStep.Complete => "complete",
                _ => "warmup",
            };
        }

        private string ResolveC3Stage()
        {
            return _c3Step switch
            {
                C3ShowcaseStep.Warmup => "warmup",
                C3ShowcaseStep.HostileOrderSubmitted => "hostile_order_submitted",
                C3ShowcaseStep.HostilePolymorphApplied => "hostile_polymorph_applied",
                C3ShowcaseStep.FriendlyOrderSetup => "friendly_order_setup",
                C3ShowcaseStep.FriendlyOrderSubmitted => "friendly_order_submitted",
                C3ShowcaseStep.FriendlyHasteApplied => "friendly_haste_applied",
                C3ShowcaseStep.Complete => "complete",
                _ => "warmup",
            };
        }

        private void TransitionTo(B1ShowcaseStep nextStep)
        {
            _b1Step = nextStep;
            _stepStartTick = _scenarioTick;
        }

        private void TransitionTo(C1ShowcaseStep nextStep)
        {
            _c1Step = nextStep;
            _stepStartTick = _scenarioTick;
        }

        private void TransitionTo(C2ShowcaseStep nextStep)
        {
            _c2Step = nextStep;
            _stepStartTick = _scenarioTick;
        }

        private void TransitionTo(C3ShowcaseStep nextStep)
        {
            _c3Step = nextStep;
            _stepStartTick = _scenarioTick;
        }

        private void ResetRuntimeState(bool clearGlobals)
        {
            _scenarioTick = 0;
            _stepStartTick = 0;
            _castSubmittedTick = -1;
            _lastConsumedFailTick = -1;
            _b1Step = B1ShowcaseStep.Warmup;
            _c1Step = C1ShowcaseStep.Warmup;
            _c2Step = C2ShowcaseStep.Warmup;
            _c3Step = C3ShowcaseStep.Warmup;
            _castSubmitted = false;
            _buffObserved = false;
            _buffExpired = false;
            _c1DamageApplied = false;
            _c2Initialized = false;
            _c2HealApplied = false;
            _c3Initialized = false;
            _c3HostilePolymorphApplied = false;
            _c3FriendlyHasteApplied = false;
            _c1DamageAppliedTick = -1;
            _c2HealAppliedTick = -1;
            _c3HostilePolymorphAppliedTick = -1;
            _c3FriendlyHasteAppliedTick = -1;
            _c1DamageAmount = 0f;
            _c1FinalDamage = 0f;
            _c2HealAmount = 0f;
            _c3HostileMoveSpeed = 0f;
            _c3FriendlyMoveSpeed = 0f;
            _c1LastAttemptTargetName = string.Empty;
            _c2LastAttemptTargetName = string.Empty;
            _c3LastAttemptTargetName = string.Empty;

            if (!clearGlobals)
            {
                return;
            }

            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.ActiveScenarioId);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.Stage);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.ScriptTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroPresent);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroAttackDamage);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroBaseDamage);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroMana);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroEmpoweredTag);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.HeroEmpoweredTagCount);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.CastSubmitted);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.CastSubmittedTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.BuffObserved);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.BuffExpired);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.PrimaryTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.PrimaryTargetArmor);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.InvalidTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.FarTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.DamageAmount);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.FinalDamage);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.DamageApplied);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.DamageAppliedTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.LastAttemptTargetName);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.LastCastFailReason);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.LastCastFailTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.LastCastFailAttribute);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.LastCastFailDelta);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2AllyTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2HostileTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2DeadAllyTargetHealth);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2HealAmount);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2HealApplied);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C2HealAppliedTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3HostileTargetMoveSpeed);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3FriendlyTargetMoveSpeed);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3HostilePolymorphActive);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3HostilePolymorphCount);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3HostilePolymorphApplied);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3HostilePolymorphAppliedTick);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3FriendlyHasteActive);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3FriendlyHasteCount);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3FriendlyHasteApplied);
            _engine.GlobalContext.Remove(InteractionShowcaseRuntimeKeys.C3FriendlyHasteAppliedTick);
        }

        private string ReadName(Entity entity)
        {
            if (entity == Entity.Null || !_world.IsAlive(entity) || !_world.Has<Name>(entity))
            {
                return string.Empty;
            }

            return _world.Get<Name>(entity).Value ?? string.Empty;
        }

        private string ReadString(string key, string fallback)
        {
            return _engine.GlobalContext.TryGetValue(key, out object? value) && value is string text
                ? text
                : fallback;
        }

        private int ReadInt(string key, int fallback)
        {
            return _engine.GlobalContext.TryGetValue(key, out object? value) && value is int number
                ? number
                : fallback;
        }

        private enum B1ShowcaseStep
        {
            Warmup = 0,
            OrderSubmitted = 1,
            BuffActive = 2,
            BuffExpired = 3,
            SilencedSetup = 4,
            SilencedAttemptSubmitted = 5,
            SilencedBlocked = 6,
            InsufficientManaSetup = 7,
            InsufficientManaAttemptSubmitted = 8,
            InsufficientManaBlocked = 9,
            Complete = 10,
        }

        private enum C1ShowcaseStep
        {
            Warmup = 0,
            OrderSubmitted = 1,
            DamageApplied = 2,
            InvalidTargetSetup = 3,
            InvalidTargetBlocked = 4,
            OutOfRangeSetup = 5,
            OutOfRangeBlocked = 6,
            Complete = 7,
        }

        private enum C2ShowcaseStep
        {
            Warmup = 0,
            OrderSubmitted = 1,
            HealApplied = 2,
            HostileTargetSetup = 3,
            HostileTargetBlocked = 4,
            DeadAllySetup = 5,
            DeadAllyBlocked = 6,
            Complete = 7,
        }

        private enum C3ShowcaseStep
        {
            Warmup = 0,
            HostileOrderSubmitted = 1,
            HostilePolymorphApplied = 2,
            FriendlyOrderSetup = 3,
            FriendlyOrderSubmitted = 4,
            FriendlyHasteApplied = 5,
            Complete = 6,
        }
    }
}
