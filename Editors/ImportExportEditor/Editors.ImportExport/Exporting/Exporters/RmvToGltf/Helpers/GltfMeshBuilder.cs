using System.IO;
using System.Numerics;
using Editors.ImportExport.Common;
using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.Vertex;
using SharpGLTF.Geometry;
using SharpGLTF.Geometry.VertexTypes;
using SharpGLTF.Materials;
using AlphaMode = SharpGLTF.Materials.AlphaMode;

namespace Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers
{
    public class GltfMeshBuilder
    {
        public List<IMeshBuilder<MaterialBuilder>> Build(
            RmvFile rmv2,
            List<TextureResult> textures,
            RmvToGltfExporterSettings settings,
            bool willHaveSkeleton)
        {
            var lodLevel = rmv2.ModelList.First();
            var hasSkeleton = willHaveSkeleton && string.IsNullOrWhiteSpace(rmv2.Header.SkeletonName) == false;

            var meshes = new List<IMeshBuilder<MaterialBuilder>>();
            for(var i = 0; i < lodLevel.Length; i++)
            {
                var rmvMesh = lodLevel[i];
                var meshTextures = textures.Where(x=>x.MeshIndex == i).ToList();
                var gltfMaterial = Create(settings, rmvMesh.Material.ModelName + "_Material", meshTextures);
                var gltfMesh = GenerateMesh(rmvMesh.Mesh, rmvMesh.Material.ModelName, gltfMaterial, hasSkeleton, settings.MirrorMesh);
                meshes.Add(gltfMesh);
            }
            return meshes;
        }

        IMeshBuilder<MaterialBuilder> GenerateMesh(
            RmvMesh rmvMesh,
            string modelName,
            MaterialBuilder material,
            bool hasSkeleton,
            bool doMirror)
        {
            if (!hasSkeleton)
            {
                return GenerateMesh<VertexEmpty>(
                    rmvMesh,
                    modelName,
                    material,
                    doMirror,
                    null);
            }

            return GenerateMesh<VertexJoints4>(
                rmvMesh,
                modelName,
                material,
                doMirror,
                (source, target) => source.WeightCount > 0
                    ? SetVertexInfluences(source, target)
                    : SetFallbackInfluence(target));
        }

        MeshBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning> GenerateMesh<TSkinning>(
            RmvMesh rmvMesh,
            string modelName,
            MaterialBuilder material,
            bool doMirror,
            Func<CommonVertex,
                VertexBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning>,
                VertexBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning>>? setSkinning)
            where TSkinning : struct, IVertexSkinning
        {
            var mesh = new MeshBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning>(modelName);
            if (setSkinning != null)
                mesh.VertexPreprocessor.SetValidationPreprocessors();

            var prim = mesh.UsePrimitive(material);

            var vertexList = new List<VertexBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning>>();
            foreach (var vertex in rmvMesh.VertexList)
            {
                var glTfvertex = new VertexBuilder<VertexPositionNormalTangent, VertexTexture1, TSkinning>();
                glTfvertex.Geometry.Position = new Vector3(vertex.Position.X, vertex.Position.Y, vertex.Position.Z);
                glTfvertex.Geometry.Normal = new Vector3(vertex.Normal.X, vertex.Normal.Y, vertex.Normal.Z);
                glTfvertex.Geometry.Tangent = new Vector4(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z, 1);
                glTfvertex.Material.TexCoord = new Vector2(vertex.Uv.X, vertex.Uv.Y);

                glTfvertex.Geometry.Position = VecConv.GetSys(GlobalSceneTransforms.FlipVector(VecConv.GetXna(glTfvertex.Geometry.Position), doMirror));

                glTfvertex.Geometry.Normal = VecConv.NormalizeVector3(
                    VecConv.GetSys(GlobalSceneTransforms.FlipVector(VecConv.GetXna(glTfvertex.Geometry.Normal), doMirror)),
                    Vector3.UnitZ);
                glTfvertex.Geometry.Tangent = VecConv.NormalizeTangentVector4(VecConv.GetSys(GlobalSceneTransforms.FlipVector(VecConv.GetXna(glTfvertex.Geometry.Tangent), doMirror)));

                if (setSkinning != null)
                    glTfvertex = setSkinning(vertex, glTfvertex);

                vertexList.Add(glTfvertex);
            }

            var triangleCount = rmvMesh.IndexList.Length;
            for (var i = 0; i < triangleCount; i += 3)
            {

                ushort i0, i1, i2;
                if (doMirror) // if mirrored, flip the winding order
                {
                    i0 = rmvMesh.IndexList[i + 0];
                    i1 = rmvMesh.IndexList[i + 2];
                    i2 = rmvMesh.IndexList[i + 1];
                }
                else
                {
                    i0 = rmvMesh.IndexList[i + 0];
                    i1 = rmvMesh.IndexList[i + 1];
                    i2 = rmvMesh.IndexList[i + 2];
                }

                prim.AddTriangle(vertexList[i0], vertexList[i1], vertexList[i2]);
            }
            return mesh;
        }

        private static VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> SetFallbackInfluence(
            VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> vertex)
        {
            vertex.Skinning.SetBindings((0, 1), (0, 0), (0, 0), (0, 0));

            return vertex;
        }


        VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> SetVertexInfluences(CommonVertex vertex, VertexBuilder<VertexPositionNormalTangent, VertexTexture1, VertexJoints4> glTfvertex)
        {
            var weights = new float[4];
            var indices = new int[4];
            var count = Math.Clamp(vertex.WeightCount, 0, 4);

            for (var index = 0; index < count; index++)
            {
                indices[index] = vertex.BoneIndex[index];
                weights[index] = Math.Max(0, vertex.BoneWeight[index]);
            }

            var sum = weights.Sum();
            if (sum <= float.Epsilon)
            {
                indices[0] = count > 0 ? vertex.BoneIndex[0] : 0;
                weights[0] = 1;
            }
            else
            {
                for (var index = 0; index < weights.Length; index++)
                    weights[index] /= sum;
            }

            glTfvertex.Skinning.SetBindings(
                (indices[0], weights[0]),
                (indices[1], weights[1]),
                (indices[2], weights[2]),
                (indices[3], weights[3]));
            return glTfvertex;
        }

        MaterialBuilder Create(RmvToGltfExporterSettings settings, string materialName, List<TextureResult> texturesForModel)
        {
            var material = new MaterialBuilder(materialName)
                  .WithDoubleSide(true)
                  .WithMetallicRoughness()
                  .WithAlpha(AlphaMode.MASK);

            foreach (var texture in texturesForModel)
            {
                material.WithChannelImage(texture.GlftTexureType, texture.SystemFilePath);

                var channel = material.UseChannel(texture.GlftTexureType);
                if (channel?.Texture?.PrimaryImage != null) 
                {
                    // Set SharpGLTF to re-resave textures with specified paths, default behavior is texturePath = "{folder}\meshName{counter}.png"
                    channel.Texture.PrimaryImage.AlternateWriteFileName = Path.GetFileName(texture.SystemFilePath);
                }                               
            }

            return material;
        }
    }
}
