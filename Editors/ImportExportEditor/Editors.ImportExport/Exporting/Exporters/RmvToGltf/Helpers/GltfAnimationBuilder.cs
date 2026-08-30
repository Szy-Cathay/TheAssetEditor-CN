using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Automation;
using Editors.ImportExport.Common;
using GameWorld.Core.Animation;
using Shared.Core.PackFiles;
using Shared.Core.PackFiles.Models;
using Shared.Core.Services;
using Shared.GameFormats.Animation;
using SharpGLTF.Schema2;
using SysNum = System.Numerics;

namespace Editors.ImportExport.Exporting.Exporters.RmvToGltf.Helpers
{
    public class GltfAnimationBuilder
    {
        private readonly IPackFileService _packFileService;
        

        public GltfAnimationBuilder(IPackFileService packFileServoce)
        {
            _packFileService = packFileServoce;            
        }

        public void Build(AnimationFile animSkeleton, RmvToGltfExporterSettings settings, ProcessedGltfSkeleton gltfSkeleton, ModelRoot outputScene)
        {
            var usedAnimationNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var animationPackFile in settings.InputAnimationFiles)
            {
                var animationToExport = AnimationFile.Create(animationPackFile);
                var baseName = Path.GetFileNameWithoutExtension(animationPackFile.Name);
                var animationName = GetUniqueAnimationName(baseName, usedAnimationNames);
                if (!string.Equals(
                        animationToExport.Header.SkeletonName,
                        animSkeleton.Header.SkeletonName,
                        StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidDataException(
                        LocalizationManager.Instance.GetFormat(
                            "RmvToGltfExporter.Error.AnimationSkeletonMismatch",
                            animationName,
                            animationToExport.Header.SkeletonName,
                            animSkeleton.Header.SkeletonName));
                }

                CreateFromTWAnim(animationName, gltfSkeleton, animSkeleton, animationToExport, outputScene, settings);
            }
        }

        private static string GetUniqueAnimationName(
            string baseName,
            ISet<string> usedAnimationNames)
        {
            if (usedAnimationNames.Add(baseName))
                return baseName;

            var suffix = 2;
            string candidate;
            do
            {
                candidate = $"{baseName}_{suffix++}";
            } while (!usedAnimationNames.Add(candidate));

            return candidate;
        }

        private void CreateFromTWAnim(string animationName, ProcessedGltfSkeleton gltfSkeleton, AnimationFile skeletonAnimFile, AnimationFile animationToExport, ModelRoot modelRoot, RmvToGltfExporterSettings settings)
        {
            var doMirror = settings.MirrorMesh;
            var gameSkeleton = new GameSkeleton(skeletonAnimFile, null);
            var animationClip = new AnimationClip(animationToExport, gameSkeleton);
            if (animationClip.DynamicFrames.Count == 0)
                throw new InvalidDataException($"动画“{animationName}”不包含任何动态帧。");
            if (!float.IsFinite(animationToExport.Header.FrameRate) || animationToExport.Header.FrameRate <= 0)
                throw new InvalidDataException($"动画“{animationName}”的帧率无效。");

            var secondsPerFrame = 1.0f / animationToExport.Header.FrameRate;

            var gltfAnimation = modelRoot.CreateAnimation(animationName);

            for (var boneIndex = 0; boneIndex < animationClip.AnimationBoneCount; boneIndex++)
            {
                var translationKeyFrames = new Dictionary<float, SysNum.Vector3>();
                var rotationKeyFrames = new Dictionary<float, SysNum.Quaternion>();
                var scaleKeyFrames = new Dictionary<float, SysNum.Vector3>();

                // populate the bone track containers with the key frames from the .ANIM animation file
                for (var frameIndex = 0; frameIndex < animationClip.DynamicFrames.Count; frameIndex++)
                {
                    translationKeyFrames.Add(secondsPerFrame * (float)frameIndex, VecConv.GetSys(GlobalSceneTransforms.FlipVector(animationClip.DynamicFrames[frameIndex].Position[boneIndex], doMirror)));
                    var rotation = Microsoft.Xna.Framework.Quaternion.Normalize(
                        GlobalSceneTransforms.FlipQuaternion(
                            animationClip.DynamicFrames[frameIndex]
                                .Rotation[boneIndex],
                            doMirror));
                    rotationKeyFrames.Add(
                        secondsPerFrame * (float)frameIndex,
                        VecConv.GetSys(rotation));
                    scaleKeyFrames.Add(secondsPerFrame * (float)frameIndex, new SysNum.Vector3(1, 1, 1));
                }

                // add the transformations
                var boneNode = gltfSkeleton.Data[boneIndex].Item1;
                gltfAnimation.CreateRotationChannel(boneNode, rotationKeyFrames);
                gltfAnimation.CreateTranslationChannel(boneNode, translationKeyFrames);
                gltfAnimation.CreateScaleChannel(boneNode, scaleKeyFrames);
            }
        }
    }
}
