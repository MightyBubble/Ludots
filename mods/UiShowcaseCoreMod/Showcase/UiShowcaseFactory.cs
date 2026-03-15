using Ludots.UI.Compose;
using Ludots.UI.Reactive;
using Ludots.UI.Runtime;

namespace UiShowcaseCoreMod.Showcase;

public static class UiShowcaseFactory
{
    public static UiScene CreateHubScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        UiElementBuilder root = Ui.Column(
                Ui.Text("Ludots Unified UI Showcase").Class("skin-header").FontSize(34).Bold(),
                Ui.Text("FeatureHub 负责顶层入口；Hub 只做薄导航，三种官方写法和换肤演示分别独立成 Mod。")
                    .Class("page-copy"),
                Ui.Row(
                    UiShowcaseScaffolding.BuildHubCard("hub-compose", "Compose Fluent", "默认生产主路径", "HUD、菜单、背包、性能敏感界面"),
                    UiShowcaseScaffolding.BuildHubCard("hub-reactive", "Reactive Fluent", "状态驱动主路径", "工具面板、复杂列表、编辑器"),
                    UiShowcaseScaffolding.BuildHubCard("hub-markup", "Markup + CodeBehind", "原型导入主路径", "内容页、帮助页、剧情页"))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1),
                Ui.Row(
                    UiShowcaseScaffolding.BuildHubCard("hub-skin", "Same DOM, Different Skin", "皮肤 Mod 只改主题与资源，不改 DOM 语义", "Classic / Sci-Fi HUD / Paper"),
                    Ui.Card(
                        Ui.Text("Official entry hints").Class("page-card-title"),
                        Ui.Text("FeatureHub: U=Hub, I=Compose, O=Reactive, P=Markup, [=Skin Swap").Class("page-copy"),
                        Ui.Text("Theme / Density / Controls / Forms / Collections / Overlays / Styles 全部在独立 Showcase 中可见。").Class("page-copy"),
                        Ui.Text("Web host 优先接收 SceneDiff，HTML 仅作为兼容桥。").Class("muted"))
                        .Class("skin-card")
                        .FlexGrow(1),
                    Ui.Card(
                        Ui.Text("CSS Profile positioning").Class("page-card-title"),
                        Ui.Text("应用级 Native CSS Profile：selector / cascade / inheritance / variables / theme token。").Class("page-copy"),
                        Ui.Text("不承诺浏览器级 CSSOM / JS / Grid / Animation / 浏览器怪癖兼容。").Class("muted"))
                        .Class("skin-card")
                        .FlexGrow(1))
                    .Class("page-grid-row")
                    .Gap(12)
                    .FlexGrow(1))
            .Classes("skin-root", "theme-dark", "density-cozy")
            .Width(1280)
            .Height(720)
            .Gap(12);

        return UiSceneComposer.Compose(textMeasurer, imageSizeProvider, root, null, UiShowcaseScaffolding.AuthoringStyleSheet);
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

    public static UiScene CreateSkinShowcaseScene(IUiTextMeasurer textMeasurer, IUiImageSizeProvider imageSizeProvider)
    {
        return UiSkinShowcaseSceneFactory.CreateScene(textMeasurer, imageSizeProvider);
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
