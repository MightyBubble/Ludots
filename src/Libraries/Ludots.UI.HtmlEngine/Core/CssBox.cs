using AngleSharp.Dom;
using ExCSS;
using FlexNode = FlexLayoutSharp.Node;
using Flex = FlexLayoutSharp.Flex;

namespace Ludots.UI.HtmlEngine.Core
{
    public class CssBox
    {
        private static readonly StylesheetParser _sharedParser = new StylesheetParser();

        public IElement Element { get; set; }
        public StyleDeclaration ComputedStyle { get; set; }
        public FlexNode FlexNode { get; set; }
        public List<CssBox> Children { get; } = new List<CssBox>();
        
        public CssBox(IElement element)
        {
            Element = element;
            ComputedStyle = new StyleDeclaration(_sharedParser);
            FlexNode = Flex.CreateDefaultNode();
            // Store reference to this box in the context for retrieval during layout/rendering
            FlexNode.config = Flex.CreateDefaultConfig();
            FlexNode.config.Context = this; 
        }
    }
}
