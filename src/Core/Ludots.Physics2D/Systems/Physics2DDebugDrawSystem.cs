using System;
using System.Numerics;
using Arch.Core;
using Arch.Core.Extensions;
using Arch.System;
using Ludots.Core.Gameplay.Spawning;
using Ludots.Core.Mathematics.FixedPoint;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.DebugDraw;
using Ludots.Core.Physics2D.Components;

namespace Ludots.Core.Physics2D.Systems
{
    /// <summary>
    /// Debug draw system for Physics2D.
    /// 
    /// 这是唯一允许 Fix64 → float 转换的边界层（渲染输出）。
    /// 
    /// 坐标系：
    /// - Position2D / ShapeData: 定点数厘米 (Fix64)
    /// - VisualTransform: 浮点米 (float m)
    /// - DebugDraw 输出: 浮点米 (float m)
    /// </summary>
    public sealed class Physics2DDebugDrawSystem : BaseSystem<World, float>
    {
        private const float CmToM = 0.01f;

        // 阻尼颜色阈值（调试用，无需定点数精度）
        private static readonly Fix64 DampingThresholdHigh = Fix64.FromFloat(0.995f);
        private static readonly Fix64 DampingThresholdMed = Fix64.FromFloat(0.98f);
        private static readonly Fix64 DampingThresholdLow = Fix64.FromFloat(0.90f);

        private readonly DebugDrawCommandBuffer _buffer;
        private readonly ShapeDataStorage2D _shapeStorage;

        private readonly QueryDescription _rigidBodyWithVisualQuery = new QueryDescription()
            .WithAll<Position2D, Collider2D, Mass2D, VisualTransform>();

        private readonly QueryDescription _rigidBodyWithoutVisualQuery = new QueryDescription()
            .WithAll<Position2D, Collider2D, Mass2D>()
            .WithNone<VisualTransform>();

        private readonly QueryDescription _compoundRigidBodyWithVisualQuery = new QueryDescription()
            .WithAll<Position2D, CompoundObstacle2DState, Mass2D, VisualTransform>();

        private readonly QueryDescription _compoundRigidBodyWithoutVisualQuery = new QueryDescription()
            .WithAll<Position2D, CompoundObstacle2DState, Mass2D>()
            .WithNone<VisualTransform>();

        private readonly QueryDescription _collisionPairQuery = new QueryDescription().WithAll<CollisionPair, ActiveCollisionPairTag>();
        private readonly QueryDescription _dampingFieldQuery = new QueryDescription().WithAll<Position2D, DampingField>();

        [Obsolete("不再需要手动设置 InterpolationAlpha，使用正式链路 WorldToVisualSyncSystem")]
        public float InterpolationAlpha { get; set; } = 1f;
        public float DefaultThickness { get; set; } = 1f;

        public Physics2DDebugDrawSystem(World world, DebugDrawCommandBuffer buffer, ShapeDataStorage2D shapeStorage) : base(world)
        {
            _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
            _shapeStorage = shapeStorage ?? throw new ArgumentNullException(nameof(shapeStorage));
        }

        public override void Update(in float dt)
        {
            _buffer.Clear();

            DrawDampingFields();
            DrawRigidBodies();
            DrawCollisionPairs();
        }

        private void DrawDampingFields()
        {
            World.Query(in _dampingFieldQuery, (ref Position2D pos, ref DampingField field) =>
            {
                var color = field.DampingValue >= DampingThresholdHigh ? DebugDrawColor.Cyan :
                            field.DampingValue >= DampingThresholdMed ? DebugDrawColor.Gray :
                            field.DampingValue >= DampingThresholdLow ? DebugDrawColor.Green :
                            DebugDrawColor.Yellow;

                // Fix64 → float 渲染边界
                var centerM = pos.Value.ToVector2() * CmToM;
                _buffer.Circles.Add(new DebugDrawCircle2D
                {
                    Center = centerM,
                    Radius = field.Radius.ToFloat() * CmToM,
                    Thickness = DefaultThickness,
                    Color = color
                });
            });
        }

        private void DrawRigidBodies()
        {
            World.Query(in _rigidBodyWithVisualQuery, (Entity entity, ref Position2D pos, ref Collider2D collider, ref Mass2D mass, ref VisualTransform visual) =>
            {
                var drawPosM = new Vector2(visual.Position.X, visual.Position.Z);
                DrawRigidBodyMeters(entity, ref collider, ref mass, drawPosM);
            });

            World.Query(in _rigidBodyWithoutVisualQuery, (Entity entity, ref Position2D pos, ref Collider2D collider, ref Mass2D mass) =>
            {
                var drawPosM = pos.Value.ToVector2() * CmToM;
                DrawRigidBodyMeters(entity, ref collider, ref mass, drawPosM);
            });

            World.Query(in _compoundRigidBodyWithVisualQuery, (Entity entity, ref Position2D pos, ref CompoundObstacle2DState state, ref Mass2D mass, ref VisualTransform visual) =>
            {
                var drawPosM = new Vector2(visual.Position.X, visual.Position.Z);
                DrawCompoundRigidBodyMeters(entity, ref state, ref mass, drawPosM);
            });

            World.Query(in _compoundRigidBodyWithoutVisualQuery, (Entity entity, ref Position2D pos, ref CompoundObstacle2DState state, ref Mass2D mass) =>
            {
                var drawPosM = pos.Value.ToVector2() * CmToM;
                DrawCompoundRigidBodyMeters(entity, ref state, ref mass, drawPosM);
            });
        }

        private void DrawCompoundRigidBodyMeters(Entity entity, ref CompoundObstacle2DState state, ref Mass2D mass, Vector2 drawPosM)
        {
            if (state.SinkPhysicsCollider == 0)
            {
                return;
            }

            for (int i = 0; i < state.PieceCount; i++)
            {
                var collider = new Collider2D
                {
                    Type = ToColliderType(state.GetShape(i)),
                    ShapeDataIndex = state.GetShapeDataIndex(i)
                };
                DrawRigidBodyMeters(entity, ref collider, ref mass, drawPosM);
            }
        }

        private void DrawRigidBodyMeters(Entity entity, ref Collider2D collider, ref Mass2D mass, Vector2 drawPosM)
        {
            Fix64 rotation = Fix64.Zero;
            if (World.TryGet(entity, out Rotation2D rotationComponent))
            {
                rotation = rotationComponent.Value;
            }

            DebugDrawColor color;
            if (World.Has<SleepingTag>(entity))
            {
                color = DebugDrawColor.Gray;
            }
            else
            {
                color = mass.IsStatic ? DebugDrawColor.Blue :
                        mass.IsKinematic ? DebugDrawColor.Yellow :
                        DebugDrawColor.Green;
            }

            switch (collider.Type)
            {
                case ColliderType2D.Circle:
                {
                    if (!_shapeStorage.TryGetCircle(collider.ShapeDataIndex, out var circle)) return;
                    // Fix64 → float 渲染边界
                    _buffer.Circles.Add(new DebugDrawCircle2D
                    {
                        Center = drawPosM + ShapeWorldTransform2D.RotateLocal(circle.LocalCenter, rotation).ToVector2() * CmToM,
                        Radius = circle.Radius.ToFloat() * CmToM,
                        Thickness = DefaultThickness,
                        Color = color
                    });
                    break;
                }
                case ColliderType2D.Box:
                {
                    if (!_shapeStorage.TryGetBox(collider.ShapeDataIndex, out var box)) return;
                    _buffer.Boxes.Add(new DebugDrawBox2D
                    {
                        Center = drawPosM + ShapeWorldTransform2D.RotateLocal(box.LocalCenter, rotation).ToVector2() * CmToM,
                        HalfWidth = box.HalfWidth.ToFloat() * CmToM,
                        HalfHeight = box.HalfHeight.ToFloat() * CmToM,
                        RotationRadians = rotation.ToFloat(),
                        Thickness = DefaultThickness,
                        Color = color
                    });
                    break;
                }
                case ColliderType2D.Polygon:
                {
                    if (!_shapeStorage.TryGetPolygon(collider.ShapeDataIndex, out var poly) || poly.Vertices == null || poly.VertexCount < 3) return;
                    Fix64 sin = Fix64.Zero;
                    Fix64 cos = Fix64.OneValue;
                    if (rotation != Fix64.Zero)
                    {
                        sin = Fix64Math.Sin(rotation);
                        cos = Fix64Math.Cos(rotation);
                    }

                    for (int i = 0; i < poly.VertexCount; i++)
                    {
                        var aLocal = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, i);
                        var bLocal = ShapeWorldTransform2D.GetPolygonLocalVertex(poly, (i + 1) % poly.VertexCount);
                        if (rotation != Fix64.Zero)
                        {
                            aLocal = ShapeWorldTransform2D.RotateLocal(aLocal, sin, cos);
                            bLocal = ShapeWorldTransform2D.RotateLocal(bLocal, sin, cos);
                        }

                        var a = drawPosM + aLocal.ToVector2() * CmToM;
                        var b = drawPosM + bLocal.ToVector2() * CmToM;
                        _buffer.Lines.Add(new DebugDrawLine2D
                        {
                            A = a,
                            B = b,
                            Thickness = DefaultThickness,
                            Color = color
                        });
                    }
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void DrawCollisionPairs()
        {
            World.Query(in _collisionPairQuery, (ref CollisionPair pair) =>
            {
                if (!World.IsAlive(pair.EntityA) || !World.IsAlive(pair.EntityB)) return;
                if (pair.ContactCount == 0) return;

                if (!World.TryGet(pair.EntityA, out Position2D posA) ||
                    !World.TryGet(pair.EntityB, out Position2D posB))
                {
                    return;
                }

                // Fix64 → float 渲染边界
                _buffer.Lines.Add(new DebugDrawLine2D
                {
                    A = posA.Value.ToVector2() * CmToM,
                    B = posB.Value.ToVector2() * CmToM,
                    Thickness = DefaultThickness,
                    Color = DebugDrawColor.Red
                });

                if (pair.ContactCount > 0)
                {
                    var p = ((pair.PositionA.Value + pair.PositionB.Value) * Fix64.HalfValue).ToVector2() * CmToM;
                    var normal = pair.Normal.ToVector2();
                    float penM = pair.Penetration.ToFloat() * CmToM;
                    _buffer.Lines.Add(new DebugDrawLine2D
                    {
                        A = p,
                        B = p + normal * MathF.Max(penM, 0.001f),
                        Thickness = DefaultThickness,
                        Color = DebugDrawColor.Cyan
                    });
                }
            });
        }

        private static ColliderType2D ToColliderType(ManifestationObstacleShape2D shape)
        {
            return shape switch
            {
                ManifestationObstacleShape2D.Circle => ColliderType2D.Circle,
                ManifestationObstacleShape2D.Box => ColliderType2D.Box,
                ManifestationObstacleShape2D.Polygon => ColliderType2D.Polygon,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
        }
    }
}
