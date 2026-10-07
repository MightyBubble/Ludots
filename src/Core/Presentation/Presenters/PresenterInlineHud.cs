using System;
using System.Numerics;
using Ludots.Core.Presentation.Config;
using Ludots.Core.Presentation.Hud;
using Ludots.Platform.Abstractions;
namespace Ludots.Core.Presentation.Presenters
{
    /// <summary>
    /// HUD 内联专 lane（spike，LUDOTS_INLINE_HUD=1 门控）：HUD-only 子定义（WorldText/WorldHud
    /// + Attachment + AttributeBinding + Style 子集）在创建期编译为父 presenter 上的内联描述符，
    /// 不再实例化子 presenter 实体——消灭每 agent 2 个子 presenter 的扇出（30K→10K presenter）。
    /// 配置面不变：同一 presenters.json，仅实例化策略不同。子集外定义回退经典扇出。
    /// </summary>
    public static class PresenterInlineHudFeature
    {
        private static bool? _enabledOverride;

        public static bool Enabled =>
            _enabledOverride ??
            Environment.GetEnvironmentVariable("LUDOTS_INLINE_HUD") is "1" or "true" or "yes" or "on";

        /// <summary>测试用：必须在任何 CreatePresenter 之前设置（boot 前）。</summary>
        public static void SetOverride(bool? value) => _enabledOverride = value;

        /// <summary>
        /// HUD 子集判定与描述符编译。支持的合同：恰好一个输出行为（WorldText 或 AssetBinding(WorldHud)）、
        /// 附件 target=Parent 且旋转恒等、不继承缩放、AttributeBinding 供值（文本值绑定 / 条形比率）、
        /// 颜色为静态 style。参数化颜色/缩放、非 Parent 附件、实例覆盖等回退经典扇出。
        /// </summary>
        public static bool TryCompileDescriptor(PresenterDefinition definition, out PresenterInlineHudDescriptor descriptor)
        {
            descriptor = default;
            if (!Enabled || definition.HasSurfaceAuthoring)
            {
                return false;
            }

            BehaviorSlot? outputSlot = null;
            BehaviorSlot? attachmentSlot = null;
            int ratioAttributeId = -1;
            foreach (BehaviorSlot slot in definition.Behaviors)
            {
                switch (slot.Kind)
                {
                    case BehaviorKind.WorldText:
                        if (outputSlot.HasValue)
                        {
                            return false;
                        }

                        outputSlot = slot;
                        break;
                    case BehaviorKind.AssetBinding when slot.AssetBinding.AssetKind == AssetKind.WorldHud:
                        if (outputSlot.HasValue)
                        {
                            return false;
                        }

                        outputSlot = slot;
                        break;
                    case BehaviorKind.Attachment:
                        if (attachmentSlot.HasValue)
                        {
                            return false;
                        }

                        attachmentSlot = slot;
                        break;
                    case BehaviorKind.AttributeBinding:
                        if (slot.AttributeBinding.Mode == ValueSourceKind.AttributeRatio)
                        {
                            ratioAttributeId = slot.AttributeBinding.AttributeId;
                        }

                        break;
                    default:
                        return false;
                }
            }

            if (!outputSlot.HasValue || !attachmentSlot.HasValue)
            {
                return false;
            }

            BehaviorSlot output = outputSlot.Value;
            BehaviorSlot attachment = attachmentSlot.Value;
            if (attachment.Attachment.Target != AttachmentTarget.Parent ||
                attachment.Attachment.InheritScale ||
                attachment.Attachment.RotationOffset != Quaternion.Identity)
            {
                return false;
            }

            if (output.AssetBinding.ColorParamKey >= 0 || !output.Style.HasColor)
            {
                return false;
            }

            if (output.Kind == BehaviorKind.WorldText)
            {
                if (output.WorldText.Args is { Length: > 0 })
                {
                    return false;
                }

                if (output.WorldText.BoundAttributeId == WorldTextConfig.UnboundAttributeId ||
                    (output.WorldText.Mode != WorldHudValueMode.AttributeCurrentOverBase &&
                     output.WorldText.Mode != WorldHudValueMode.AttributeCurrent))
                {
                    return false;
                }

                descriptor = new PresenterInlineHudDescriptor
                {
                    DefinitionId = definition.Id,
                    SlotIndex = output.SlotIndex,
                    Kind = WorldHudItemKind.Text,
                    Offset = attachment.Attachment.Offset,
                    FontSize = output.WorldText.FontSize > 0 ? output.WorldText.FontSize : 16,
                    Color = output.Style.Color,
                    BoundAttributeId = output.WorldText.BoundAttributeId,
                    ValueMode = output.WorldText.Mode,
                };
                return true;
            }

            if (ratioAttributeId < 0 || output.AssetBinding.ScaleParamKey >= 0)
            {
                return false;
            }

            Vector3 scale = output.AssetBinding.LocalScale;
            descriptor = new PresenterInlineHudDescriptor
            {
                DefinitionId = definition.Id,
                SlotIndex = output.SlotIndex,
                Kind = WorldHudItemKind.Bar,
                Offset = attachment.Attachment.Offset,
                Width = scale.X > 0f ? scale.X : 40f,
                Height = scale.Y > 0f ? scale.Y : 6f,
                Color = output.Style.Color,
                BoundAttributeId = ratioAttributeId,
            };
            return true;
        }
    }

    public struct PresenterInlineHudDescriptor
    {
        public int DefinitionId;
        public int SlotIndex;
        public WorldHudItemKind Kind;
        public Vector3 Offset;
        public int FontSize;
        public float Width;
        public float Height;
        public Vector4 Color;
        public int BoundAttributeId;
        public WorldHudValueMode ValueMode;

        // 合成期变更门控（运行态）：锚点（位置+旋转）/属性值/可见性任一变化才重合成，稳态零成本。
        public Vector3 LastAnchor;
        public Quaternion LastRotation;
        public float LastCurrent;
        public float LastBase;
        public byte LastPresent;
    }

    /// <summary>挂在父 presenter 上的内联 HUD 描述符集；随父实体生命周期存亡。
    /// AttributeDirty 由 PresenterBehaviorSystem 在属主属性变更时置位——emit 门控据此
    /// 决定是否重读属性，稳态纯结构体比对零实体查表。</summary>
    public struct PresenterInlineHud
    {
        public PresenterInlineHudDescriptor[] Descriptors;
        public byte AttributeDirty;
    }
}
