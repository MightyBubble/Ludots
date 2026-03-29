namespace Ludots.Adapter.UE5
{
    public readonly record struct HostWorldBindingSnapshot(string WorldName, long Generation)
    {
        public string CurrentWorldName => WorldName;

        public long BindGeneration => Generation;

        public static HostWorldBindingSnapshot Empty => new(string.Empty, 0);
    }
}
