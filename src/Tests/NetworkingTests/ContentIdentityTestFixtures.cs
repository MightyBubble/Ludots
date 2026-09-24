using System.Collections.Generic;
using System.Text;
using Ludots.Core.Networking.Session;

namespace Ludots.Tests.Networking;

internal static class ContentIdentityTestFixtures
{
    public static ContentIdentityManifest CreateManifest(string seed)
    {
        var items = new List<ContentIdentityItem>(ContentIdentityManifest.CategoryCount);
        for (int i = 0; i < ContentIdentityManifest.CategoryCount; i++)
        {
            ContentIdentityCategory category = ContentIdentityManifest.CategoryAt(i);
            items.Add(new ContentIdentityItem(
                category,
                "seed",
                ContentFingerprintBuilder.FromCanonicalBytes(Encoding.UTF8.GetBytes($"{seed}:{i}"))));
        }

        return ContentIdentityManifest.Create(items);
    }

    public static ContentCategoryDigestTable CategoryDigests(ContentIdentityManifest manifest) =>
        new(manifest.CategoryDigests);
}
