using System;

namespace Ludots.Core.Presentation.Components
{
    /// <summary>
    /// Central contract guards for separating static instance lanes from skinned runtime lanes.
    /// </summary>
    public static class PresentationRenderContract
    {
        public static void ValidateTemplate(string sourceName, VisualRenderPath renderPath, int animatorControllerId)
        {
            if (!renderPath.IsSkinnedLane() && animatorControllerId > 0)
            {
                throw new InvalidOperationException(
                    $"{sourceName} uses render path '{renderPath}' but also sets animatorControllerId={animatorControllerId}. " +
                    "Only skinned lanes may consume AnimatorPackedState.");
            }

            if (renderPath.RequiresExplicitAnimatorController() && animatorControllerId <= 0)
            {
                throw new InvalidOperationException(
                    $"{sourceName} uses skinned render path '{renderPath}' without an animatorControllerId. " +
                    "Skinned lanes require an explicit animator controller because Core does not emit pose or bone-palette data.");
            }
        }

        public static void ValidateAnimatorAuthoring(string sourceName, VisualRenderPath renderPath)
        {
            if (!renderPath.SupportsAnimatorPackedState())
            {
                throw new InvalidOperationException(
                    $"{sourceName} targets render path '{renderPath}', but AnimatorPackedState is reserved for skinned lanes. " +
                    "Do not route static instance visuals through the skinned runtime contract.");
            }
        }

        public static void ValidateRuntimeState(
            string sourceName,
            in VisualRuntimeState visual,
            bool hasAnimatorComponent,
            in AnimatorPackedState animator)
        {
            int packedControllerId = animator.GetControllerId();

            if (!visual.RenderPath.IsSkinnedLane())
            {
                if (visual.AnimatorControllerId > 0 || visual.HasAnimator || hasAnimatorComponent || packedControllerId > 0)
                {
                    throw new InvalidOperationException(
                        $"{sourceName} produced non-skinned render path '{visual.RenderPath}' with animator data attached. " +
                        "Static lane dirty sync must stay separate from skinned runtime sync.");
                }

                return;
            }

            if (visual.AnimatorControllerId <= 0)
            {
                throw new InvalidOperationException(
                    $"{sourceName} produced skinned render path '{visual.RenderPath}' without VisualRuntimeState.AnimatorControllerId.");
            }

            if (!visual.HasAnimator)
            {
                throw new InvalidOperationException(
                    $"{sourceName} produced skinned render path '{visual.RenderPath}' without VisualRuntimeFlags.HasAnimator.");
            }

            if (!hasAnimatorComponent)
            {
                throw new InvalidOperationException(
                    $"{sourceName} produced skinned render path '{visual.RenderPath}' without an AnimatorPackedState component.");
            }

            if (packedControllerId <= 0)
            {
                throw new InvalidOperationException(
                    $"{sourceName} produced skinned render path '{visual.RenderPath}' with an empty AnimatorPackedState controller id.");
            }

            if (packedControllerId != visual.AnimatorControllerId)
            {
                throw new InvalidOperationException(
                    $"{sourceName} produced skinned render path '{visual.RenderPath}' with mismatched animator controller ids. " +
                    $"VisualRuntimeState={visual.AnimatorControllerId}, AnimatorPackedState={packedControllerId}.");
            }
        }
    }
}
