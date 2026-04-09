using System;

namespace VisualTerrainEditorMod.Runtime;

internal sealed class VisualTerrainBindingDescriptor
{
    public static readonly VisualTerrainBindingDescriptor None = new(VisualTerrainBindingKind.None);

    public VisualTerrainBindingDescriptor(
        VisualTerrainBindingKind kind,
        int logicalColumns = 0,
        int logicalRows = 0)
    {
        if (logicalColumns < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalColumns));
        }

        if (logicalRows < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(logicalRows));
        }

        if (kind == VisualTerrainBindingKind.None && (logicalColumns != 0 || logicalRows != 0))
        {
            throw new ArgumentException("Standalone visual terrain binding cannot declare logical dimensions.");
        }

        Kind = kind;
        LogicalColumns = logicalColumns;
        LogicalRows = logicalRows;
    }

    public VisualTerrainBindingKind Kind { get; }

    public int LogicalColumns { get; }

    public int LogicalRows { get; }

    public bool IsEnabled => Kind != VisualTerrainBindingKind.None;
}
