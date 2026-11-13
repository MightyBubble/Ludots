using System.Text;
using Ludots.Core.Presentation.Hud;
using Ludots.UI.Browser;

namespace WebUiVfsShowcaseMod;

public static class WebUiVfsShowcasePage
{
    public const string ImageSourceSlot = "{{image-src}}";
    public const string BrowserAppDirectory = "assets/browser-app";
    public const string TemplateFileName = "index.html";
    public const string StyleFileName = "styles.css";

    private const string ImageAssetAttribute = "data-image-asset=\"";

    public static string Compose(string templateHtml, PresentationImageAssetCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(templateHtml);
        ArgumentNullException.ThrowIfNull(catalog);

        string assetId = ReadSingleImageAssetId(templateHtml);
        PresentationImageAssetDefinition asset = catalog.Require(assetId);
        if (!templateHtml.Contains(ImageSourceSlot, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Browser page is missing the image source slot '{ImageSourceSlot}'.");
        }

        string url = BrowserVfsUri.FromVfsPath(asset.Path);
        return templateHtml.Replace(ImageSourceSlot, url, StringComparison.Ordinal);
    }

    public static void Materialize(string templateDirectory, string destinationDirectory, PresentationImageAssetCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);

        string templatePath = Path.Combine(templateDirectory, TemplateFileName);
        string stylePath = Path.Combine(templateDirectory, StyleFileName);
        if (!File.Exists(templatePath))
        {
            throw new FileNotFoundException("Browser page template was not found.", templatePath);
        }

        if (!File.Exists(stylePath))
        {
            throw new FileNotFoundException("Browser page style was not found.", stylePath);
        }

        string html = Compose(File.ReadAllText(templatePath, Encoding.UTF8), catalog);
        Directory.CreateDirectory(destinationDirectory);
        File.WriteAllText(Path.Combine(destinationDirectory, TemplateFileName), html, Encoding.UTF8);
        File.Copy(stylePath, Path.Combine(destinationDirectory, StyleFileName), overwrite: true);
    }

    public static string ReadSingleImageAssetId(string templateHtml)
    {
        ArgumentNullException.ThrowIfNull(templateHtml);
        int first = templateHtml.IndexOf(ImageAssetAttribute, StringComparison.Ordinal);
        if (first < 0)
        {
            throw new InvalidOperationException("Browser page does not name a presentation image asset.");
        }

        int start = first + ImageAssetAttribute.Length;
        int end = templateHtml.IndexOf('"', start);
        if (end <= start)
        {
            throw new InvalidOperationException("Browser page image asset id is empty.");
        }

        if (templateHtml.IndexOf(ImageAssetAttribute, end, StringComparison.Ordinal) >= 0)
        {
            throw new InvalidOperationException("Browser page names more than one presentation image asset.");
        }

        return templateHtml[start..end];
    }
}
