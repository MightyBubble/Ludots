using Ludots.Core.UI;

namespace Ludots.Adapter.UE5
{
    public sealed class UE5UiSystem : IUiSystem
    {
        /// <summary>最后一次由 Ludots 设置的 HTML 内容（供 UE5 侧按需读取）。</summary>
        public string LastHtml { get; private set; } = string.Empty;

        /// <summary>最后一次由 Ludots 设置的 CSS 内容。</summary>
        public string LastCss  { get; private set; } = string.Empty;

        /// <summary>当 Ludots 写入新 HTML/CSS 时触发（UE5 侧订阅以更新 WebUI）。</summary>
        public event Action<string, string>? OnHtmlChanged;

        public void SetHtml(string html, string css)
        {
            LastHtml = html;
            LastCss  = css;
            OnHtmlChanged?.Invoke(html, css);
        }
    }
}
