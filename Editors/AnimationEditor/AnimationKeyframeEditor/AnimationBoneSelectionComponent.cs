using System;
using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Bone;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Components.Selection;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using MouseButton = GameWorld.Core.Components.Input.MouseButton;

namespace Editors.AnimationVisualEditors.AnimationKeyframeEditor
{
    public sealed class AnimationBoneSelectionComponent :
        BaseComponent,
        IDisposable
    {
        private const int BonePointSelectionHalfSize = 4;

        private readonly IMouseComponent _mouse;
        private readonly IKeyboardComponent _keyboard;
        private readonly ArcBallCamera _camera;
        private readonly SelectionManager _selectionManager;
        private readonly CommandFactory _commandFactory;
        private readonly SceneManager _sceneManager;
        private readonly RenderEngineComponent _renderEngine;
        private readonly IDeviceResolver _deviceResolver;
        private Texture2D? _selectionTexture;
        private bool _isMouseDown;
        private Vector2 _startDrag;
        private Vector2 _currentMousePosition;

        public AnimationBoneSelectionComponent(
            IMouseComponent mouse,
            IKeyboardComponent keyboard,
            ArcBallCamera camera,
            SelectionManager selectionManager,
            CommandFactory commandFactory,
            SceneManager sceneManager,
            RenderEngineComponent renderEngine,
            IDeviceResolver deviceResolver)
        {
            _mouse = mouse;
            _keyboard = keyboard;
            _camera = camera;
            _selectionManager = selectionManager;
            _commandFactory = commandFactory;
            _sceneManager = sceneManager;
            _renderEngine = renderEngine;
            _deviceResolver = deviceResolver;
            UpdateOrder = (int)ComponentUpdateOrderEnum.SelectionComponent;
            DrawOrder = (int)ComponentDrawOrderEnum.SelectionComponent;
        }

        public override void Initialize()
        {
            if (Isinitialized)
                return;

            _selectionTexture = new Texture2D(
                _deviceResolver.Device,
                1,
                1);
            _selectionTexture.SetData([Color.White]);
            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            var state = _selectionManager.GetState();
            if (state is not ObjectSelectionState and
                not BoneSelectionState)
            {
                ReleaseMouseOwnership();
                return;
            }

            if (!_mouse.IsMouseOwner(this))
                return;

            _currentMousePosition = _mouse.Position();
            if (_mouse.IsMouseButtonPressed(MouseButton.Left))
            {
                _startDrag = _currentMousePosition;
                _isMouseDown = true;
                _mouse.MouseOwner = this;
            }

            if (!_mouse.IsMouseButtonReleased(MouseButton.Left))
                return;

            if (_isMouseDown)
            {
                var rectangle = CreateSelectionRectangle(
                    _startDrag,
                    _currentMousePosition);
                var isModification =
                    _keyboard.IsKeyDown(Keys.LeftShift);
                var removeSelection =
                    _keyboard.IsKeyDown(Keys.LeftControl);

                if (rectangle.Width * rectangle.Height >= 10)
                {
                    SelectFromRectangle(
                        rectangle,
                        isModification,
                        removeSelection);
                }
                else
                {
                    SelectFromPoint(
                        _currentMousePosition,
                        isModification,
                        removeSelection);
                }
            }

            ReleaseMouseOwnership();
        }

        private void SelectFromRectangle(
            Rectangle rectangle,
            bool isModification,
            bool removeSelection)
        {
            var state = _selectionManager.GetState();
            if (state is BoneSelectionState boneSelection)
            {
                SelectBones(
                    boneSelection,
                    rectangle,
                    isModification,
                    removeSelection);
                return;
            }

            if (state is not ObjectSelectionState objectSelection)
                return;

            var selectedObjects = _sceneManager
                .SelectObjects(_camera.UnprojectRectangle(rectangle))
                .ToList();
            if (selectedObjects.Count == 0 && !isModification)
            {
                if (objectSelection.SelectionCount() != 0)
                    ExecuteObjectSelection([], false, false);
                return;
            }

            ExecuteObjectSelection(
                selectedObjects,
                isModification,
                removeSelection);
        }

        private void SelectFromPoint(
            Vector2 mousePosition,
            bool isModification,
            bool removeSelection)
        {
            var state = _selectionManager.GetState();
            if (state is BoneSelectionState boneSelection)
            {
                var pointRectangle = new Rectangle(
                    (int)mousePosition.X -
                        BonePointSelectionHalfSize,
                    (int)mousePosition.Y -
                        BonePointSelectionHalfSize,
                    BonePointSelectionHalfSize * 2,
                    BonePointSelectionHalfSize * 2);
                SelectBones(
                    boneSelection,
                    pointRectangle,
                    isModification,
                    removeSelection);
                return;
            }

            if (state is not ObjectSelectionState objectSelection)
                return;

            var selectedObject = _sceneManager.SelectObject(
                _camera.CreateCameraRay(mousePosition));
            if (selectedObject == null && !isModification)
            {
                if (objectSelection.SelectionCount() != 0)
                    ExecuteObjectSelection([], false, false);
                return;
            }

            if (selectedObject != null)
            {
                ExecuteObjectSelection(
                    [selectedObject],
                    isModification,
                    removeSelection);
            }
        }

        private void SelectBones(
            BoneSelectionState state,
            Rectangle rectangle,
            bool isModification,
            bool removeSelection)
        {
            if (state.RenderObject is not Rmv2MeshNode mesh ||
                state.Skeleton == null ||
                state.CurrentAnimation == null)
            {
                return;
            }

            var intersects = IntersectionMath.IntersectBones(
                _camera.UnprojectRectangle(rectangle),
                mesh,
                state.Skeleton,
                mesh.RenderMatrix,
                out var bones);
            if (intersects)
            {
                _commandFactory.Create<BoneSelectionCommand>()
                    .Configure(command => command.Configure(
                        bones,
                        isModification,
                        removeSelection))
                    .BuildAndExecute();
            }
            else if (!isModification &&
                     !removeSelection &&
                     state.SelectedBones.Count != 0)
            {
                _commandFactory.Create<BoneSelectionCommand>()
                    .Configure(command => command.Configure(
                        [],
                        false,
                        false))
                    .BuildAndExecute();
            }
        }

        private void ExecuteObjectSelection(
            IEnumerable<ISelectable> selectedObjects,
            bool isModification,
            bool removeSelection)
        {
            _commandFactory.Create<ObjectSelectionCommand>()
                .Configure(command => command.Configure(
                    new List<ISelectable>(selectedObjects),
                    isModification,
                    removeSelection))
                .BuildAndExecute();
        }

        public bool SetObjectSelectionMode()
        {
            var state = _selectionManager.GetState();
            if (state?.Mode == GeometrySelectionMode.Object)
                return false;

            _commandFactory.Create<ObjectSelectionModeCommand>()
                .Configure(command => command.Configure(
                    state?.GetSingleSelectedObject(),
                    GeometrySelectionMode.Object))
                .BuildAndExecute();
            return true;
        }

        public bool SetBoneSelectionMode()
        {
            var state = _selectionManager.GetState();
            if (state?.Mode == GeometrySelectionMode.Bone)
                return false;

            var selectedObject = state?.GetSingleSelectedObject();
            if (selectedObject == null)
                return false;

            _commandFactory.Create<ObjectSelectionModeCommand>()
                .Configure(command => command.Configure(
                    selectedObject,
                    GeometrySelectionMode.Bone))
                .BuildAndExecute();
            return true;
        }

        public override void Draw(GameTime gameTime)
        {
            if (!_isMouseDown || _selectionTexture == null)
                return;

            var destination = CreateSelectionRectangle(
                _startDrag,
                _currentMousePosition);
            const int lineWidth = 2;
            var top = new Rectangle(
                destination.X,
                destination.Y,
                destination.Width,
                lineWidth);
            var bottom = new Rectangle(
                destination.X,
                destination.Y + destination.Height,
                destination.Width + lineWidth,
                lineWidth);
            var left = new Rectangle(
                destination.X,
                destination.Y,
                lineWidth,
                destination.Height);
            var right = new Rectangle(
                destination.X + destination.Width,
                destination.Y,
                lineWidth,
                destination.Height);

            _renderEngine.CommonSpriteBatch.Begin(
                SpriteSortMode.Deferred,
                BlendState.AlphaBlend);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                destination,
                Color.White * 0.5f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                top,
                Color.Red * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                bottom,
                Color.Red * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                left,
                Color.Red * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                right,
                Color.Red * 0.75f);
            _renderEngine.CommonSpriteBatch.End();
        }

        private void ReleaseMouseOwnership()
        {
            _isMouseDown = false;
            if (_mouse.MouseOwner != this)
                return;

            _mouse.MouseOwner = null;
            _mouse.ClearStates();
        }

        private static Rectangle CreateSelectionRectangle(
            Vector2 start,
            Vector2 stop)
        {
            return new Rectangle(
                (int)Math.Min(start.X, stop.X),
                (int)Math.Min(start.Y, stop.Y),
                Math.Abs((int)(stop.X - start.X)),
                Math.Abs((int)(stop.Y - start.Y)));
        }

        public void Dispose()
        {
            ReleaseMouseOwnership();
            _selectionTexture?.Dispose();
            _selectionTexture = null;
        }
    }
}
