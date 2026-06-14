namespace Ludots.Core.Presentation.Assets
{
    public readonly struct PresentationBehaviorStateDefinition
    {
        public PresentationBehaviorStateDefinition(string stateId, int prefabAssetId)
        {
            StateId = stateId;
            PrefabAssetId = prefabAssetId;
        }

        public string StateId { get; }

        public int PrefabAssetId { get; }
    }
}
