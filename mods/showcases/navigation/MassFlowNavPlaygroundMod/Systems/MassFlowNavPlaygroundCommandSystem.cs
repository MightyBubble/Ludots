using System;
using System.Numerics;
using Arch.Buffer;
using Arch.Core;
using Arch.System;
using Ludots.Core.Components;
using Ludots.Core.Diagnostics;
using Ludots.Core.Engine;
using Ludots.Core.Input.Interaction;
using Ludots.Core.Input.Runtime;
using Ludots.Core.Input.Selection;
using Ludots.Core.Mathematics;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Navigation2D.Components;
using Ludots.Core.Physics2D.Components;
using Ludots.Core.Scripting;
using MassFlowNavPlaygroundMod.Components;
using MassFlowNavPlaygroundMod.Runtime;

namespace MassFlowNavPlaygroundMod.Systems
{
    internal sealed class MassFlowNavPlaygroundCommandSystem : ISystem<float>
    {
        private readonly GameEngine _engine;
        private readonly World _world;
        private readonly CommandBuffer _commandBuffer = new();
        private Entity[] _selectedScratch = Array.Empty<Entity>();
        private int[] _rotationGroupScratch = Array.Empty<int>();
        private Vector2[] _formationOffsetScratch = Array.Empty<Vector2>();

        public MassFlowNavPlaygroundCommandSystem(GameEngine engine)
        {
            _engine = engine;
            _world = engine.World;
        }

        public void Initialize() { }
        public void BeforeUpdate(in float t) { }
        public void AfterUpdate(in float t) { }
        public void Dispose()
        {
            _commandBuffer.Dispose();
        }

        public void Update(in float t)
        {
            if (_engine.GetService(MassFlowNavPlaygroundServiceKeys.State) is not MassFlowNavPlaygroundState state ||
                !state.IsActive ||
                !string.Equals(_engine.CurrentMapSession?.MapId.Value, MassFlowNavPlaygroundIds.MapId, StringComparison.OrdinalIgnoreCase) ||
                _engine.GetService(CoreServiceKeys.AuthoritativeInput) is not IInputActionReader input)
            {
                return;
            }

            InteractionActionBindings bindings = InteractionActionBindingsResolver.Require(_engine.GlobalContext, nameof(MassFlowNavPlaygroundCommandSystem));
            bool uiCaptured = _engine.GetService(CoreServiceKeys.UiCaptured);
            bool liveCommandPressed = input.PressedThisFrame(bindings.CommandActionId);
            bool liveCommandDown = input.IsDown(bindings.CommandActionId);
            if (!uiCaptured)
            {
                HandleFormationRotation(state, input, t);
            }

            bool hasPointerSnapshot = PointerInteractionSnapshotReader.TryRead(_engine.GlobalContext, out PointerInteractionSnapshot pointer);
            bool snapshotCommandPressed = hasPointerSnapshot && pointer.Command.PressedThisFrame;
            bool snapshotCommandDown = hasPointerSnapshot && pointer.Command.IsDown;
            bool hasGroundPoint = hasPointerSnapshot && pointer.HasGroundPoint;
            WorldCmInt2 worldCm = hasPointerSnapshot ? pointer.GroundWorldCm : default;
            if (!hasGroundPoint)
            {
                hasGroundPoint = AuthoritativeGroundPointerHelper.TryRead(input, out worldCm);
            }

            bool commandPressedThisFrame = snapshotCommandPressed || liveCommandPressed;
            state.LastCommandInputDebug = $"input ui={Bool01(uiCaptured)} live={Bool01(liveCommandPressed)}/{Bool01(liveCommandDown)} snap={Bool01(snapshotCommandPressed)}/{Bool01(snapshotCommandDown)} ground={Bool01(hasGroundPoint)} sel={SelectionContextRuntime.GetCurrentCount(_world, _engine.GlobalContext)}";

            if (uiCaptured || !commandPressedThisFrame || !hasGroundPoint)
            {
                if (uiCaptured || liveCommandPressed || snapshotCommandPressed || liveCommandDown || snapshotCommandDown)
                {
                    Log.Info(in LogChannels.Input, $"[MassFlowNav] cmd:block {state.LastCommandInputDebug}");
                }

                return;
            }

            Vector2 targetCm = new(worldCm.X, worldCm.Y);
            int selectedCount = CopySelectedEntities();
            if (selectedCount <= 0)
            {
                MoveSharedFlowGoal(state, targetCm);
                WriteCommandDebug(state, $"cmd:flow sel=0 target=({worldCm.X},{worldCm.Y})", log: true);
                return;
            }

            SortByEntityId(_selectedScratch, selectedCount);
            state.ArmMotionProbe(_selectedScratch[0], frames: 180);
            float preservedRotation = state.RemoveEntitiesFromGroups(_world, _selectedScratch.AsSpan(0, selectedCount));
            int manualTagsAdded = 0;
            if (state.FormationMode == MassFlowFormationMode.None || selectedCount == 1)
            {
                for (int i = 0; i < selectedCount; i++)
                {
                    if (IssueManualPointGoal(_selectedScratch[i], targetCm))
                    {
                        manualTagsAdded++;
                    }
                }

                state.AddManualCount(manualTagsAdded);
                PlaybackCommandsIfNeeded();
                int goalsWritten = CountPointGoals(selectedCount);
                int detachedCount = CountDetachedFromFlow(selectedCount);
                WriteCommandDebug(
                    state,
                    $"cmd:manual sel={selectedCount} goals={goalsWritten} detached={detachedCount} target=({worldCm.X},{worldCm.Y})",
                    log: true);
                return;
            }

            float initialRotation = ResolveInitialRotation(_selectedScratch, selectedCount, targetCm, preservedRotation);
            EnsureFormationOffsetCapacity(selectedCount);
            BuildFormationOffsets(_formationOffsetScratch.AsSpan(0, selectedCount), state.FormationMode, state.FormationSpacingCm);
            MassFlowFormationGroup group = state.CreateGroup(
                _selectedScratch.AsSpan(0, selectedCount),
                _formationOffsetScratch.AsSpan(0, selectedCount),
                targetCm,
                initialRotation,
                state.FormationMode);

            for (int i = 0; i < selectedCount; i++)
            {
                Entity member = _selectedScratch[i];
                UpsertFormationMember(member, group.GroupId, i);
                DetachFromSharedFlow(member);
                SetSmartStopSuppressed(member, suppressed: true);
                if (UpsertManualTag(member))
                {
                    manualTagsAdded++;
                }
                UpsertPointGoal(member, group.DestinationCm + group.OffsetsCm[i], radiusCm: 60);
            }

            state.AddManualCount(manualTagsAdded);
            PlaybackCommandsIfNeeded();
            int formationGoals = CountPointGoals(selectedCount);
            WriteCommandDebug(
                state,
                $"cmd:formation sel={selectedCount} group={group.GroupId} goals={formationGoals} target=({worldCm.X},{worldCm.Y})",
                log: true);
        }

        private void HandleFormationRotation(MassFlowNavPlaygroundState state, IInputActionReader input, float dt)
        {
            float rotationDelta = 0f;
            if (input.IsDown(MassFlowNavPlaygroundIds.RotateFormationLeftActionId))
            {
                rotationDelta -= state.RotationSpeedRadPerSec * dt;
            }

            if (input.IsDown(MassFlowNavPlaygroundIds.RotateFormationRightActionId))
            {
                rotationDelta += state.RotationSpeedRadPerSec * dt;
            }

            if (MathF.Abs(rotationDelta) <= 0.0001f)
            {
                return;
            }

            int touchedGroupCount = 0;
            int selectedCount = CopySelectedEntities();
            for (int i = 0; i < selectedCount; i++)
            {
                Entity entity = _selectedScratch[i];
                if (!_world.IsAlive(entity) || !_world.TryGet(entity, out MassFlowNavFormationMember member))
                {
                    continue;
                }

                if (ContainsRotationGroup(touchedGroupCount, member.GroupId) || !state.TryGetGroup(member.GroupId, out MassFlowFormationGroup group))
                {
                    continue;
                }

                EnsureRotationGroupCapacity(touchedGroupCount + 1);
                _rotationGroupScratch[touchedGroupCount++] = member.GroupId;
                group.RotationRad += rotationDelta;
                group.RecomputeOffsets();
            }
        }

        private void MoveSharedFlowGoal(MassFlowNavPlaygroundState state, Vector2 targetCm)
        {
            if (!state.TryGetFlowGoalEntity(state.SelectedTeamFlowId, out Entity goalEntity) ||
                !_world.IsAlive(goalEntity) ||
                !_world.TryGet(goalEntity, out NavFlowGoal2D goal))
            {
                return;
            }

            goal.GoalCm = Fix64Vec2.FromFloat(targetCm.X, targetCm.Y);
            _commandBuffer.Set(goalEntity, goal);
            PlaybackCommandsIfNeeded();
            state.MarkPanelDirty();
        }

        private bool IssueManualPointGoal(Entity entity, Vector2 targetCm)
        {
            if (!_world.IsAlive(entity))
            {
                return false;
            }

            if (_world.Has<MassFlowNavFormationMember>(entity))
            {
                _commandBuffer.Remove<MassFlowNavFormationMember>(entity);
            }

            DetachFromSharedFlow(entity);
            SetSmartStopSuppressed(entity, suppressed: true);
            bool addedManualTag = UpsertManualTag(entity);
            UpsertPointGoal(entity, targetCm, radiusCm: 90);
            return addedManualTag;
        }

        private void UpsertPointGoal(Entity entity, Vector2 targetCm, int radiusCm)
        {
            var goal = new NavGoal2D
            {
                Kind = NavGoalKind2D.Point,
                TargetCm = Fix64Vec2.FromFloat(targetCm.X, targetCm.Y),
                RadiusCm = Fix64.FromInt(radiusCm)
            };

            if (_world.Has<NavGoal2D>(entity))
            {
                _commandBuffer.Set(entity, goal);
            }
            else
            {
                _commandBuffer.Add(entity, goal);
            }
        }

        private bool UpsertManualTag(Entity entity)
        {
            if (_world.Has<MassFlowNavManualGoalTag>(entity))
            {
                return false;
            }

            _commandBuffer.Add(entity, default(MassFlowNavManualGoalTag));
            return true;
        }

        private void DetachFromSharedFlow(Entity entity)
        {
            if (_world.Has<NavFlowBinding2D>(entity))
            {
                _commandBuffer.Remove<NavFlowBinding2D>(entity);
            }
        }

        private void UpsertFormationMember(Entity entity, int groupId, int slotIndex)
        {
            var member = new MassFlowNavFormationMember
            {
                GroupId = groupId,
                SlotIndex = slotIndex
            };

            if (_world.Has<MassFlowNavFormationMember>(entity))
            {
                _commandBuffer.Set(entity, member);
            }
            else
            {
                _commandBuffer.Add(entity, member);
            }
        }

        private void SetSmartStopSuppressed(Entity entity, bool suppressed)
        {
            if (!_world.IsAlive(entity) || !_world.Has<NavAgent2D>(entity))
            {
                return;
            }

            ref var navAgent = ref _world.Get<NavAgent2D>(entity);
            navAgent.SmartStopSuppressed = suppressed ? (byte)1 : (byte)0;
        }

        private void PlaybackCommandsIfNeeded()
        {
            if (_commandBuffer.Size > 0)
            {
                _commandBuffer.Playback(_world, dispose: true);
            }
        }

        private float ResolveInitialRotation(Entity[] selected, int count, Vector2 destinationCm, float preservedRotation)
        {
            if (MathF.Abs(preservedRotation) > 0.001f)
            {
                return preservedRotation;
            }

            Vector2 centroid = Vector2.Zero;
            int alive = 0;
            for (int i = 0; i < count; i++)
            {
                if (!TryGetPositionCm(selected[i], out Vector2 position))
                {
                    continue;
                }

                centroid += position;
                alive++;
            }

            if (alive <= 0)
            {
                return 0f;
            }

            centroid /= alive;
            Vector2 forward = destinationCm - centroid;
            return forward.LengthSquared() <= 1f ? 0f : MathF.Atan2(forward.Y, forward.X);
        }

        private bool TryGetPositionCm(Entity entity, out Vector2 positionCm)
        {
            positionCm = Vector2.Zero;
            if (!_world.IsAlive(entity))
            {
                return false;
            }

            if (_world.TryGet(entity, out WorldPositionCm worldPosition))
            {
                positionCm = worldPosition.Value.ToVector2();
                return true;
            }

            if (_world.TryGet(entity, out Position2D position))
            {
                positionCm = position.Value.ToVector2();
                return true;
            }

            return false;
        }

        private static void BuildFormationOffsets(Span<Vector2> offsets, MassFlowFormationMode mode, float spacingCm)
        {
            offsets.Clear();
            int count = offsets.Length;
            switch (mode)
            {
                case MassFlowFormationMode.Line:
                    for (int i = 0; i < count; i++)
                    {
                        offsets[i] = new Vector2(i * spacingCm, 0f);
                    }
                    break;
                case MassFlowFormationMode.Square:
                {
                    int columns = (int)MathF.Ceiling(MathF.Sqrt(count));
                    for (int i = 0; i < count; i++)
                    {
                        int row = i / columns;
                        int column = i % columns;
                        offsets[i] = new Vector2(column * spacingCm, row * spacingCm);
                    }
                    break;
                }
                case MassFlowFormationMode.Circle:
                {
                    float radius = MathF.Max(spacingCm, (count * spacingCm) / (2f * MathF.PI));
                    for (int i = 0; i < count; i++)
                    {
                        float angle = (MathF.PI * 2f * i) / count;
                        offsets[i] = new Vector2(MathF.Cos(angle) * radius, MathF.Sin(angle) * radius);
                    }
                    break;
                }
                case MassFlowFormationMode.Wedge:
                {
                    int index = 0;
                    int row = 0;
                    while (index < count)
                    {
                        int rowCount = Math.Min(row + 1, count - index);
                        for (int slot = 0; slot < rowCount; slot++)
                        {
                            offsets[index++] = new Vector2(slot * spacingCm, row * spacingCm);
                        }

                        row++;
                    }
                    break;
                }
                default:
                    break;
            }

            CenterOffsets(offsets);
        }

        private static void CenterOffsets(Span<Vector2> offsets)
        {
            if (offsets.Length <= 0)
            {
                return;
            }

            Vector2 centroid = Vector2.Zero;
            for (int i = 0; i < offsets.Length; i++)
            {
                centroid += offsets[i];
            }

            centroid /= offsets.Length;
            for (int i = 0; i < offsets.Length; i++)
            {
                offsets[i] -= centroid;
            }
        }

        private int CopySelectedEntities()
        {
            int requested = SelectionContextRuntime.GetCurrentCount(_world, _engine.GlobalContext);
            if (requested <= 0)
            {
                return 0;
            }

            EnsureSelectedCapacity(requested);
            int copied = SelectionContextRuntime.CopyCurrentSelection(_world, _engine.GlobalContext, _selectedScratch);
            int next = 0;
            for (int i = 0; i < copied; i++)
            {
                Entity entity = _selectedScratch[i];
                if (entity == Entity.Null || !_world.IsAlive(entity))
                {
                    continue;
                }

                _selectedScratch[next++] = entity;
            }

            return next;
        }

        private void EnsureSelectedCapacity(int required)
        {
            if (required <= _selectedScratch.Length)
            {
                return;
            }

            int nextSize = _selectedScratch.Length == 0 ? 16 : _selectedScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _selectedScratch, nextSize);
        }

        private void EnsureRotationGroupCapacity(int required)
        {
            if (required <= _rotationGroupScratch.Length)
            {
                return;
            }

            int nextSize = _rotationGroupScratch.Length == 0 ? 8 : _rotationGroupScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _rotationGroupScratch, nextSize);
        }

        private void EnsureFormationOffsetCapacity(int required)
        {
            if (required <= _formationOffsetScratch.Length)
            {
                return;
            }

            int nextSize = _formationOffsetScratch.Length == 0 ? 16 : _formationOffsetScratch.Length;
            while (nextSize < required)
            {
                nextSize *= 2;
            }

            Array.Resize(ref _formationOffsetScratch, nextSize);
        }

        private bool ContainsRotationGroup(int count, int groupId)
        {
            for (int i = 0; i < count; i++)
            {
                if (_rotationGroupScratch[i] == groupId)
                {
                    return true;
                }
            }

            return false;
        }

        private static void SortByEntityId(Span<Entity> entities, int count)
        {
            for (int i = 1; i < count; i++)
            {
                Entity current = entities[i];
                int j = i - 1;
                while (j >= 0 && CompareEntities(entities[j], current) > 0)
                {
                    entities[j + 1] = entities[j];
                    j--;
                }

                entities[j + 1] = current;
            }
        }

        private static int CompareEntities(Entity a, Entity b)
        {
            int worldCompare = a.WorldId.CompareTo(b.WorldId);
            return worldCompare != 0 ? worldCompare : a.Id.CompareTo(b.Id);
        }

        private int CountPointGoals(int selectedCount)
        {
            int count = 0;
            for (int i = 0; i < selectedCount; i++)
            {
                Entity entity = _selectedScratch[i];
                if (_world.IsAlive(entity) &&
                    _world.TryGet(entity, out NavGoal2D goal) &&
                    goal.Kind == NavGoalKind2D.Point)
                {
                    count++;
                }
            }

            return count;
        }

        private int CountDetachedFromFlow(int selectedCount)
        {
            int count = 0;
            for (int i = 0; i < selectedCount; i++)
            {
                Entity entity = _selectedScratch[i];
                if (_world.IsAlive(entity) && !_world.Has<NavFlowBinding2D>(entity))
                {
                    count++;
                }
            }

            return count;
        }

        private static string Bool01(bool value)
        {
            return value ? "1" : "0";
        }

        private static void WriteCommandDebug(MassFlowNavPlaygroundState state, string message, bool log)
        {
            state.LastCommandDebug = message;
            if (log)
            {
                Log.Info(in LogChannels.Input, $"[MassFlowNav] {message}");
            }
        }
    }
}
