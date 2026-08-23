using System;
using Ludots.Core.Components;
using Ludots.Core.Mathematics.FixedPoint;

namespace Ludots.Core.Gameplay.Attachment
{
    /// <summary>
    /// AttachedLocalPose 的 authoring 解析单点（effect relation 块与 EntityTemplate.children 共用）。
    /// 严格解析：字段缺省即 fail-fast，无隐藏默认。
    /// </summary>
    public static class AttachedLocalPoseAuthoring
    {
        public static AttachedOffsetRotation ParseOffsetRotation(string? raw, string context)
        {
            return raw switch
            {
                "None" => AttachedOffsetRotation.None,
                "ParentFacing" => AttachedOffsetRotation.ParentFacing,
                "OwnFacing" => AttachedOffsetRotation.OwnFacing,
                _ => throw new InvalidOperationException(
                    $"{context}: offsetRotation '{raw}' is not configured (expected None, ParentFacing or OwnFacing)."),
            };
        }

        public static int RequireInt(int? value, string context, string field)
        {
            if (value == null)
            {
                throw new InvalidOperationException($"{context}: {field} is required.");
            }

            return value.Value;
        }

        public static bool RequireBool(bool? value, string context, string field)
        {
            if (value == null)
            {
                throw new InvalidOperationException($"{context}: {field} is required.");
            }

            return value.Value;
        }

        public static AttachedLocalPose Parse(Config.EntityTemplateLocalPose? cfg, string context)
        {
            if (cfg == null)
            {
                throw new InvalidOperationException($"{context}: localPose is required.");
            }

            int offsetXCm = RequireInt(cfg.OffsetXCm, context, "localPose.offsetXCm");
            int offsetYCm = RequireInt(cfg.OffsetYCm, context, "localPose.offsetYCm");
            int facingDeg = RequireInt(cfg.FacingDeg, context, "localPose.facingDeg");
            bool inheritParentFacing = RequireBool(cfg.InheritParentFacing, context, "localPose.inheritParentFacing");
            AttachedOffsetRotation offsetRotation = ParseOffsetRotation(cfg.OffsetRotation, context);
            if (inheritParentFacing && offsetRotation == AttachedOffsetRotation.OwnFacing)
            {
                throw new InvalidOperationException(
                    $"{context}: localPose.inheritParentFacing=true 与 offsetRotation=OwnFacing 互斥（朝向同时随父又随子无定义）。");
            }

            return new AttachedLocalPose
            {
                OffsetCm = Fix64Vec2.FromInt(offsetXCm, offsetYCm),
                LocalFacingRad = Fix64.FromInt(facingDeg) * (Fix64.Pi / Fix64.FromInt(180)),
                InheritParentFacing = inheritParentFacing ? (byte)1 : (byte)0,
                OffsetRotation = offsetRotation,
            };
        }
    }
}
