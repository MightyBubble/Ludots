using System;
using Ludots.Core.Presentation.Components;
using Ludots.Core.Presentation.Rendering;
using Ludots.Platform.Abstractions;

namespace Ludots.Core.Presentation.Instancing
{
    public static class InstancedBatchCapabilityValidator
    {
        public static void Validate(
            InstancedBatchRequestBuffer requests,
            InstancedBatchOperationBuffer operations,
            PresentationAdapterCapabilities? capabilities)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (operations == null) throw new ArgumentNullException(nameof(operations));

            ReadOnlySpan<InstancedBatchRequest> requestSpan = requests.GetSpan();
            ReadOnlySpan<InstancedBatchOperation> operationSpan = operations.GetSpan();
            if (requestSpan.Length == 0 && operationSpan.Length == 0)
            {
                return;
            }

            if (capabilities == null)
            {
                throw new InvalidOperationException(
                    $"Presentation adapter has not declared capabilities for instanced batch work ({requestSpan.Length} request(s), {operationSpan.Length} operation(s)).");
            }

            for (int i = 0; i < requestSpan.Length; i++)
            {
                ValidateRequest(in requestSpan[i], capabilities);
            }

            for (int i = 0; i < operationSpan.Length; i++)
            {
                ValidateOperation(in operationSpan[i], capabilities);
            }
        }

        private static void ValidateRequest(in InstancedBatchRequest request, PresentationAdapterCapabilities capabilities)
        {
            PresentationVisualCapabilities required = request.RenderPath switch
            {
                VisualRenderPath.InstancedStaticMesh => PresentationVisualCapabilities.InstancedStaticMeshBatch,
                VisualRenderPath.HierarchicalInstancedStaticMesh => PresentationVisualCapabilities.HierarchicalInstancedStaticMeshBatch,
                _ => PresentationVisualCapabilities.None,
            };

            if (required == PresentationVisualCapabilities.None || !capabilities.Visuals.HasFlag(required))
            {
                throw new InvalidOperationException(
                    $"Presentation adapter does not support instanced batch renderPath '{request.RenderPath}' (batchAssetId={request.BatchAssetId}).");
            }
        }

        private static void ValidateOperation(in InstancedBatchOperation operation, PresentationAdapterCapabilities capabilities)
        {
            PresentationVisualCapabilities required = operation.Kind switch
            {
                InstancedBatchOperationKind.SetVisibility => PresentationVisualCapabilities.InstancedBatchVisibility,
                InstancedBatchOperationKind.WriteCustomData => PresentationVisualCapabilities.InstanceCustomData,
                InstancedBatchOperationKind.SetPresentationState => PresentationVisualCapabilities.InstancedBatchPresentationState,
                InstancedBatchOperationKind.Refresh => PresentationVisualCapabilities.InstancedBatchRefresh,
                InstancedBatchOperationKind.AttachEffect => PresentationVisualCapabilities.InstancedBatchEffect,
                InstancedBatchOperationKind.UpdateEffect => PresentationVisualCapabilities.InstancedBatchEffect,
                InstancedBatchOperationKind.RemoveEffect => PresentationVisualCapabilities.InstancedBatchEffect,
                _ => PresentationVisualCapabilities.None,
            };

            if (required == PresentationVisualCapabilities.None || !capabilities.Visuals.HasFlag(required))
            {
                throw new InvalidOperationException(
                    $"Presentation adapter does not support instanced batch operation '{operation.Kind}' (batchAssetId={operation.BatchAssetId}).");
            }
        }
    }
}
