namespace Ludots.Core.Presentation.AdapterSync
{
    /// <summary>
    /// Platform-neutral adapter op produced from a frame snapshot diff.
    /// </summary>
    public readonly struct StaticMeshAdapterSyncOp
    {
        public StaticMeshAdapterSyncOp(StaticMeshAdapterSyncOpKind kind, in StaticMeshAdapterBindingState binding)
        {
            Kind = kind;
            Binding = binding;
        }

        public StaticMeshAdapterSyncOpKind Kind { get; }

        public StaticMeshAdapterBindingState Binding { get; }
    }
}
