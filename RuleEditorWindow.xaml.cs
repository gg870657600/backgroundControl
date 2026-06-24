using System.Collections.ObjectModel;
using System.Windows;

namespace backgroundControl
{
    public partial class RuleEditorWindow : Window
    {
        public ObservableCollection<RuleItem> Rules { get; set; }

        // 无参构造函数（供 XAML 设计器使用）
        public RuleEditorWindow()
        {
            InitializeComponent();
            Rules = new ObservableCollection<RuleItem>();
            RulesGrid.ItemsSource = Rules;
        }

        // 有参构造函数（供主程序调用）
        public RuleEditorWindow(ObservableCollection<RuleItem> rules)
        {
            InitializeComponent();
            Rules = new ObservableCollection<RuleItem>(rules);
            RulesGrid.ItemsSource = Rules;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}