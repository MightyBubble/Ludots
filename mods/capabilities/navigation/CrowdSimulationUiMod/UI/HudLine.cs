using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 一行 HUD 文案。格式化写进 <see cref="Buffer"/>，<see cref="Commit"/> 把整行写进 <see cref="Whole"/>，
/// 再按 “ · ” 拆进各段绑定（不换行的小段由 flex-wrap 折行）。段数变了页面结构才变，其余只改绑定。
/// </summary>
internal sealed class HudLine
{
    public const string ChipSeparator = " · ";

    // 留足最长一行（展开后的活动模板、遥测行）的余量，采样帧里绑定不再扩容。
    private const int LineCapacity = 256;
    private const int PartCapacity = 64;

    private readonly List<UiTextBinding> _parts = new();

    public UiTextBuffer Buffer { get; } = new(LineCapacity);

    public UiTextBinding Whole { get; } = new(LineCapacity);

    public int PartCount { get; private set; }

    public bool IsEmpty => Whole.Length == 0;

    public static HudLine[] Many(int count)
    {
        var lines = new HudLine[count];
        for (int i = 0; i < count; i++) lines[i] = new HudLine();
        return lines;
    }

    public UiTextBinding Part(int index)
    {
        if ((uint)index >= (uint)PartCount) throw new ArgumentOutOfRangeException(nameof(index));
        return _parts[index];
    }

    public void Set(string text)
    {
        Buffer.Clear();
        Buffer.Append(text);
    }

    /// <returns>段数是否变了；变了要重组页面。</returns>
    public bool Commit()
    {
        ReadOnlySpan<char> text = Buffer.Span;
        Whole.Set(text);
        int count = 0;
        int start = 0;
        while (start <= text.Length)
        {
            int found = text.Slice(start).IndexOf(ChipSeparator.AsSpan());
            int end = found < 0 ? text.Length : start + found;
            if (end > start)
            {
                if (count == _parts.Count) _parts.Add(new UiTextBinding(PartCapacity));
                _parts[count++].Set(text.Slice(start, end - start));
            }

            if (found < 0) break;
            start = end + ChipSeparator.Length;
        }

        bool reshaped = count != PartCount;
        PartCount = count;
        return reshaped;
    }

    public override string ToString() => Buffer.ToString();
}
