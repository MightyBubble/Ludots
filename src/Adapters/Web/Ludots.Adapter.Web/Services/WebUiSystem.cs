using Ludots.Core.UI;

namespace Ludots.Adapter.Web.Services
{
    public sealed class WebUiSystem : IUiSystem
    {
        public void SetHtml(string html, string css)
        {
            // No-op: web client renders its own UI
        }
    }
}
