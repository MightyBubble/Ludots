using System;

namespace Ludots.Core.Presentation.Rendering
{
    public static class PresentationVisualCapabilityValidator
    {
        public static void Validate(PresentationVisualRequestBuffer requests, PresentationAdapterCapabilities? capabilities)
        {
            Validate(requests, capabilities, targetGeneration: null);
        }

        public static void ValidateTargetLifecycle(
            PresentationAdapterCapabilities? capabilities,
            PresentationTargetGeneration? targetGeneration)
        {
            if (capabilities != null &&
                capabilities.Visuals.HasFlag(PresentationVisualCapabilities.ExternalTargetLifecycle) &&
                targetGeneration == null)
            {
                throw new InvalidOperationException(
                    "Presentation adapter declares an external target lifecycle but has not wired PresentationTargetGeneration.");
            }
        }

        public static void Validate(
            PresentationVisualRequestBuffer requests,
            PresentationAdapterCapabilities? capabilities,
            PresentationTargetGeneration? targetGeneration)
        {
            ValidateTargetLifecycle(capabilities, targetGeneration);

            if (requests == null) throw new ArgumentNullException(nameof(requests));

            ReadOnlySpan<PresentationVisualRequest> span = requests.GetSpan();
            if (span.Length == 0)
            {
                return;
            }

            if (capabilities == null)
            {
                throw new InvalidOperationException(
                    $"Presentation adapter has not declared capabilities for {span.Length} typed visual request(s).");
            }

            for (int i = 0; i < span.Length; i++)
            {
                ref readonly PresentationVisualRequest request = ref span[i];
                if (!capabilities.Supports(request.Kind))
                {
                    throw new InvalidOperationException(
                        $"Presentation adapter does not support typed visual request '{request.Kind}' (stableId={request.StableId}, asset='{request.AssetKey}').");
                }
            }
        }
    }
}
