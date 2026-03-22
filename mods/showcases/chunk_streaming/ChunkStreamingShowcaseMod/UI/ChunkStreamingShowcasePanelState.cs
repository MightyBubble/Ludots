namespace ChunkStreamingShowcaseMod.UI
{
    internal readonly record struct ChunkStreamingShowcasePanelState(
        string Title,
        string Status,
        string Camera,
        string Chunks,
        string Hint)
    {
        public static readonly ChunkStreamingShowcasePanelState Empty = new(
            "Chunk Streaming Showcase",
            "Chunk window ready. Use the panel to jump between landmarks and inspect streamed road splines.",
            "Camera (0,0)",
            "Chunks 0 | Nodes 0 | Splines 0",
            "Jump west/center/east to inspect chunk windows.");
    }
}
