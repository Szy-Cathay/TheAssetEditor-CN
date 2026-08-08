using System;
using System.Collections.Generic;
using GameWorld.Core.Commands;
using GameWorld.Core.Commands.Object;
using GameWorld.Core.Components.Input;
using GameWorld.Core.Components.Rendering;
using GameWorld.Core.SceneNodes;
using GameWorld.Core.Services;
using GameWorld.Core.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Keys = Microsoft.Xna.Framework.Input.Keys;
using MouseButton = GameWorld.Core.Components.Input.MouseButton;

namespace GameWorld.Core.Components.Selection
{
    public sealed class ReferenceObjectSelectionComponent :
        BaseComponent,
        IDisposable
    {
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

        public ReferenceObjectSelectionComponent(
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
            if (_selectionManager.GetState() == null)
            {
                _selectionManager.CreateSelectionSate(
                    GeometrySelectionMode.Object,
                    null,
                    false);
            }

            _selectionTexture = new Texture2D(
                _deviceResolver.Device,
                1,
                1);
            _selectionTexture.SetData([Color.White]);
            base.Initialize();
        }

        public override void Update(GameTime gameTime)
        {
            if (_selectionManager.GetState()?.Mode !=
                GeometrySelectionMode.Object)
            {
                return;
            }

            if (!_mouse.IsMouseOwner(this))
                return;

            _currentMousePosition = _mouse.Position();
            if (_mouse.IsMouseButtonPressed(MouseButton.Left))
            {
                _startDrag = _currentMousePosition;
                _isMouseDown = true;
                if (_mouse.MouseOwner != this)
                    _mouse.MouseOwner = this;
            }

            if (_mouse.IsMouseButtonReleased(MouseButton.Left))
            {
                var rectangle = CreateSelectionRectangle(
                    _startDrag,
                    _currentMousePosition);
                var isModification =
                    _keyboard.IsKeyDown(Keys.LeftShift);
                var removeSelection =
                    _keyboard.IsKeyDown(Keys.LeftControl);
                if (_isMouseDown && rectangle.Width * rectangle.Height >= 10)
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

                _isMouseDown = false;
            }

            if (!_isMouseDown && _mouse.MouseOwner == this)
            {
                _mouse.MouseOwner = null;
                _mouse.ClearStates();
            }
        }

        public void SelectFromIndex(int index)
        {
            var selectable = _sceneManager.GetByIndex(index);
            if (selectable != null)
                SelectObjects([selectable], false, false);
        }

        private void SelectFromRectangle(
            Rectangle screenRectangle,
            bool isModification,
            bool removeSelection)
        {
            var frustum = _camera.UnprojectRectangle(screenRectangle);
            SelectObjects(
                _sceneManager.SelectObjects(frustum),
                isModification,
                removeSelection);
        }

        private void SelectFromPoint(
            Vector2 mousePosition,
            bool isModification,
            bool removeSelection)
        {
            var selectedObject = _sceneManager.SelectObject(
                _camera.CreateCameraRay(mousePosition));
            SelectObjects(
                selectedObject == null ? [] : [selectedObject],
                isModification,
                removeSelection);
        }

        private void SelectObjects(
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
                Color.White * 0.35f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                top,
                Color.White * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                bottom,
                Color.White * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                left,
                Color.White * 0.75f);
            _renderEngine.CommonSpriteBatch.Draw(
                _selectionTexture,
                right,
                Color.White * 0.75f);
            _renderEngine.CommonSpriteBatch.End();
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
            _selectionTexture?.Dispose();
            _selectionTexture = null;
        }
    }
}
