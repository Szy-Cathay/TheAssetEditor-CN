using System.Windows;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;
using Shared.Core.Services;
using Test.TestingUtility.TestUtility;

namespace Test.TestingUtility.Shared
{
    public class WpfGameMock : IWpfGame
    {
        static GraphicsDeviceServiceMock? s_instance;
        static ContentManager? s_contentManager;

        public ContentManager Content { get; set; }
        public GraphicsDevice GraphicsDevice { get; private set; }

        public WpfGameMock()
        {
            if (s_instance == null)
            {
                var mock = new GraphicsDeviceServiceMock();
                var services = new GameServiceContainer();
                services.AddService<IGraphicsDeviceService>(mock);
                var contentPath = PathHelper.GetProjectOutputPath("GameWorld\\ContentProject", "Content");
                var fullPath = PathHelper.GetDataFolder(contentPath);
                if (Directory.Exists(fullPath) == false)
                {
                    throw new Exception("Unable to determine full path of content folder");
                }

                s_instance = mock;
                s_contentManager = new ContentManager(services, fullPath);
            }


            GraphicsDevice = s_instance.GraphicsDevice;
            Content = s_contentManager!;
        }

        public T AddComponent<T>(T comp) where T : IGameComponent
        {
            comp.Initialize();
            return comp;
        }

        public void ForceEnsureCreated()
        {
        }

        public FrameworkElement GetFocusElement()
        {
#pragma warning disable CS8603 // Possible null reference return.
            return null;
#pragma warning restore CS8603 // Possible null reference return.
        }

        public void RemoveComponent<T>(T comp) where T : IGameComponent
        {
        }
    }
}
