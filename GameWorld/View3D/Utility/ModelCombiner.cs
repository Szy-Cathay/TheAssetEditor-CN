using System.Collections.Generic;
using System.Linq;
using GameWorld.Core.Rendering.Geometry;
using GameWorld.Core.Rendering.Materials.Shaders;
using GameWorld.Core.SceneNodes;
using Microsoft.Xna.Framework;
using Shared.Core.ErrorHandling;
using Shared.Core.Services;

namespace GameWorld.Core.Utility
{
    public class ModelCombiner
    {

        public static bool HasPotentialCombineMeshes(List<Rmv2MeshNode> meshList, out ErrorList out_errors)
        {
            out_errors = new ErrorList();

            var combineGroups = SortMeshesIntoCombinableGroups(meshList);
            if (combineGroups.Any(ExceedsVertexLimit))
            {
                out_errors.Error("Error", VertexLimitMessage());
                return false;
            }
            var combineGroupLengths = combineGroups.Select(x => x.Count).Distinct();
            if (combineGroupLengths.Count() == 1 && combineGroupLengths.First() == 1)
            {
                var meshes = combineGroups.Select(x => x.First());
                foreach (var outerMesh in meshes)
                {
                    foreach (var innerMesh in meshes)
                    {
                        if (innerMesh == outerMesh)
                            continue;

                        if (CanCombine(innerMesh, outerMesh, out var errorStr) == false)
                            out_errors.Error("Error", errorStr);

                    }
                }

                return false;
            }

            return true;
        }

   
        public static List<Rmv2MeshNode> CombineMeshes(List<Rmv2MeshNode> geometriesToCombine, bool addPrefix = false)
        {
            var combinedMeshes = new List<Rmv2MeshNode>();
            var combineGroups = SortMeshesIntoCombinableGroups(geometriesToCombine);
            if (combineGroups.Any(ExceedsVertexLimit))
                throw new InvalidOperationException(VertexLimitMessage());
            foreach (var currentGroup in combineGroups)
            {
                if (currentGroup.Count != 1)
                {
                    var combinedMesh = SceneNodeHelper.CloneNode(currentGroup.First());
                    combinedMesh.ModelMatrix = currentGroup.First().ModelMatrix;
                    combinedMesh.Name = currentGroup.First().Name;
                    if (addPrefix)
                        combinedMesh.Name += "_Combined";

                    var newModel = currentGroup.First().Geometry.Clone();
                    combinedMesh.Geometry = newModel;

                    var targetWorld = combinedMesh.GetRenderWorldMatrix();
                    var inverseTarget = InvertMeshTransform(targetWorld);
                    var geoList = new List<MeshObject>();
                    try
                    {
                        foreach (var mesh in currentGroup.Skip(1))
                        {
                            var geometry = mesh.Geometry.Clone();
                            geoList.Add(geometry);
                            var transform = mesh.GetRenderWorldMatrix() * inverseTarget;
                            if (transform != Matrix.Identity)
                            {
                                var normalTransform = Matrix.Transpose(InvertMeshTransform(transform));
                                for (var index = 0; index < geometry.VertexCount(); index++)
                                    geometry.TransformVertex(index, transform, normalTransform);
                            }
                        }
                        newModel.Merge(geoList);
                    }
                    finally
                    {
                        foreach (var geometry in geoList)
                            geometry.Dispose();
                    }

                    combinedMeshes.Add(combinedMesh);
                }
                else
                {
                    combinedMeshes.Add(currentGroup.First());
                }
            }

            return combinedMeshes;
        }

        static bool ExceedsVertexLimit(List<Rmv2MeshNode> meshes) => meshes.Sum(x => (long)x.Geometry.VertexCount()) > MeshObject.MaxVertexCount;

        static Matrix InvertMeshTransform(Matrix transform)
        {
            var determinant = transform.Determinant();
            if (!float.IsFinite(determinant) || determinant == 0)
                throw new InvalidOperationException(LocalizationManager.Instance?.Get("Msg.Kitbash.CombineSingularTransform")
                    ?? "网格缩放为零或变换无效，无法合并。请先恢复有效变换。");
            return Matrix.Invert(transform);
        }

        static string VertexLimitMessage() => LocalizationManager.Instance?.Get("Msg.Kitbash.CombineVertexLimit")
            ?? "合并后的网格超过 65536 个顶点。请减少选择的网格，或先减面再合并。";

        static List<List<Rmv2MeshNode>> SortMeshesIntoCombinableGroups(List<Rmv2MeshNode> meshList)
        {
            var groupedOutput = new List<List<Rmv2MeshNode>>();
            foreach (var currentMesh in meshList)
            {
                var foundMeshToCombineWith = false;
                foreach (var potentialCombineTargetGroup in groupedOutput)
                {
                    var canCombine = CanCombine(potentialCombineTargetGroup.First(), currentMesh, out _);
                    if (canCombine)
                    {
                        potentialCombineTargetGroup.Add(currentMesh);
                        foundMeshToCombineWith = true;
                        break;
                    }
                }

                if (foundMeshToCombineWith == false)
                    groupedOutput.Add(new List<Rmv2MeshNode>() { currentMesh });
            }

            return groupedOutput;
        }

        static bool CanCombine(Rmv2MeshNode meshA, Rmv2MeshNode meshB, out string? errorMessage)
        {
            if (AreMaterialsEqual(meshA.Name, meshA.Material, meshB.Material, meshB.Name, out var textureErrorMsg) == false)
            {
                errorMessage = "Material - " + textureErrorMsg;
                return false;
            }

            if (meshA.Geometry.VertexFormat != meshB.Geometry.VertexFormat)
            {
                errorMessage = "VertexType - " + $"{meshA.Name} has a different vertex type then {meshB.Name}";
                return false;
            }

            if (meshA.Geometry.WeightCount > 0 &&
                (meshA.AnimationPlayer?.IsEnabled == true || meshB.AnimationPlayer?.IsEnabled == true) &&
                (meshA.AnimationPlayer != meshB.AnimationPlayer || meshA.GetRenderWorldMatrix() != meshB.GetRenderWorldMatrix()))
            {
                errorMessage = LocalizationManager.Instance?.Get("Msg.Kitbash.CombineAnimationSpace")
                    ?? "动画预览中的网格使用了不同的动画源或空间变换，合并会改变姿态。请先关闭动画预览，或统一动画源和变换。";
                return false;
            }

            errorMessage = null;
            return true;
        }

        static bool AreMaterialsEqual(string meshNameA, CapabilityMaterial materialA, CapabilityMaterial materialB, string meshNameB, out string? errorMessage)
        {
            var res = materialA.AreEqual(materialB);
            if (res.Result == false)
            {
                errorMessage = $"Comparing material for mesh {meshNameA} against {meshNameB} : {res.Message}";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
