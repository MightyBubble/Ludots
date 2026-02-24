using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ExCSS;
using System.Collections.Generic;
using System.Linq;

namespace Ludots.UI.HtmlEngine.Core
{
    public class HtmlParserService
    {
        private readonly HtmlParser _htmlParser;
        private readonly StylesheetParser _cssParser;

        public HtmlParserService()
        {
            _htmlParser = new HtmlParser();
            _cssParser = new StylesheetParser();
        }

        public IDocument ParseHtml(string html)
        {
            return _htmlParser.ParseDocument(html);
        }

        public Stylesheet ParseCss(string css)
        {
            return _cssParser.Parse(css);
        }

        // Simple style matching logic
        public void ApplyStyles(CssBox rootBox, Stylesheet stylesheet)
        {
            ApplyStylesRecursive(rootBox, stylesheet);
        }

        private void ApplyStylesRecursive(CssBox box, Stylesheet stylesheet)
        {
            if (box.Element == null) return;

            foreach (var rule in stylesheet.StyleRules)
            {
                if (MatchesSelector(box.Element, rule.Selector))
                {
                    // Merge styles
                    foreach (var prop in rule.Style)
                    {
                        // A very naive merge - last one wins
                        // In reality, we need specificity calculation
                        // box.ComputedStyle.RemoveProperty(prop.Name);
                        box.ComputedStyle.SetProperty(prop.Name, prop.Value);
                    }
                }
            }

            foreach (var child in box.Children)
            {
                ApplyStylesRecursive(child, stylesheet);
            }
        }

        private bool MatchesSelector(IElement element, ISelector selector)
        {
            // ExCSS selectors are complex objects. 
            // We need to implement a visitor or basic matching logic.
            // For this simplified engine, we'll convert selector to string and use basic checks
            // Or try to map ExCSS selector logic to AngleSharp elements.
            
            // AngleSharp has its own CSS selector engine. 
            // Ideally, we would use AngleSharp.Css, but we are using ExCSS as requested.
            // So we have to bridge them manually.
            
            // Simple implementation for demo: Class, ID, Tag
            var selectorText = selector.Text;
            
            if (selectorText.StartsWith("."))
            {
                return element.ClassList.Contains(selectorText.Substring(1));
            }
            else if (selectorText.StartsWith("#"))
            {
                return element.Id == selectorText.Substring(1);
            }
            else
            {
                return element.LocalName.Equals(selectorText, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
