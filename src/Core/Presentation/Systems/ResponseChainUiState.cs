using Arch.Core;
using Ludots.Core.Gameplay.GAS.Orders;
 
namespace Ludots.Core.Presentation.Systems
{
    public sealed class ResponseChainUiState
    {
        public bool Dirty { get; private set; } = true;
        public bool Visible { get; private set; }
        public int RootId { get; private set; }
        public int PlayerId { get; private set; }
        public int PromptTagId { get; private set; }
        public Entity Actor { get; private set; }
        public Entity Target { get; private set; }
        public Entity TargetContext { get; private set; }
 
        public int AllowedCount { get; private set; }
        public int[] AllowedOrderTagIds { get; } = new int[OrderRequest.MaxAllowed];
 
        public void ApplyRequest(in OrderRequest request)
        {
            Visible = true;
            RootId = request.RequestId;
            PlayerId = request.PlayerId;
            PromptTagId = request.PromptTagId;
            Actor = request.Actor;
            Target = request.Target;
            TargetContext = request.TargetContext;
 
            AllowedCount = request.AllowedCount;
            for (int i = 0; i < AllowedOrderTagIds.Length; i++)
            {
                AllowedOrderTagIds[i] = i < request.AllowedCount ? request.GetAllowed(i) : 0;
            }
 
            Dirty = true;
        }
 
        public void Close(int rootId)
        {
            if (!Visible || RootId != rootId) return;
            Visible = false;
            Dirty = true;
        }
 
        public void MarkClean()
        {
            Dirty = false;
        }
    }
}
