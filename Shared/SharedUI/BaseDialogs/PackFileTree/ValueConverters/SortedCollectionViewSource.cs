using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows.Data;

namespace Shared.Ui.BaseDialogs.PackFileTree.ValueConverters
{
    public class SortedCollectionViewSource : IValueConverter
    {
        public string Property0 { get; set; } = string.Empty;
        public string Property1 { get; set; } = string.Empty;

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            var s = CollectionViewSource.GetDefaultView(value);
            if (HasExpectedSortDescriptions(s))
                return s;

            using (s.DeferRefresh())
            {
                s.SortDescriptions.Clear();
                s.SortDescriptions.Add(new SortDescription(
                    Property0,
                    ListSortDirection.Ascending));
                s.SortDescriptions.Add(new SortDescription(
                    Property1,
                    ListSortDirection.Ascending));
            }
            return s;
        }

        private bool HasExpectedSortDescriptions(ICollectionView view) =>
            view.SortDescriptions.Count == 2 &&
            view.SortDescriptions[0].PropertyName == Property0 &&
            view.SortDescriptions[0].Direction == ListSortDirection.Ascending &&
            view.SortDescriptions[1].PropertyName == Property1 &&
            view.SortDescriptions[1].Direction == ListSortDirection.Ascending;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
