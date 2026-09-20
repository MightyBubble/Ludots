using System;
using System.Collections.Generic;

namespace EntityInfoPanelsMod;

public sealed class EntityInfoPanelTemplateDescriptor
{
    public string Id { get; init; } = string.Empty;
    public EntityInfoPanelTemplateBindingKind BindingKind { get; init; } = EntityInfoPanelTemplateBindingKind.TargetEntity;
    public EntityInfoPanelTemplateLayoutMode LayoutMode { get; init; } = EntityInfoPanelTemplateLayoutMode.Compact;
    public EntityInfoPanelTemplateSectionFlags Sections { get; init; } =
        EntityInfoPanelTemplateSectionFlags.Title |
        EntityInfoPanelTemplateSectionFlags.Subtitle |
        EntityInfoPanelTemplateSectionFlags.Stats;
    public string HeaderTokenKey { get; init; } = string.Empty;
    public string EmptyTokenKey { get; init; } = string.Empty;
    public bool RequireInsightProfile { get; init; }
}

public interface IEntityInfoPanelTemplateCatalog
{
    void Register(EntityInfoPanelTemplateDescriptor descriptor);
    bool TryGet(string templateId, out EntityInfoPanelTemplateDescriptor descriptor);
}

public sealed class EntityInfoPanelTemplateCatalog : IEntityInfoPanelTemplateCatalog
{
    public const string DefaultTemplateId = "entityinfo.template.default";
    public const string CompactInsightTemplateId = "entityinfo.template.compact-insight";

    private readonly Dictionary<string, EntityInfoPanelTemplateDescriptor> _templates = new(StringComparer.Ordinal);

    public EntityInfoPanelTemplateCatalog()
    {
        Register(new EntityInfoPanelTemplateDescriptor
        {
            Id = DefaultTemplateId,
            BindingKind = EntityInfoPanelTemplateBindingKind.TargetEntity,
            LayoutMode = EntityInfoPanelTemplateLayoutMode.Compact,
            Sections = EntityInfoPanelTemplateSectionFlags.Title | EntityInfoPanelTemplateSectionFlags.Subtitle | EntityInfoPanelTemplateSectionFlags.Stats,
            RequireInsightProfile = false
        });
        Register(new EntityInfoPanelTemplateDescriptor
        {
            Id = CompactInsightTemplateId,
            BindingKind = EntityInfoPanelTemplateBindingKind.TargetEntity,
            LayoutMode = EntityInfoPanelTemplateLayoutMode.Compact,
            Sections = EntityInfoPanelTemplateSectionFlags.All,
            RequireInsightProfile = true
        });
    }

    public void Register(EntityInfoPanelTemplateDescriptor descriptor)
    {
        if (descriptor == null)
        {
            throw new ArgumentNullException(nameof(descriptor));
        }

        string id = RequireId(descriptor.Id);
        _templates[id] = new EntityInfoPanelTemplateDescriptor
        {
            Id = id,
            BindingKind = descriptor.BindingKind,
            LayoutMode = descriptor.LayoutMode,
            Sections = descriptor.Sections == EntityInfoPanelTemplateSectionFlags.None
                ? EntityInfoPanelTemplateSectionFlags.Title
                : descriptor.Sections,
            HeaderTokenKey = descriptor.HeaderTokenKey?.Trim() ?? string.Empty,
            EmptyTokenKey = descriptor.EmptyTokenKey?.Trim() ?? string.Empty,
            RequireInsightProfile = descriptor.RequireInsightProfile
        };
    }

    public bool TryGet(string templateId, out EntityInfoPanelTemplateDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(templateId) &&
            _templates.TryGetValue(templateId.Trim(), out descriptor!))
        {
            return true;
        }

        descriptor = null!;
        return false;
    }

    private static string RequireId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            throw new ArgumentException("Entity info panel template id is required.");
        }

        return id.Trim();
    }
}
