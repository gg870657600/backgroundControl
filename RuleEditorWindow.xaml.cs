using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;

namespace backgroundControl
{
    public partial class RuleEditorWindow : Window
    {
        public ObservableCollection<RuleItem> Rules { get; set; }
        private ICollectionView _filteredView;

        public RuleEditorWindow()
        {
            InitializeComponent();
            Rules = new ObservableCollection<RuleItem>();
            InitFilter();
        }

        public RuleEditorWindow(ObservableCollection<RuleItem> rules)
        {
            InitializeComponent();
            Rules = new ObservableCollection<RuleItem>(rules);
            InitFilter();
        }

        private void InitFilter()
        {
            _filteredView = CollectionViewSource.GetDefaultView(Rules);
            _filteredView.Filter = FilterRule;
            RulesGrid.ItemsSource = _filteredView;
        }

        private bool FilterRule(object obj)
        {
            if (obj is not RuleItem rule) return false;
            if (string.IsNullOrWhiteSpace(SearchBox.Text) || SearchBox.Text == "搜索关键词或命令...") return true;
            var filter = SearchBox.Text.ToLowerInvariant();
            return rule.Keywords?.ToLowerInvariant().Contains(filter) == true
                || rule.Command?.ToLowerInvariant().Contains(filter) == true;
        }

        private void SearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _filteredView.Refresh();
        }

        private RuleItem CurrentRuleItem(object sender)
        {
            return (sender as FrameworkElement)?.DataContext as RuleItem;
        }

        private void CopyRow_Click(object sender, RoutedEventArgs e)
        {
            var item = CurrentRuleItem(sender);
            if (item == null) return;
            var idx = Rules.IndexOf(item);
            var copy = new RuleItem { Keywords = item.Keywords, Command = item.Command };
            Rules.Insert(idx + 1, copy);
        }

        private void InsertRow_Click(object sender, RoutedEventArgs e)
        {
            var item = CurrentRuleItem(sender);
            if (item == null) return;
            var idx = Rules.IndexOf(item);
            Rules.Insert(idx + 1, new RuleItem());
        }

        private void DeleteRow_Click(object sender, RoutedEventArgs e)
        {
            var item = CurrentRuleItem(sender);
            if (item == null) return;
            Rules.Remove(item);
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            var sorted = Rules.OrderBy(r => r.Command).ToList();
            Rules.Clear();
            foreach (var r in sorted) Rules.Add(r);
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
