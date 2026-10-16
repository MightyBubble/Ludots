using Ludots.UI.Runtime;

namespace CrowdSimulationUiMod.UI;

/// <summary>
/// 节点在几组预先拼好的 class 串之间切换。选中项没变就什么都不做；变了才写属性并报告需要重建。
/// </summary>
internal sealed class CrowdHudClassSwitch
{
    private readonly UiElement _element;
    private readonly string[] _options;
    private int _current = -1;

    private CrowdHudClassSwitch(UiElement element, string[] options)
    {
        _element = element;
        _options = options;
    }

    public UiElement Element => _element;

    public int Current => _current;

    /// <summary>两态：关是去掉 <paramref name="token"/> 的原 class，开是再加上它。</summary>
    public static CrowdHudClassSwitch Toggle(UiElement element, string token)
    {
        string baseClass = Without(element.Attributes["class"], token);
        return new CrowdHudClassSwitch(element, [baseClass, Join(baseClass, token)]);
    }

    /// <summary>显隐：<see cref="Show"/> 为 true 时去掉 nf-hidden。</summary>
    public static CrowdHudClassSwitch Visibility(UiElement element) => Toggle(element, "nf-hidden");

    /// <summary>在原 class（去掉 <paramref name="tokens"/> 里出现的每一个）后面各接一个 token。</summary>
    public static CrowdHudClassSwitch Options(UiElement element, params string[] tokens)
    {
        string baseClass = element.Attributes["class"] ?? "";
        foreach (string token in tokens)
        {
            if (token.Length != 0) baseClass = Without(baseClass, token);
        }

        var options = new string[tokens.Length];
        for (int i = 0; i < tokens.Length; i++) options[i] = Join(baseClass, tokens[i]);
        return new CrowdHudClassSwitch(element, options);
    }

    /// <returns>class 是否改了。</returns>
    public bool Pick(int index)
    {
        if (index == _current) return false;
        _current = index;
        _element.Attributes["class"] = _options[index];
        return true;
    }

    public bool Set(bool on) => Pick(on ? 1 : 0);

    public bool Show(bool visible) => Pick(visible ? 0 : 1);

    private static string Without(string? classes, string token)
    {
        if (string.IsNullOrWhiteSpace(classes)) return "";
        string[] parts = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts.Where(p => !string.Equals(p, token, StringComparison.Ordinal)));
    }

    private static string Join(string baseClass, string token) =>
        token.Length == 0 ? baseClass : baseClass.Length == 0 ? token : baseClass + " " + token;
}
