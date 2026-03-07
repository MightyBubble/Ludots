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
    Radio = 9,
    Toggle = 10,
    Slider = 11,
    Select = 12,
    TextArea = 13,
    ScrollView = 14,
    List = 15,
    Card = 16,
    Table = 17,
    TableHeader = 18,
    TableBody = 19,
    TableFooter = 20,
    TableRow = 21,
    TableCell = 22,
    TableHeaderCell = 23,
    Custom = 255
}
