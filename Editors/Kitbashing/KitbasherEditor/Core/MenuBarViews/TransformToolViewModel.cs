using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GameWorld.Core.Commands;
using GameWorld.Core.Components.Gizmo;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Shared.Core.Events;
using Shared.Core.Misc;
using Shared.Core.Services;
using Shared.Ui.BaseDialogs.MathViews;
using System.Runtime.ExceptionServices;
using System.Globalization;
using Editors.KitbasherEditor.Components;

namespace KitbasherEditor.ViewModels.MenuBarViews
{
    public class TransformToolViewModel : NotifyPropertyChangedImpl
    {
        public enum TransformMode
        {
            None,
            Rotate,
            Scale,
            Translate
        }

        TransformMode _activeMode = TransformMode.None;

        private readonly SelectionManager _selectionManager;
        private readonly CommandExecutor _commandExecutor;
        private readonly CommandFactory _commandFactory;

        private KitbashModelGizmoComponent _gizmoComponent;
        private TransformGizmoWrapper _numericTransform;
        private Matrix _scaleOrientation = Matrix.Identity;
        private PivotType _pivot = PivotType.ObjectCenter;
        private bool _synchronizing;
        private bool _gizmoActive;
        private Vector3 _numericInitialValue;
        public bool CanEditValues => !_gizmoActive;
        public double DragStep => _activeMode == TransformMode.Rotate ? 0.25 : 0.01;
        public string CoordinateDescription => LocalizationManager.Instance.Get(
            _activeMode == TransformMode.Scale && _scaleOrientation != Matrix.Identity
                ? "Kitbasher.Transform.LocalScaleDelta" : "Kitbasher.Transform.WorldDelta");

        public System.Windows.Input.ICommand ApplyCommand { get; set; }

        bool _buttonEnabled = false;
        public bool ButtonEnabled { get { return _buttonEnabled; } set { SetAndNotify(ref _buttonEnabled, value); } }


        bool _isVisible = false;
        public bool IsVisible { get { return _isVisible; } set { SetAndNotify(ref _isVisible, value); } }

        string _text;
        public string Text { get { return _text; } set { SetAndNotify(ref _text, value); } }

        string _description;
        public string Description { get => _description; private set => SetAndNotify(ref _description, value); }

        Vector3ViewModel _vector3 = new Vector3ViewModel(0, 0, 0);
        public Vector3ViewModel Vector3 { get { return _vector3; } set { SetAndNotify(ref _vector3, value); } }

        public NotifyAttr<DoubleViewModel> VertexMovementFalloff { get; set; }

        public NotifyAttr<bool> ShowVertexFalloff { get; set; } = new NotifyAttr<bool>(false);

        public TransformToolViewModel(SelectionManager selectionManager, CommandExecutor commandExecutor, CommandFactory commandFactory, IEventHub eventHub)
        {
            ApplyCommand = new RelayCommand(ApplyTransform);
            Vector3.OnValueChanged += _ => PreviewNumericEdit();

            VertexMovementFalloff = new NotifyAttr<DoubleViewModel>(new DoubleViewModel());
            VertexMovementFalloff.Value.PropertyChanged += VertexMovementFalloffChanged;
            _selectionManager = selectionManager;
            _commandExecutor = commandExecutor;
            _commandFactory = commandFactory;
            eventHub.Register<SelectionChangedEvent>(this, Handle);
        }

        public void VertexMovementFalloffChanged(object sender, PropertyChangedEventArgs e)
        {
            _selectionManager.UpdateVertexSelectionFallof((float)VertexMovementFalloff.Value.Value);
        }

        private void SelectionChanged(ISelectionState state)
        {
            EndNumericEdit(true);
            SetDefaultValue();
            ShowVertexFalloff.Value = false;
            ButtonEnabled = false;
            if (state is ObjectSelectionState objectSelectionState)
                ButtonEnabled = objectSelectionState.SelectionCount() != 0;
            else if (state is VertexSelectionState vertexSelectionState)
            {
                ButtonEnabled = vertexSelectionState.SelectionCount() != 0;
                ShowVertexFalloff.Value = true;
            }
            else if (state is FaceSelectionState faceSelectionState)
            {
                ButtonEnabled = faceSelectionState.SelectionCount() != 0;
                ShowVertexFalloff.Value = true;
            }
            else if (state is EdgeSelectionState edgeSelectionState)
            {
                ButtonEnabled = edgeSelectionState.SelectionCount() != 0;
                ShowVertexFalloff.Value = true;
            }
            else if (state is BoneSelectionState boneSelectionState)
                ButtonEnabled = boneSelectionState.SelectionCount() != 0;
        }

        public void SetMode(TransformMode mode)
        {
            EndNumericEdit(true);
            _activeMode = mode;
            IsVisible = _activeMode != TransformMode.None;

            if (_activeMode == TransformMode.Rotate)
                Text = LocalizationManager.Instance.Get("Kitbasher.Transform.Rotate");
            else if (_activeMode == TransformMode.Scale)
                Text = LocalizationManager.Instance.Get("Kitbasher.Transform.Scale");
            else if (_activeMode == TransformMode.Translate)
                Text = LocalizationManager.Instance.Get("Kitbasher.Transform.Translate");

            Description = mode == TransformMode.None ? "" : LocalizationManager.Instance.Get($"Kitbasher.Transform.{mode}.Help");

            SetDefaultValue();
            NotifyPropertyChanged(nameof(DragStep));
            NotifyPropertyChanged(nameof(CoordinateDescription));
        }

        void ApplyTransform()
        {
            if (_activeMode == TransformMode.None || _gizmoActive || !HasValidValues())
                return;

            var transform = TransformGizmoWrapper.CreateFromSelectionState(_selectionManager.GetState(), _commandFactory);
            if (transform == null)
                return;

            using (transform)
            {
                ExceptionDispatchInfo primaryError = null;
                var commitAttempted = false;
                try
                {
                    ConfigureTransform(transform);
                    transform.BeginTransform();
                    transform.ReplaceInitialPreview(CreateReplacement());
                    commitAttempted = true;
                    transform.CommitTransform(_commandExecutor);
                    SetDefaultValue();
                }
                catch (Exception exception)
                {
                    primaryError = ExceptionDispatchInfo.Capture(exception);
                    if (!commitAttempted)
                    {
                        try
                        {
                            transform.CancelTransform();
                        }
                        catch
                        {
                        }
                    }
                }

                primaryError?.Throw();
            }
        }

        public void AttachGizmo(KitbashModelGizmoComponent component)
        {
            if (_gizmoComponent != null)
                _gizmoComponent.PreviewChanged -= SynchronizeGizmoPreview;
            _gizmoComponent = component;
            component.PreviewChanged += SynchronizeGizmoPreview;
        }

        internal void SynchronizeGizmoPreview(GizmoMode mode, ModalPreviewReplacement? preview)
        {
            if (!preview.HasValue)
            {
                _gizmoActive = false;
                SetDefaultValue();
            }
            else
            {
                var transformMode = mode switch
                {
                    GizmoMode.Rotate => TransformMode.Rotate,
                    GizmoMode.Translate => TransformMode.Translate,
                    _ => TransformMode.Scale
                };
                if (_activeMode != transformMode)
                    SetMode(transformMode);
                _gizmoActive = true;
                var value = preview.Value;
                _pivot = value.Pivot;
                if (value.Kind == ModalPreviewReplacementKind.Scale)
                    _scaleOrientation = value.ScaleOrientation;
                SetVector(value.Kind switch
                {
                    ModalPreviewReplacementKind.Translate => value.VectorValue,
                    ModalPreviewReplacementKind.Rotate => RotationDegrees(value.RotationValue),
                    ModalPreviewReplacementKind.Scale => value.VectorValue + Microsoft.Xna.Framework.Vector3.One,
                    _ => _activeMode == TransformMode.Scale ? Microsoft.Xna.Framework.Vector3.One : Microsoft.Xna.Framework.Vector3.Zero
                });
            }
            NotifyPropertyChanged(nameof(CanEditValues));
            NotifyPropertyChanged(nameof(CoordinateDescription));
        }

        // Row-vector X * Y * Z, identical to the numeric apply path.
        internal static Vector3 RotationDegrees(Matrix matrix)
        {
            var y = MathF.Asin(Math.Clamp(-matrix.M13, -1, 1));
            var x = Math.Abs(MathF.Cos(y)) > 0.0001f
                ? MathF.Atan2(matrix.M23, matrix.M33)
                : MathF.Atan2(-matrix.M32, matrix.M22);
            var z = Math.Abs(MathF.Cos(y)) > 0.0001f ? MathF.Atan2(matrix.M12, matrix.M11) : 0;
            return new Vector3(MathHelper.ToDegrees(x), MathHelper.ToDegrees(y), MathHelper.ToDegrees(z));
        }

        private void ConfigureTransform(TransformGizmoWrapper transform)
        {
            transform.SetFalloffDistance(_selectionManager.VertexSelectionFalloff);
            _pivot = _gizmoComponent?.Gizmo?.ActivePivot ?? PivotType.ObjectCenter;
            _scaleOrientation = _gizmoComponent?.Gizmo?.GizmoDisplaySpace == TransformSpace.Local
                ? Matrix.CreateFromQuaternion(transform.Orientation) : Matrix.Identity;
            NotifyPropertyChanged(nameof(CoordinateDescription));
        }

        private ModalPreviewReplacement CreateReplacement()
        {
            var value = _vector3.GetAsVector3();
            return _activeMode switch
            {
                TransformMode.Translate => ModalPreviewReplacement.Translate(value, _pivot),
                TransformMode.Rotate => ModalPreviewReplacement.Rotate(
                    Matrix.CreateRotationX(MathHelper.ToRadians(value.X)) *
                    Matrix.CreateRotationY(MathHelper.ToRadians(value.Y)) *
                    Matrix.CreateRotationZ(MathHelper.ToRadians(value.Z)), _pivot),
                TransformMode.Scale => ModalPreviewReplacement.Scale(value - Microsoft.Xna.Framework.Vector3.One, _pivot, _scaleOrientation),
                _ => ModalPreviewReplacement.RestoreOnly(_pivot)
            };
        }

        private bool HasValidValues() => new[] { _vector3.X, _vector3.Y, _vector3.Z }.All(x =>
            double.TryParse(x.TextValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) &&
            double.IsFinite(number) && Math.Abs(number) <= float.MaxValue);

        public void BeginNumericEdit()
        {
            if (_gizmoActive || _activeMode == TransformMode.None || !HasValidValues())
                return;
            EndNumericEdit(true);
            var transform = TransformGizmoWrapper.CreateFromSelectionState(_selectionManager.GetState(), _commandFactory);
            if (transform == null)
                return;
            _numericInitialValue = _vector3.GetAsVector3();
            ConfigureTransform(transform);
            try
            {
                transform.BeginTransform();
                _numericTransform = transform;
            }
            catch
            {
                transform.Dispose();
                throw;
            }
        }

        private void PreviewNumericEdit()
        {
            if (_synchronizing || _numericTransform == null || !HasValidValues())
                return;
            try
            {
                if (!_numericTransform.ReplaceInitialPreview(CreateReplacement()))
                    SetDefaultValue();
            }
            catch
            {
                EndNumericEdit(true);
                throw;
            }
        }

        public void EndNumericEdit(bool cancelled)
        {
            var transform = _numericTransform;
            if (transform == null)
                return;
            _numericTransform = null;
            try
            {
                if (cancelled)
                    transform.CancelTransform();
                else
                    transform.CommitTransform(_commandExecutor);
            }
            finally
            {
                transform.Dispose();
                if (cancelled) SetVector(_numericInitialValue);
                else SetDefaultValue();
            }
        }

        private void SetVector(Vector3 value)
        {
            _synchronizing = true;
            try { _vector3.Set(value); }
            finally { _synchronizing = false; }
        }

        void SetDefaultValue() => SetVector(_activeMode == TransformMode.Scale ? Microsoft.Xna.Framework.Vector3.One : Microsoft.Xna.Framework.Vector3.Zero);
        void Handle(SelectionChangedEvent notification)
        {
            SelectionChanged(notification.NewState);
        }
    }
}
