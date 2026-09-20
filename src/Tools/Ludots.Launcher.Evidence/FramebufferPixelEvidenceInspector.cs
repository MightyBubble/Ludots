using SkiaSharp;

namespace Ludots.Launcher.Evidence;

public sealed record FramebufferPixelInspectionRequest(
    int SchemaVersion,
    string ImagePath,
    int ExpectedWidth,
    int ExpectedHeight,
    IReadOnlyList<FramebufferPixelRequirement> Requirements);

public sealed record FramebufferPixelRequirement(
    string Role,
    string PresentationTemplate,
    int MaximumChannelDifference,
    int MinimumPixelsPerRegion,
    int MinimumPassingRegions,
    IReadOnlyList<FramebufferPixelColor> AcceptedColors,
    IReadOnlyList<FramebufferPixelRegion> Regions);

public sealed record FramebufferPixelColor(byte Red, byte Green, byte Blue);

public sealed record FramebufferPixelRegion(string Id, int X, int Y, int Width, int Height);

public sealed record FramebufferPixelInspectionResult(
    int SchemaVersion,
    string ImagePath,
    int Width,
    int Height,
    bool Passed,
    IReadOnlyList<FramebufferPixelRequirementResult> Requirements);

public sealed record FramebufferPixelRequirementResult(
    string Role,
    string PresentationTemplate,
    int MinimumPixelsPerRegion,
    int MinimumPassingRegions,
    int PassingRegions,
    int MatchingPixels,
    bool Passed,
    IReadOnlyList<FramebufferPixelRegionResult> Regions);

public sealed record FramebufferPixelRegionResult(
    string Id,
    int X,
    int Y,
    int Width,
    int Height,
    int MatchingPixels,
    bool Passed);

public static class FramebufferPixelEvidenceInspector
{
    private const int SupportedSchemaVersion = 1;

    public static FramebufferPixelInspectionResult Inspect(FramebufferPixelInspectionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.SchemaVersion != SupportedSchemaVersion)
        {
            throw new InvalidOperationException(
                $"Unsupported framebuffer pixel inspection schema {request.SchemaVersion}; expected {SupportedSchemaVersion}.");
        }

        string imagePath = Path.GetFullPath(RequireText(request.ImagePath, "ImagePath"));
        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Framebuffer PNG does not exist.", imagePath);
        }

        if (request.ExpectedWidth <= 0 || request.ExpectedHeight <= 0)
        {
            throw new InvalidOperationException("Expected framebuffer dimensions must be positive.");
        }

        if (request.Requirements == null || request.Requirements.Count == 0)
        {
            throw new InvalidOperationException("At least one framebuffer pixel requirement is required.");
        }

        using SKCodec codec = SKCodec.Create(imagePath)
            ?? throw new InvalidOperationException($"Unable to decode framebuffer PNG: {imagePath}");
        if (codec.EncodedFormat != SKEncodedImageFormat.Png)
        {
            throw new InvalidOperationException($"Framebuffer evidence must be a PNG image: {imagePath}");
        }

        using SKBitmap bitmap = SKBitmap.Decode(codec)
            ?? throw new InvalidOperationException($"Unable to decode framebuffer PNG pixels: {imagePath}");
        if (bitmap.Width != request.ExpectedWidth || bitmap.Height != request.ExpectedHeight)
        {
            throw new InvalidOperationException(
                $"Framebuffer PNG is {bitmap.Width}x{bitmap.Height}; expected {request.ExpectedWidth}x{request.ExpectedHeight}.");
        }

        SKColor[] pixels = bitmap.Pixels;
        var requirementResults = new List<FramebufferPixelRequirementResult>(request.Requirements.Count);
        foreach (FramebufferPixelRequirement requirement in request.Requirements)
        {
            ValidateRequirement(requirement, bitmap.Width, bitmap.Height);
            var regionResults = new List<FramebufferPixelRegionResult>(requirement.Regions.Count);
            int passingRegions = 0;
            int matchingPixels = 0;
            foreach (FramebufferPixelRegion region in requirement.Regions)
            {
                int regionMatchingPixels = CountMatchingPixels(
                    pixels,
                    bitmap.Width,
                    region,
                    requirement.AcceptedColors,
                    requirement.MaximumChannelDifference);
                bool regionPassed = regionMatchingPixels >= requirement.MinimumPixelsPerRegion;
                if (regionPassed)
                {
                    passingRegions++;
                }

                matchingPixels += regionMatchingPixels;
                regionResults.Add(new FramebufferPixelRegionResult(
                    region.Id,
                    region.X,
                    region.Y,
                    region.Width,
                    region.Height,
                    regionMatchingPixels,
                    regionPassed));
            }

            bool requirementPassed = passingRegions >= requirement.MinimumPassingRegions;
            requirementResults.Add(new FramebufferPixelRequirementResult(
                requirement.Role,
                requirement.PresentationTemplate,
                requirement.MinimumPixelsPerRegion,
                requirement.MinimumPassingRegions,
                passingRegions,
                matchingPixels,
                requirementPassed,
                regionResults));
        }

        return new FramebufferPixelInspectionResult(
            SupportedSchemaVersion,
            imagePath,
            bitmap.Width,
            bitmap.Height,
            requirementResults.TrueForAll(result => result.Passed),
            requirementResults);
    }

    private static void ValidateRequirement(FramebufferPixelRequirement requirement, int imageWidth, int imageHeight)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        RequireText(requirement.Role, "Requirement.Role");
        RequireText(requirement.PresentationTemplate, "Requirement.PresentationTemplate");
        if (requirement.MaximumChannelDifference is < 0 or > 255)
        {
            throw new InvalidOperationException(
                $"Framebuffer role '{requirement.Role}' MaximumChannelDifference must be between 0 and 255.");
        }

        if (requirement.MinimumPixelsPerRegion <= 0 || requirement.MinimumPassingRegions <= 0)
        {
            throw new InvalidOperationException(
                $"Framebuffer role '{requirement.Role}' pixel and passing-region minimums must be positive.");
        }

        if (requirement.AcceptedColors == null || requirement.AcceptedColors.Count == 0)
        {
            throw new InvalidOperationException($"Framebuffer role '{requirement.Role}' requires at least one accepted color.");
        }

        if (requirement.Regions == null || requirement.Regions.Count == 0)
        {
            throw new InvalidOperationException($"Framebuffer role '{requirement.Role}' requires at least one search region.");
        }

        if (requirement.MinimumPassingRegions > requirement.Regions.Count)
        {
            throw new InvalidOperationException(
                $"Framebuffer role '{requirement.Role}' requires {requirement.MinimumPassingRegions} passing regions but only {requirement.Regions.Count} were supplied.");
        }

        var regionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (FramebufferPixelRegion region in requirement.Regions)
        {
            string regionId = RequireText(region.Id, $"Framebuffer role '{requirement.Role}' region id");
            if (!regionIds.Add(regionId))
            {
                throw new InvalidOperationException($"Framebuffer role '{requirement.Role}' duplicates region '{regionId}'.");
            }

            if (region.X < 0 || region.Y < 0 || region.Width <= 0 || region.Height <= 0 ||
                region.X > imageWidth - region.Width || region.Y > imageHeight - region.Height)
            {
                throw new InvalidOperationException(
                    $"Framebuffer role '{requirement.Role}' region '{regionId}' is outside the {imageWidth}x{imageHeight} image.");
            }
        }
    }

    private static int CountMatchingPixels(
        IReadOnlyList<SKColor> pixels,
        int imageWidth,
        FramebufferPixelRegion region,
        IReadOnlyList<FramebufferPixelColor> acceptedColors,
        int maximumChannelDifference)
    {
        int count = 0;
        int bottom = region.Y + region.Height;
        int right = region.X + region.Width;
        for (int y = region.Y; y < bottom; y++)
        {
            int rowOffset = y * imageWidth;
            for (int x = region.X; x < right; x++)
            {
                SKColor actual = pixels[rowOffset + x];
                for (int colorIndex = 0; colorIndex < acceptedColors.Count; colorIndex++)
                {
                    FramebufferPixelColor expected = acceptedColors[colorIndex];
                    if (Math.Abs(actual.Red - expected.Red) <= maximumChannelDifference &&
                        Math.Abs(actual.Green - expected.Green) <= maximumChannelDifference &&
                        Math.Abs(actual.Blue - expected.Blue) <= maximumChannelDifference)
                    {
                        count++;
                        break;
                    }
                }
            }
        }

        return count;
    }

    private static string RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{name} must be non-empty.");
        }

        return value;
    }
}
