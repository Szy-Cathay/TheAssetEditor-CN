using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Data;
using CommunityToolkit.Diagnostics;
using Shared.Core.Misc;
using Shared.Core.Services;
using WindowHandling;

namespace Editors.AnimationMeta.Presentation.View
{
    public partial class NewMetaDataEntryWindow : AssetEditorWindow
    {
        public NewMetaDataEntryWindow()
        {
            InitializeComponent();
        }

        private void HandleOnClick()
        {
            var model = DataContext as NewTagWindowViewModel;
            Guard.IsNotNull(model, $"{nameof(model)} - DataContext must be of type {nameof(NewTagWindowViewModel)}");

            if (model.SelectedItem == null)
            {
                MessageBox.Show(LocalizationManager.Instance.Get("Msg.NothingSelected"));
                return;
            }

            DialogResult = true;
            Close();
        }

        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            var model = DataContext as NewTagWindowViewModel;
            Guard.IsNotNull(model, $"{nameof(model)} - DataContext must be of type {nameof(NewTagWindowViewModel)}");
            if (model.SelectedItem != null)
                HandleOnClick();
        }
    }

    public sealed record NewTagWindowItem(
        string Name,
        string Description,
        string Category = "其他",
        int CategoryOrder = int.MaxValue);

    public sealed class NewTagWindowViewModel : NotifyPropertyChangedImpl
    {
        private readonly List<NewTagWindowItem> _allItems = [];
        private string _searchText = "";
        private NewTagWindowItem? _selectedItem;

        public ObservableCollection<NewTagWindowItem> Items { get; } = [];
        public ListCollectionView GroupedItems { get; }

        public NewTagWindowViewModel()
        {
            GroupedItems = (ListCollectionView)
                CollectionViewSource.GetDefaultView(Items);
            GroupedItems.GroupDescriptions.Add(
                new PropertyGroupDescription(nameof(NewTagWindowItem.Category)));
        }
        public string SearchText
        {
            get => _searchText;
            set => SetAndNotifyWhenChanged(
                ref _searchText,
                value,
                _ => ApplyFilter());
        }
        public NewTagWindowItem? SelectedItem
        {
            get => _selectedItem;
            set => SetAndNotifyWhenChanged(ref _selectedItem, value);
        }

        public void SetItems(IEnumerable<NewTagWindowItem> items)
        {
            _allItems.Clear();
            _allItems.AddRange(items);
            ApplyFilter();
        }

        private void ApplyFilter()
        {
            var filtered = _allItems.Where(item =>
                IsFuzzyMatch(_searchText, item));
            Items.Clear();
            foreach (var item in filtered)
                Items.Add(item);

            if (SelectedItem != null && !Items.Contains(SelectedItem))
                SelectedItem = null;
        }

        private static bool IsFuzzyMatch(
            string query,
            NewTagWindowItem item)
        {
            if (string.IsNullOrWhiteSpace(query))
                return true;

            var searchable = Normalize(item.Name + item.Description);
            return query.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries)
                .Select(Normalize)
                .All(term =>
                    searchable.Contains(term, StringComparison.Ordinal) ||
                    IsSubsequence(term, searchable));
        }

        private static string Normalize(string value) =>
            string.Concat(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant));

        private static bool IsSubsequence(
            string search,
            string candidate)
        {
            var searchIndex = 0;
            foreach (var character in candidate)
            {
                if (searchIndex < search.Length &&
                    search[searchIndex] == character)
                {
                    searchIndex++;
                }
            }

            return searchIndex == search.Length;
        }
    }
}
