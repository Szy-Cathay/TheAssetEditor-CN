using System;
using System.Collections.Generic;
using System.Linq;

namespace GameWorld.Core.SceneNodes
{
    public static class SceneNodeHelper
    {
        public static List<T> GetChildrenOfType<T>(ISceneNode root, Func<ISceneNode, bool> filterFunction = null) where T : ISceneNode
        {
            var output = new List<T>();
            foreach (var child in root.Children)
            {
                if (filterFunction != null)
                {
                    var filterResult = filterFunction(child);
                    if (filterResult == false)
                        continue;
                }

                var res = GetChildrenOfType<T>(child, filterFunction);
                output.AddRange(res);
                if (child is T)
                    output.Add((T)child);
            }
            return output;
        }

        public static T CloneNode<T>(T target) where T : ISceneNode
        {
            var clone = (T)target.CreateCopyInstance();
            target.CopyInto(clone);
            return clone;
        }


        public static T CloneNodeAndChildren<T>(T target) where T : ISceneNode
        {
            var clone = (T)target.CreateCopyInstance();
            target.CopyInto(clone);

            foreach (var child in target.Children)
            {
                var childClone = CloneNodeAndChildren(child);
                clone.Children.Add(childClone);
            }
            return clone;
        }

        public static string GetSkeletonName(ISceneNode result)
        {
            var output = new List<string>();

            var nodeQueue = new Queue<ISceneNode>();
            nodeQueue.Enqueue(result);
            while (nodeQueue.Count != 0)
            {
                var item = nodeQueue.Dequeue();
                foreach (var child in item.Children)
                    nodeQueue.Enqueue(child);

                if (item is Rmv2ModelNode modelNode)
                    output.Add(modelNode.GetMeshNodes(0).First().Geometry.SkeletonName);
            }

            var potentialSkeletons = output.Where(x => string.IsNullOrWhiteSpace(x) == false);
            if (output.Count == 0)
                return "";
            return potentialSkeletons.First();
        }

        public static bool AreAllNodesVisible(Rmv2ModelNode searchStart)
        {
            var isAllVisible = true;
            searchStart.GetLodNodes()[0].ForeachNodeRecursive((node) =>
            {
                if (!node.IsVisible)
                    isAllVisible = false;
            });
            return isAllVisible;
        }
    }
}
