using System;
using Ludots.UI.Compose;
using Ludots.UI.HtmlEngine.Markup;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;
using Ludots.UI.Surface;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseFactory
{
    public static UiScene CreateHubScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, BuildHubRoot(), null, UiShowcaseScaffolding.AuthoringStyleSheet);
    }

    public static UiSurfaceContribution CreateHubContribution()
    {
        return UiSurfaceContribution.FromBuilder(
            BuildHubRoot,
            styleSheets: new[] { UiShowcaseScaffolding.AuthoringStyleSheet });
    }

    public static UiElementBuilder BuildHubRoot()
    {
        return Ui.Column(
                Ui.Text("Ludots Unified UI Showcase").Class("skin-header").FontSize(34).Bold(),
                Ui.Text("三种官方写法同属 UiShowcaseCoreMod：Compose≈Flutter Fluent、Reactive≈React 状态驱动、Markup=HTML/CSS+CodeBehind；另有换肤 / 水墨匣 / 星港同稿独立 Showcase。")
                    .Class("page-copy"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildHubCard("hub-compose", "Compose Fluent", "Flutter 式生产主路径", "HUD、菜单、背包、性能敏感界面"),
                    UiShowcaseScaffolding.BuildHubCard("hub-reactive", "Reactive Fluent", "React 式状态驱动", "工具面板、复杂列表、编辑器"),
                    UiShowcaseScaffolding.BuildHubCard("hub-markup", "Markup + CodeBehind", "HTML/CSS 原型导入", "内容页、帮助页、剧情页"))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1),
                Ui.Row(
                    UiShowcaseScaffolding.BuildHubCard("hub-skin", "Same DOM, Different Skin", "皮肤 Mod 只改主题与资源，不改 DOM 语义", "Classic / Sci-Fi HUD / Paper"),
                    Ui.Card(
                        Ui.Text("Official entry hints").Class("page-card-title"),
                        Ui.Text("FeatureHub: U=Hub, I=Compose, O=Reactive, P=Markup, [=Skin Swap").Class("page-copy"),
                        Ui.Text("Appearance 里 Phase 1–6：选择器、视觉、文本、图像、关键帧、Grid auto/sticky/伪元素图标。").Class("page-copy"),
                        Ui.Text("Web host 优先接收 SceneDiff，HTML 仅作为兼容桥。").Class("muted"))
                        .Class("skin-card")
                        .FlexGrow(1),
                    Ui.Card(
                        Ui.Text("CSS Profile positioning").Class("page-card-title"),
                        Ui.Text("Native CSS：selector / cascade / flex / grid(auto·minmax) / calc / viewport / sticky / transform 动效。").Class("page-copy"),
                        Ui.Text("浏览器同稿对照见「同一张暂停菜单」；切铺动效见「墨痕怎么裁、怎么铺」。不做 JS / 全量 CSSOM。").Class("muted"))
                        .Class("skin-card")
                        .FlexGrow(1))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1))
            .Classes("skin-root", "theme-dark", "density-cozy")
            .Width(1280)
            .Height(720)
            .Gap(12);
    }

    public static UiScene CreateComposeScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return new ComposeShowcaseController(textMeasurer, imageSizeProvider).BuildScene();
    }

    public static ReactivePage<ReactiveShowcaseState> CreateReactivePage(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return ReactiveShowcasePageFactory.CreatePage(textMeasurer, imageSizeProvider);
    }

    public static UiScene CreateMarkupScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return new MarkupShowcaseCodeBehind(textMeasurer, imageSizeProvider).BuildScene();
    }

    public static UiScene CreateNineSlicePanelScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return new NineSlicePanelShowcaseCodeBehind(textMeasurer, imageSizeProvider).BuildScene();
    }

    public static UiSurfaceContribution CreateNineSlicePanelContribution(
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider,
        Action requestRebuild)
    {
        return new NineSlicePanelShowcaseCodeBehind(textMeasurer, imageSizeProvider).CreateContribution(requestRebuild);
    }

    public static UiScene CreateWebParityFixtureScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return new UiMarkupLoader().LoadScene(
            textMeasurer,
            imageSizeProvider,
            UiShowcaseAssets.GetParityMenuHtml(),
            UiShowcaseAssets.GetParityMenuCss());
    }

    public static UiScene CreateWebParityShowcaseScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return new WebParityShowcaseCodeBehind(textMeasurer, imageSizeProvider).BuildScene();
    }

    public static UiSurfaceContribution CreateWebParityContribution(
        IUiTextMeasurer textMeasurer,
        IUiImageSizeProvider imageSizeProvider,
        Action requestRebuild)
    {
        return new WebParityShowcaseCodeBehind(textMeasurer, imageSizeProvider).CreateContribution(requestRebuild);
    }
    public static UiScene CreateSkinShowcaseScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return UiSkinShowcaseSceneFactory.CreateScene(textMeasurer, imageSizeProvider);
    }

    public static UiSurfaceContribution CreateSkinShowcaseContribution(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return UiSkinShowcaseSceneFactory.CreateContribution(textMeasurer, imageSizeProvider);
    }

    public static UiScene CreateSkinFixtureScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider, UiThemePack theme)
    {
        return UiSkinShowcaseSceneFactory.CreateFixtureScene(textMeasurer, imageSizeProvider, theme);
    }

    public static IReadOnlyList<UiThemePack> GetSkinThemes()
    {
        return new[] { UiSkinThemes.Classic, UiSkinThemes.SciFiHud, UiSkinThemes.Paper };
    }
}
