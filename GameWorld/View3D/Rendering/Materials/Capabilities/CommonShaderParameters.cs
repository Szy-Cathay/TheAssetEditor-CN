using GameWorld.Core.Services;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace GameWorld.Core.Rendering.Materials.Capabilities
{
    public class CommonShaderParametersCapability : ICapability
    {
        public Matrix View { get; set; }
        public Matrix Projection { get; set; }
        public Vector3 CameraPosition { get; set; }
        public Vector3 CameraLookAt { get; set; }
        public float EnvLightRotationsRadians_Y { get; set; }
        public float DirLightRotationRadians_X { get; set; }
        public float DirLightRotationRadians_Y { get; set; }

        public Matrix ModelMatrix { get; set; }

        public float LightIntensityMult { get; set; }
        public Vector3 LightColour { get; set; } = Vector3.One;
        public float SurfaceOpacity { get; set; } = 1;
        public bool ViewportWireframe { get; set; }
        public Components.Rendering.ViewportShadingSettings? ViewportShading { get; set; }
        public Texture2D? ViewportMatcap { get; set; }
        public Texture2D? ViewportGeometry { get; set; }
        public TextureCube? ViewportDiffuse { get; set; }
        public TextureCube? ViewportSpecular { get; set; }
        public Vector2 ViewportSize { get; set; }

        public void Apply(Effect effect, IScopedResourceLibrary _)
        {
            effect.Parameters["View"].SetValue(View);
            effect.Parameters["Projection"].SetValue(Projection);
            effect.Parameters["EnvMapTransform"].SetValue(Matrix.CreateRotationY(EnvLightRotationsRadians_Y));
            effect.Parameters["DirLightTransform"].SetValue(Matrix.CreateRotationY(DirLightRotationRadians_Y) * Matrix.CreateRotationX(DirLightRotationRadians_X));
            effect.Parameters["LightMult"].SetValue(LightIntensityMult);
            effect.Parameters["Constant_LightColour"].SetValue(LightColour);
            effect.Parameters["World"].SetValue(ModelMatrix);
            effect.Parameters["CameraPos"].SetValue(CameraPosition);
            effect.Parameters["ViewportSurfaceOpacity"]?.SetValue(SurfaceOpacity);
            effect.Parameters["ViewportWireframe"]?.SetValue(ViewportWireframe);
            effect.Parameters["ViewportWireframeObjectSelection"]?.SetValue(ViewportShading?.WireframeObjectSelection ?? true);
            effect.Parameters["ViewportSolidLighting"]?.SetValue((int)(ViewportShading?.SolidLighting ?? 0));
            effect.Parameters["ViewportMatcap"]?.SetValue(ViewportMatcap);
            effect.Parameters["ViewportGeometry"]?.SetValue(ViewportGeometry);
            effect.Parameters["ViewportGeometryEnabled"]?.SetValue(ViewportGeometry != null);
            effect.Parameters["ViewportSize"]?.SetValue(ViewportSize);
            effect.Parameters["ViewportInverseProjection"]?.SetValue(ViewportGeometry == null ? Matrix.Identity : Matrix.Invert(Projection));
            effect.Parameters["ViewportCavityStrength"]?.SetValue(ViewportShading?.CavityStrength ?? 0);
            effect.Parameters["ViewportShadowStrength"]?.SetValue(ViewportShading?.ShadowStrength ?? 0);
            effect.Parameters["ViewportDiffuse"]?.SetValue(ViewportDiffuse);
            effect.Parameters["ViewportSpecular"]?.SetValue(ViewportSpecular);
            effect.Parameters["ViewportEnvironmentEnabled"]?.SetValue(ViewportDiffuse != null && ViewportSpecular != null);
            effect.Parameters["ViewportEnvironmentRotationEnabled"]?.SetValue(ViewportShading?.UseLocalLighting == true);
        }

        public void Assign(CommonShaderParameters parameters, Matrix modelMatrix)
        {
            ModelMatrix = modelMatrix;

            View = parameters.View;
            Projection = parameters.Projection;
            CameraPosition = parameters.CameraPosition;
            CameraLookAt = parameters.CameraLookAt;
            EnvLightRotationsRadians_Y = parameters.EnvLightRotationsRadians_Y;
            DirLightRotationRadians_X = parameters.DirLightRotationRadians_X;
            DirLightRotationRadians_Y = parameters.DirLightRotationRadians_Y;
            LightIntensityMult = parameters.LightIntensityMult;
            LightColour = parameters.LightColour;
            SurfaceOpacity = parameters.SurfaceOpacity;
            ViewportWireframe = parameters.ViewportWireframe;
            ViewportShading = parameters.ViewportShading;
            ViewportMatcap = parameters.ViewportMatcap;
            ViewportGeometry = parameters.ViewportGeometry;
            ViewportDiffuse = parameters.ViewportDiffuse;
            ViewportSpecular = parameters.ViewportSpecular;
            ViewportSize = new Vector2(parameters.ViewportWidth, parameters.ViewportHeight);
        }

        public ICapability Clone()
        {
            return new CommonShaderParametersCapability()
            {
                View = View,
                Projection = Projection,
                CameraPosition = CameraPosition,
                CameraLookAt = CameraLookAt,
                EnvLightRotationsRadians_Y = EnvLightRotationsRadians_Y,
                DirLightRotationRadians_X = DirLightRotationRadians_X,
                DirLightRotationRadians_Y = DirLightRotationRadians_Y,
                ModelMatrix = ModelMatrix,
                LightIntensityMult = LightIntensityMult,
                LightColour = LightColour,
                SurfaceOpacity = SurfaceOpacity,
                ViewportWireframe = ViewportWireframe,
                ViewportShading = ViewportShading,
                ViewportMatcap = ViewportMatcap,
                ViewportGeometry = ViewportGeometry,
                ViewportDiffuse = ViewportDiffuse,
                ViewportSpecular = ViewportSpecular,
                ViewportSize = ViewportSize,
            };
        }

        public (bool Result, string Message) AreEqual(ICapability otherCap)
        {
            var typedCap = otherCap as CommonShaderParametersCapability;
            if (typedCap == null)
                throw new System.Exception($"Comparing {GetType} against {otherCap?.GetType()}");
            return (true, "");
        }
    }
}
