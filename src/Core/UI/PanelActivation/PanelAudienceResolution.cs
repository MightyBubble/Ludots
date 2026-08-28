using System;
using Ludots.Core.UI.PanelProjection;

namespace Ludots.Core.UI.PanelActivation
{
    /// <summary>
    /// Effective-audience resolution: the runtime override recorded in the activation
    /// store (hotseat rotation) wins over the template's declared audience; no override
    /// means the declaration rules. Admission and surface placement both consume this
    /// one resolution — there is no second precedence rule.
    /// </summary>
    public static class PanelAudienceResolution
    {
        public static PanelAudience Effective(PanelTemplate template, UiPanelActivationStore? activation)
        {
            ArgumentNullException.ThrowIfNull(template);
            if (activation != null && activation.TryGetAudienceOverride(template.Id, out PanelAudience overrideAudience))
            {
                return overrideAudience;
            }

            return template.Audience;
        }
    }
}
