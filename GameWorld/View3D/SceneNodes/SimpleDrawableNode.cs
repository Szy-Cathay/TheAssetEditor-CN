using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering;
using GameWorld.Core.Rendering.RenderItems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.SceneNodes
{
    // This whole class is very hacky. Please refactor at some points!

    public class SimpleDrawableNode : GroupNode, IDrawableItem
    {
        private readonly List<WorldTextRenderItem> _renderList = [];
        private readonly List<VertexPositionColor> _lineVertexList = [];
        private readonly List<VertexPositionColor> _surfaceVertexList = [];
        private readonly List<EdgeData> _previewEdges = [];

        public SimpleDrawableNode(string name)
        {
            Name = name;
        }

        public void AddItem(WorldTextRenderItem item)
        {
            _renderList.Add(item);
        }

        public void AddItem(VertexPositionColor[] lineArray)
        {
            _lineVertexList.AddRange(lineArray);
        }

        public void AddItem(PreviewShape shape)
        {
            _surfaceVertexList.AddRange(shape.Triangles);
            _previewEdges.AddRange(shape.Edges);
        }

        public void ClearItems()
        {
            _renderList.Clear();
            _lineVertexList.Clear();
            _surfaceVertexList.Clear();
            _previewEdges.Clear();
        }

        public void Render(RenderEngineComponent renderEngine, Matrix parentWorld)
        {
            var m = ModelMatrix * parentWorld;
            foreach (var item in _renderList)
            {
                item.ModelMatrix = m;
                renderEngine.AddRenderItem(RenderBuckedId.Font, item);
            }

            for (var i = 0; i < _lineVertexList.Count; i += 2)
            {
                var transformedPos0 = Vector3.Transform(_lineVertexList[i+0].Position, m);
                var transformed0 = new VertexPositionColor(transformedPos0, _lineVertexList[i+0].Color);

                var transformedPos1 = Vector3.Transform(_lineVertexList[i + 1].Position, m);
                var transformed1 = new VertexPositionColor(transformedPos1, _lineVertexList[i + 1].Color);

                renderEngine.AddRenderLines([transformed0, transformed1]);
            }

            if (_surfaceVertexList.Count != 0)
            {
                var triangles = new VertexPositionColor[
                    _surfaceVertexList.Count];
                for (var i = 0; i < triangles.Length; i++)
                {
                    triangles[i] = new VertexPositionColor(
                        Vector3.Transform(
                            _surfaceVertexList[i].Position,
                            m),
                        _surfaceVertexList[i].Color);
                }
                renderEngine.AddTranslucentPreviewTriangles(triangles);
            }

            if (_previewEdges.Count != 0)
            {
                var edges = new EdgeData[_previewEdges.Count];
                for (var i = 0; i < edges.Length; i++)
                {
                    var source = _previewEdges[i];
                    edges[i] = new EdgeData
                    {
                        P0 = Vector3.Transform(source.P0, m),
                        P1 = Vector3.Transform(source.P1, m),
                        C0 = source.C0,
                        C1 = source.C1,
                        Width = source.Width
                    };
                }
                renderEngine.AddPreviewEdges(edges);
            }
        }

        protected SimpleDrawableNode() { }

        public override ISceneNode CreateCopyInstance() => new SimpleDrawableNode();
    }
}
