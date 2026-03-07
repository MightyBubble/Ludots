namespace Ludots.UI.Runtime;

public enum UiNodeKind : byte
{
    Container = 0,
    Text = 1,
    Button = 2,
    Image = 3,
    Panel = 4,
    Row = 5,
    Column = 6,
    Input = 7,
    Checkbox = 8,
    Toggle = 9,
    Slider = 10,
    Select = 11,
    TextArea = 12,
    ScrollView = 13,
    List = 14,
    Card = 15,
    Custom = 255
}
