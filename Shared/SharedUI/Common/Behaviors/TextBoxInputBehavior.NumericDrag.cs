using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Shared.Ui.Common.Behaviors
{
    public partial class TextBoxInputBehavior
    {
        public static readonly RoutedEvent NumericDragStartedEvent = EventManager.RegisterRoutedEvent(
            "NumericDragStarted", RoutingStrategy.Bubble, typeof(RoutedEventHandler), typeof(TextBoxInputBehavior));
        public static readonly RoutedEvent NumericDragCompletedEvent = EventManager.RegisterRoutedEvent(
            "NumericDragCompleted", RoutingStrategy.Bubble, typeof(EventHandler<NumericDragCompletedEventArgs>), typeof(TextBoxInputBehavior));
        public static readonly DependencyProperty DragStepProperty = DependencyProperty.Register(
            nameof(DragStep), typeof(double), typeof(TextBoxInputBehavior), new PropertyMetadata(0.01));

        public bool IsDragEnabled { get; set; }
        public double DragStep { get => (double)GetValue(DragStepProperty); set => SetValue(DragStepProperty, value); }
        public double DragMinimum { get; set; } = -float.MaxValue;
        public double DragMaximum { get; set; } = float.MaxValue;

        private Point _dragOrigin;
        private double _dragLastX;
        private bool _dragCandidate;
        private bool _numericDragActive;
        private string _dragOriginalText = string.Empty;
        private double _dragValue;
        private double _dragRemainder;
        private Cursor? _dragOldCursor;

        private void AttachNumericDrag()
        {
            AssociatedObject.PreviewMouseLeftButtonDown += NumericMouseDown;
            AssociatedObject.PreviewMouseMove += NumericMouseMove;
            AssociatedObject.PreviewMouseLeftButtonUp += NumericMouseUp;
            AssociatedObject.PreviewMouseRightButtonDown += NumericCancelMouse;
            AssociatedObject.PreviewKeyDown += NumericKeyDown;
            AssociatedObject.LostMouseCapture += NumericLostCapture;
            AssociatedObject.LostKeyboardFocus += NumericLostFocus;
            AssociatedObject.IsEnabledChanged += NumericEnabledChanged;
            AssociatedObject.DataContextChanged += NumericContextChanged;
            AssociatedObject.Unloaded += NumericUnloaded;
        }

        private void DetachNumericDrag()
        {
            FinishNumericDrag(true);
            AssociatedObject.PreviewMouseLeftButtonDown -= NumericMouseDown;
            AssociatedObject.PreviewMouseMove -= NumericMouseMove;
            AssociatedObject.PreviewMouseLeftButtonUp -= NumericMouseUp;
            AssociatedObject.PreviewMouseRightButtonDown -= NumericCancelMouse;
            AssociatedObject.PreviewKeyDown -= NumericKeyDown;
            AssociatedObject.LostMouseCapture -= NumericLostCapture;
            AssociatedObject.LostKeyboardFocus -= NumericLostFocus;
            AssociatedObject.IsEnabledChanged -= NumericEnabledChanged;
            AssociatedObject.DataContextChanged -= NumericContextChanged;
            AssociatedObject.Unloaded -= NumericUnloaded;
        }

        private void NumericMouseDown(object sender, MouseButtonEventArgs e)
        {
            _dragCandidate = IsDragEnabled && InputMode == TextBoxInputMode.DecimalInput &&
                AssociatedObject.IsEnabled && !AssociatedObject.IsReadOnly && e.ClickCount == 1;
            _dragOrigin = e.GetPosition(AssociatedObject);
            _dragLastX = _dragOrigin.X;
        }

        private void NumericMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragCandidate && !_numericDragActive)
                return;
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                FinishNumericDrag(true);
                return;
            }
            var position = e.GetPosition(AssociatedObject);
            if (!_numericDragActive)
            {
                var horizontal = Math.Abs(position.X - _dragOrigin.X);
                var vertical = Math.Abs(position.Y - _dragOrigin.Y);
                if (vertical > 6 && vertical > horizontal)
                    _dragCandidate = false;
                if (horizontal < 6 || horizontal <= vertical || !_dragCandidate)
                    return;
                if (!BeginNumericDrag())
                    return;
                if (!AssociatedObject.CaptureMouse())
                {
                    FinishNumericDrag(true);
                    return;
                }
                _dragLastX = position.X;
            }
            ApplyNumericDrag(position.X - _dragLastX, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));
            _dragLastX = position.X;
            e.Handled = true;
        }

        internal bool BeginNumericDrag()
        {
            _dragCandidate = false;
            if (!IsDragEnabled || InputMode != TextBoxInputMode.DecimalInput || AssociatedObject.IsReadOnly || !AssociatedObject.IsEnabled ||
                !double.IsFinite(DragStep) || DragStep <= 0 || !double.IsFinite(DragMinimum) || !double.IsFinite(DragMaximum) ||
                (JustPositivDecimalInput ? Math.Max(0, DragMinimum) : DragMinimum) > DragMaximum ||
                !double.TryParse(AssociatedObject.Text.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out _dragValue) || !double.IsFinite(_dragValue))
                return false;
            _dragOriginalText = AssociatedObject.Text;
            _dragRemainder = 0;
            _dragOldCursor = AssociatedObject.Cursor;
            _numericDragActive = true;
            AssociatedObject.Cursor = Cursors.SizeWE;
            AssociatedObject.RaiseEvent(new RoutedEventArgs(NumericDragStartedEvent, AssociatedObject));
            return true;
        }

        internal void ApplyNumericDrag(double pixels, bool precise)
        {
            if (!_numericDragActive || !double.IsFinite(pixels))
                return;
            var step = DragStep * (precise ? 0.1 : 1);
            if (!double.IsFinite(pixels * step + _dragRemainder))
                return;
            _dragRemainder += pixels * step;
            var increment = Math.Truncate(_dragRemainder / step) * step;
            _dragRemainder -= increment;
            var minimum = JustPositivDecimalInput ? Math.Max(0, DragMinimum) : DragMinimum;
            _dragValue = Math.Clamp(_dragValue + increment, minimum, DragMaximum);
            AssociatedObject.SetCurrentValue(TextBox.TextProperty, _dragValue.ToString("0.######", CultureInfo.InvariantCulture));
            AssociatedObject.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            if (double.TryParse(AssociatedObject.Text.Replace(',', '.'), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out var accepted) && double.IsFinite(accepted))
                _dragValue = accepted;
            AssociatedObject.SelectAll();
        }

        internal void FinishNumericDrag(bool cancelled, bool restoreText = true)
        {
            _dragCandidate = false;
            if (!_numericDragActive)
                return;
            _numericDragActive = false;
            try
            {
                if (cancelled && restoreText)
                {
                    AssociatedObject.SetCurrentValue(TextBox.TextProperty, _dragOriginalText);
                    AssociatedObject.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                }
                AssociatedObject.RaiseEvent(new NumericDragCompletedEventArgs(cancelled, AssociatedObject));
                if (!restoreText)
                    AssociatedObject.GetBindingExpression(TextBox.TextProperty)?.UpdateTarget();
            }
            finally
            {
                AssociatedObject.Cursor = _dragOldCursor;
                if (AssociatedObject.IsMouseCaptured)
                    AssociatedObject.ReleaseMouseCapture();
            }
        }

        private void NumericMouseUp(object sender, MouseButtonEventArgs e)
        {
            e.Handled |= _numericDragActive;
            FinishNumericDrag(false);
        }
        private void NumericCancelMouse(object sender, MouseButtonEventArgs e)
        {
            e.Handled |= _numericDragActive;
            FinishNumericDrag(true);
        }
        private void NumericKeyDown(object sender, KeyEventArgs e)
        {
            if (_numericDragActive && e.Key == Key.Escape)
            {
                FinishNumericDrag(true);
                e.Handled = true;
            }
        }
        private void NumericLostCapture(object sender, MouseEventArgs e) => FinishNumericDrag(true);
        private void NumericLostFocus(object sender, KeyboardFocusChangedEventArgs e) => FinishNumericDrag(true);
        private void NumericEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!AssociatedObject.IsEnabled) FinishNumericDrag(true);
        }
        private void NumericContextChanged(object sender, DependencyPropertyChangedEventArgs e) => FinishNumericDrag(true, false);
        private void NumericUnloaded(object sender, RoutedEventArgs e) => FinishNumericDrag(true);
    }

    public sealed class NumericDragCompletedEventArgs(bool cancelled, object source)
        : RoutedEventArgs(TextBoxInputBehavior.NumericDragCompletedEvent, source)
    {
        public bool Cancelled { get; } = cancelled;
        protected override void InvokeEventHandler(Delegate genericHandler, object genericTarget) =>
            ((EventHandler<NumericDragCompletedEventArgs>)genericHandler)(genericTarget, this);
    }
}
