using GameWorld.Core.Components.Rendering;
using GameWorld.Core.Rendering.Materials.Capabilities;
using GameWorld.Core.Services;

namespace GameWorld.Core.Rendering.Materials.Shaders.SpecGloss
{
    public class AdvancedRmvMaterial : CapabilityMaterial
    {
        public AdvancedRmvMaterial(IScopedResourceLibrary resourceLibrary)
            : base(CapabilityMaterialsEnum.SpecGlossPbr_Advanced, ShaderTypes.Pbr_SpecGloss, resourceLibrary)
        {
            Capabilities =
            [
                new CommonShaderParametersCapability(),
                new SpecGlossCapability(),
                new AnimationCapability(),
                new AdvancedMaterialCapability(),
                new TintCapability(),
            ];

            _renderingTechniqueMap[RenderingTechnique.Normal] = "BasicColorDrawing";
            _renderingTechniqueMap[RenderingTechnique.Solid] = "SolidDrawing";
        }

        protected override CapabilityMaterial CreateCloneInstance() => new AdvancedRmvMaterial(_resourceLibrary);
    }
}
