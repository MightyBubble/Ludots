using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Arch.Core;
using Ludots.Core.Components;
using Ludots.Core.MassNavigation.Runtime;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Physics2D;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Movement.Physics2DBridge
{
    /// <summary>
    /// massnav→kinematic 桥的位姿喂送半边。
    ///
    /// 每个引擎固定步、在 Physics2D 步进之前，把 physicsPresence=Kinematic 且已绑定
    /// massnav agent 的实体的已提交 <see cref="WorldPositionCm"/>（Nav 写权由 entity-sync
    /// 提交、Displacement 写权由位移窗口提交——物理视角单位永远在场，仅驱动源不同）
    /// 通过 <see cref="KinematicTargetPoseBuffer2D.SetKinematicTargetPose"/> 喂给 kinematic body。
    ///
    /// 合同（全部 fail-fast，无静默降级）：
    /// - 模板配对：participation=Kinematic 的 massnav 实体必须同时具备
    ///   Mass2D.Kinematic + Position2D + Collider2D + PoseAuthority，缺任何一半抛异常；
    ///   反之，绑定了 massnav agent 的 kinematic 物理体必须声明 MovementParticipation。
    /// - 半径 SSOT：kinematic 圆形 collider 半径必须等于 agent profile 的 bodyRadiusCm
    ///   （首次绑定校验，agent index 变化时重校验），漂移即抛异常。
    /// - 容量：参与单位数超过 kinematicBodyCapacity 抛异常。
    /// - 节拍：位姿在上一固定步各写权系统提交之后、本固定步物理消费之前喂送；
    ///   若上一步的位姿未被物理消费（physicsHz &lt; 固定步 Hz 或物理被禁用）抛异常。
    /// - 热路径零分配：chunk 遍历 + 预分配校验缓存，无每帧 LINQ/装箱/字典扩容。
    /// </summary>
    public sealed class MassNavKinematicPoseFeedSystem2D : BaseSystem<World, float>
    {
        private static readonly QueryDescription _participantsQuery = new QueryDescription()
            .WithAll<MovementParticipation, MassNavigationAgentIndex, WorldPositionCm>();

        private static readonly QueryDescription _unparticipatedBodiesQuery = new QueryDescription()
            .WithAll<MassNavigationAgentIndex, Mass2D>()
            .WithNone<MovementParticipation>();

        private readonly Func<MassNavigationSimulationRuntime?> _runtimeProvider;
        private readonly KinematicTargetPoseBuffer2D _poseBuffer;
        private readonly ShapeDataStorage2D _shapeStorage;
        private readonly Dictionary<Entity, int> _validatedAgentIndexByEntity;
        private readonly List<Entity> _staleValidationEntries;

        public MassNavKinematicPoseFeedSystem2D(
            World world,
            Func<MassNavigationSimulationRuntime?> runtimeProvider,
            KinematicTargetPoseBuffer2D poseBuffer,
            ShapeDataStorage2D shapeStorage) : base(world)
        {
            _runtimeProvider = runtimeProvider ?? throw new ArgumentNullException(nameof(runtimeProvider));
            _poseBuffer = poseBuffer ?? throw new ArgumentNullException(nameof(poseBuffer));
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
            _validatedAgentIndexByEntity = new Dictionary<Entity, int>(poseBuffer.Capacity);
            _staleValidationEntries = new List<Entity>(poseBuffer.Capacity);
        }

        /// <summary>上一固定步实际喂送的参与单位数（可观测状态，供测试与 HUD 查询）。</summary>
        public int LastFedParticipantCount { get; private set; }

        public override void Update(in float deltaTime)
        {
            MassNavigationSimulationRuntime? runtime = _runtimeProvider();
            if (runtime == null)
            {
                LastFedParticipantCount = 0;
                return;
            }

            RejectKinematicBodiesWithoutParticipation();

            int fedCount = 0;
            foreach (ref var chunk in World.Query(in _participantsQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                chunk.GetSpan<MovementParticipation, MassNavigationAgentIndex, WorldPositionCm>(
                    out var participations, out var agentIndices, out var worldPositions);
                bool hasMass = chunk.Has<Mass2D>();
                Span<Mass2D> masses = hasMass ? chunk.GetSpan<Mass2D>() : default;
                bool hasPosition = chunk.Has<Position2D>();
                bool hasPoseAuthority = chunk.Has<PoseAuthority>();
                bool hasRotation = chunk.Has<Rotation2D>();
                Span<Rotation2D> rotations = hasRotation ? chunk.GetSpan<Rotation2D>() : default;
                ref Entity entityFirst = ref chunk.Entity(0);

                foreach (int index in chunk)
                {
                    ref MovementParticipation participation = ref participations[index];
                    Entity entity = Unsafe.Add(ref entityFirst, index);

                    if (participation.PhysicsPresence != PhysicsPresenceKind.Kinematic)
                    {
                        if (hasMass && masses[index].IsKinematic)
                        {
                            throw new InvalidOperationException(
                                $"massnav→kinematic bridge: entity {entity.Id} authored movementParticipation.physicsPresence={participation.PhysicsPresence} " +
                                "but carries a kinematic Mass2D body. Template authoring gap: declare physicsPresence=Kinematic or remove the kinematic rigid body.");
                        }

                        continue;
                    }

                    if (!hasMass || !masses[index].IsKinematic || !hasPosition)
                    {
                        throw new InvalidOperationException(
                            $"massnav→kinematic bridge: entity {entity.Id} authored movementParticipation.physicsPresence=Kinematic " +
                            "but is missing its kinematic physics half (requires Mass2D.Kinematic + Position2D via rigidBody2D template authoring). " +
                            "Template authoring gap: participation and physics body must be declared together.");
                    }

                    if (!hasPoseAuthority)
                    {
                        throw new InvalidOperationException(
                            $"massnav→kinematic bridge: entity {entity.Id} has MovementParticipation but no PoseAuthority runtime state; " +
                            "MovementParticipation authoring must always attach the derived PoseAuthority component.");
                    }

                    ValidateRadiusSsot(entity, agentIndices[index].Value, runtime);

                    if (_poseBuffer.TryGetPending(entity, out _))
                    {
                        throw new InvalidOperationException(
                            $"massnav→kinematic bridge: entity {entity.Id} still has an unconsumed kinematic target pose from the previous fixed step. " +
                            "Physics2D did not step between two feeds; the bridge cadence contract requires physicsHz >= the engine fixed-step Hz and an enabled Physics2D simulation.");
                    }

                    if (_poseBuffer.PendingCount >= _poseBuffer.Capacity)
                    {
                        throw new InvalidOperationException(
                            $"massnav→kinematic bridge: kinematicBodyCapacity={_poseBuffer.Capacity} cannot admit massnav participant entity {entity.Id} " +
                            $"(pending poses this step: {_poseBuffer.PendingCount}). Raise 'Physics2D/kinematic.json' kinematicBodyCapacity to at least the participating unit count.");
                    }

                    Fix64 rotationRad = hasRotation ? rotations[index].Value : Fix64.Zero;
                    _poseBuffer.SetKinematicTargetPose(entity, worldPositions[index].Value, rotationRad);
                    fedCount++;
                }
            }

            LastFedParticipantCount = fedCount;
        }

        private void RejectKinematicBodiesWithoutParticipation()
        {
            foreach (ref var chunk in World.Query(in _unparticipatedBodiesQuery))
            {
                if (chunk.Count <= 0)
                {
                    continue;
                }

                Span<Mass2D> masses = chunk.GetSpan<Mass2D>();
                ref Entity entityFirst = ref chunk.Entity(0);
                foreach (int index in chunk)
                {
                    if (masses[index].IsKinematic)
                    {
                        Entity entity = Unsafe.Add(ref entityFirst, index);
                        throw new InvalidOperationException(
                            $"massnav→kinematic bridge: massnav agent entity {entity.Id} carries a kinematic Mass2D body but no MovementParticipation. " +
                            "Template authoring gap: declare movementParticipation with physicsPresence=Kinematic alongside the kinematic rigid body.");
                    }
                }
            }
        }

        private void ValidateRadiusSsot(Entity entity, int agentIndex, MassNavigationSimulationRuntime runtime)
        {
            if (_validatedAgentIndexByEntity.TryGetValue(entity, out int validatedAgentIndex) &&
                validatedAgentIndex == agentIndex)
            {
                return;
            }

            if (!World.TryGet(entity, out Collider2D collider))
            {
                throw new InvalidOperationException(
                    $"massnav→kinematic bridge: kinematic massnav participant entity {entity.Id} has no Collider2D; " +
                    "the kinematic physics half requires a circle collider matching the agent profile bodyRadiusCm.");
            }

            if (collider.Type != ColliderType2D.Circle)
            {
                throw new InvalidOperationException(
                    $"massnav→kinematic bridge: kinematic massnav participant entity {entity.Id} declares a {collider.Type} collider; " +
                    "the agent body contract is a circle whose radius is single-sourced from the agent profile bodyRadiusCm.");
            }

            if (!_shapeStorage.TryGetCircle(collider.ShapeDataIndex, out CircleShapeData circle))
            {
                throw new InvalidOperationException(
                    $"massnav→kinematic bridge: entity {entity.Id} circle collider shape index {collider.ShapeDataIndex} does not resolve in ShapeDataStorage2D.");
            }

            float profileRadiusCm = runtime.GetAgentBodyRadiusCm(agentIndex);
            Fix64 expectedRadius = Fix64.FromFloat(profileRadiusCm);
            if (circle.Radius != expectedRadius)
            {
                throw new InvalidOperationException(
                    $"massnav→kinematic bridge: radius drift on entity {entity.Id} (agent {agentIndex}): " +
                    $"kinematic collider radius {circle.Radius.ToFloat()}cm != agent profile bodyRadiusCm {profileRadiusCm}cm. " +
                    "The agent profile is the single source of truth for body radius; fix the physics template to match the profile.");
            }

            if (circle.LocalCenter.X != Fix64.Zero || circle.LocalCenter.Y != Fix64.Zero)
            {
                throw new InvalidOperationException(
                    $"massnav→kinematic bridge: entity {entity.Id} kinematic circle collider has non-zero localCenterCm " +
                    $"({circle.LocalCenter.X.ToFloat()}, {circle.LocalCenter.Y.ToFloat()}); the agent body disc must be centered on the entity pose.");
            }

            if (_validatedAgentIndexByEntity.Count >= _poseBuffer.Capacity &&
                !_validatedAgentIndexByEntity.ContainsKey(entity))
            {
                PruneDeadValidationEntries();
                if (_validatedAgentIndexByEntity.Count >= _poseBuffer.Capacity)
                {
                    throw new InvalidOperationException(
                        $"massnav→kinematic bridge: live massnav kinematic participants exceed kinematicBodyCapacity={_poseBuffer.Capacity}; " +
                        "raise 'Physics2D/kinematic.json' kinematicBodyCapacity to at least the participating unit count.");
                }
            }

            _validatedAgentIndexByEntity[entity] = agentIndex;
        }

        private void PruneDeadValidationEntries()
        {
            _staleValidationEntries.Clear();
            foreach (KeyValuePair<Entity, int> entry in _validatedAgentIndexByEntity)
            {
                if (!World.IsAlive(entry.Key))
                {
                    _staleValidationEntries.Add(entry.Key);
                }
            }

            for (int i = 0; i < _staleValidationEntries.Count; i++)
            {
                _validatedAgentIndexByEntity.Remove(_staleValidationEntries[i]);
            }
        }
    }
}
