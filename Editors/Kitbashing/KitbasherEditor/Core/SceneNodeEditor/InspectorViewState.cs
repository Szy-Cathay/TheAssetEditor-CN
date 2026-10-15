using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace KitbasherEditor.Views
{
    public sealed class InspectorTabs : TabControl
    {
        protected override Size MeasureOverride(Size constraint)
        {
            var desiredSize = base.MeasureOverride(constraint);
            return InspectorViewState.PrepareSelection(this) ? base.MeasureOverride(constraint) : desiredSize;
        }
    }

    public sealed class InspectorViewState
    {
        private readonly Dictionary<string, string> _selectedCategories = [];

        public static readonly DependencyProperty StateProperty = DependencyProperty.RegisterAttached(
            "State", typeof(InspectorViewState), typeof(InspectorViewState),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.Inherits, OnStateChanged));

        public static readonly DependencyProperty KeyProperty = DependencyProperty.RegisterAttached(
            "Key", typeof(string), typeof(InspectorViewState), new PropertyMetadata(null, OnKeyChanged));

        private static readonly DependencyProperty ReadyProperty = DependencyProperty.RegisterAttached(
            "Ready", typeof(bool), typeof(InspectorViewState), new PropertyMetadata(false));

        public static InspectorViewState? GetState(DependencyObject element) => (InspectorViewState?)element.GetValue(StateProperty);
        public static void SetState(DependencyObject element, InspectorViewState? value) => element.SetValue(StateProperty, value);
        public static string? GetKey(DependencyObject element) => (string?)element.GetValue(KeyProperty);
        public static void SetKey(DependencyObject element, string? value) => element.SetValue(KeyProperty, value);

        private static void OnStateChanged(DependencyObject element, DependencyPropertyChangedEventArgs args) => Restore(element);

        private static void OnKeyChanged(DependencyObject element, DependencyPropertyChangedEventArgs args)
        {
            if (element is TabControl categories)
            {
                categories.SelectionChanged -= OnCategoryChanged;
                categories.DataContextChanged -= OnCategoryContextChanged;
                if (args.NewValue != null)
                {
                    categories.SelectionChanged += OnCategoryChanged;
                    categories.DataContextChanged += OnCategoryContextChanged;
                }
            }
            Restore(element);
        }

        private static void Restore(DependencyObject element)
        {
            var state = GetState(element);
            var key = GetKey(element);
            if (state == null || key == null)
                return;

            if (element is TabControl categories)
            {
                categories.SetValue(ReadyProperty, false);
                categories.InvalidateMeasure();
            }
        }

        internal static bool PrepareSelection(TabControl categories)
        {
            if (categories.DataContext == null ||
                GetState(categories) is not { } state || GetKey(categories) is not { } key)
                return false;
            if ((bool)categories.GetValue(ReadyProperty) &&
                categories.SelectedItem is TabItem { Visibility: Visibility.Visible, IsEnabled: true })
                return false;

            // Measuring attaches child bindings; resolve selection before arrange and the first rendered frame.
            categories.SetValue(ReadyProperty, false);
            foreach (var item in categories.Items.OfType<TabItem>())
            {
                BindingOperations.GetBindingExpression(item, UIElement.VisibilityProperty)?.UpdateTarget();
                BindingOperations.GetBindingExpression(item, UIElement.IsEnabledProperty)?.UpdateTarget();
            }
            var available = categories.Items.OfType<TabItem>()
                .Where(item => item.Visibility == Visibility.Visible && item.IsEnabled).ToList();
            var preferred = state._selectedCategories.GetValueOrDefault(key);
            var selected = available.FirstOrDefault(item => Equals(item.Tag, preferred)) ?? available.FirstOrDefault();
            var changed = !ReferenceEquals(categories.SelectedItem, selected);
            categories.SetCurrentValue(TabControl.SelectedItemProperty, selected);
            categories.SetValue(ReadyProperty, true);
            return changed;
        }

        private static void OnCategoryChanged(object sender, SelectionChangedEventArgs args)
        {
            if (sender is TabControl categories && ReferenceEquals(args.OriginalSource, categories) &&
                (bool)categories.GetValue(ReadyProperty) &&
                GetState(categories) is { } state && GetKey(categories) is { } key &&
                categories.SelectedItem is TabItem { Visibility: Visibility.Visible, Tag: string category })
                state._selectedCategories[key] = category;
        }

        private static void OnCategoryContextChanged(object sender, DependencyPropertyChangedEventArgs args) => Restore((DependencyObject)sender);
    }
}
