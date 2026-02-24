using System.Windows;

namespace Ludots.ModLauncher
{
    public partial class TextPromptWindow : Window
    {
        public string PromptTitle { get; }
        public string PromptLabel { get; }
        public string? Value { get; private set; }

        public TextPromptWindow(string title, string label)
        {
            InitializeComponent();
            PromptTitle = title;
            PromptLabel = label;
            Title = title;
            DataContext = this;
            Loaded += (_, _) =>
            {
                InputBox.Focus();
                InputBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

