#include "../Helpers/CASpecglosshelper.hlsli"
#include "../helpers/constants.hlsli"
#include "../Helpers/tone_mapping.hlsli"

#include "../Shared/const_layout.hlsli"
#include "../Shared/MainVertexShader.hlsli"

#include "../TextureSamplers.hlsli"
#include "../inputlayouts.hlsli"
#include "../Capabilites/Tint.hlsli"
#include "../Shared/ViewportSolid.hlsli"

bool SelectionMaskEnabled;

// -------------------------------------------------------------------------------------------------------------
//  Fetch Data needed to render 1 pixel
// -------------------------------------------------------------------------------------------------------------
GBufferMaterial GetMaterial(in PixelInputType input)
{
    GBufferMaterial material;

    // default values
    material.diffuse = float4(0.2f, 0.2f, 0.2f, 1);
    material.specular = float4(0, 0, 0, 1);
    material.roughness = 1.0f;
    material.metalness = 0.0f;
    material.pixelNormal = input.normal;
    material.maskValue = float4(0, 0, 0, 0);
    
    float2 texCord = float2(nfmod(input.tex.x, 1), nfmod(input.tex.y, 1));
        
    if (UseSpecular)
    {
        material.specular = _linear(SpecularTexture.Sample(SampleType, texCord).rgb);
    }
    
    if (UseDiffuse)
    {
        float4 diffuseTexSample = DiffuseTexture.Sample(SampleType, texCord);
        material.diffuse.rgb = _linear(diffuseTexSample.rgb);
        material.diffuse.a = diffuseTexSample.a;
    }
    
    if (UseGloss)
    {
        float4 glossTex = GlossTexture.Sample(SampleType, texCord);
        material.roughness = saturate(1 - glossTex.r * glossTex.r);
    }
    
    if (UseNormal)
    {
        material.pixelNormal = GetPixelNormal(input);
    }

    if (UseMask)
    {
        material.maskValue = MaskTexture.Sample(SampleType, texCord);
    }
    
    return material;
}

struct MainPixelOutput
{
    float4 Colour : SV_TARGET0;
    float4 SelectionMask : SV_TARGET1;
};

MainPixelOutput mainPS(in PixelInputType input, bool bIsFrontFace : SV_IsFrontFace)
{    
    // -- fetch data needed to light pixel
    GBufferMaterial material = GetMaterial(input);

    if (UseAlpha == 1)    
        alpha_test(material.diffuse.a);

    material.diffuse.rgb = ApplyTintAndFactionColours(
        material.diffuse.rgb,
        material.maskValue);
    
    float3 normalizedViewDirection = -normalize(CameraPos - input.worldPosition);

    // no SSAO + no shadows    
    float occlusion = 1.0f;
    float shadow = 1.0f;
    
	//  Create the standard material...       
    R2_4_StandardLightingModelMaterial standard_mat = R2_4_create_standard_lighting_material(
        material.diffuse.rgb,
        material.specular.rgb,
        material.pixelNormal.rgb,
        1.0 - material.roughness,
        float4(input.worldPosition, 0),
        shadow,
        occlusion);    

    float3 reflected_view_vec = reflect(normalizedViewDirection, standard_mat.Normal);

    float3 env_light = standard_lighting_model_environment_light_SM4_private(
        normalizedViewDirection,
        reflected_view_vec,
        standard_mat);

    float unchartedSunFactor = 3.0f;
    float3 L_main = normalize(CameraPos - input.worldPosition);
    float3 lightCol_main = get_sun_colour() * unchartedSunFactor;

    float3 combined_dir_light = standard_lighting_model_directional_light_SM4_private(
        lightCol_main,
        L_main,
        normalizedViewDirection,
        reflected_view_vec,
        standard_mat);

    float3 hdr_linear_col = env_light + (ViewportEnvironmentEnabled ? 0 : combined_dir_light);
    hdr_linear_col *= Constant_LightColour;

    float3 ldr_linear_col = saturate(Uncharted2ToneMapping(hdr_linear_col));
    
    MainPixelOutput output;
    output.Colour = ViewportSurfaceColour(_gamma(ldr_linear_col));
    output.SelectionMask = SelectionMaskEnabled
        ? float4(1.0f, 1.0f, 1.0f, 1.0f)
        : float4(0.0f, 0.0f, 0.0f, 0.0f);
    return output;
}

MainPixelOutput SolidPixelShader(
    in PixelInputType input,
    bool bIsFrontFace : SV_IsFrontFace)
{
    MainPixelOutput output;
    output.Colour = ViewportSurfaceColour(
        ViewportWireframe ? (SelectionMaskEnabled && ViewportWireframeObjectSelection ? float3(1.0f, 0.47f, 0.0f) : float3(0.45f, 0.46f, 0.48f)) : ShadeViewportSolid(
            input.normal,
            input.worldPosition,
            CameraPos, input.position.xy));
    output.SelectionMask = SelectionMaskEnabled
        ? float4(1.0f, 1.0f, 1.0f, 1.0f)
        : float4(0.0f, 0.0f, 0.0f, 0.0f);
    return output;
}


technique BasicColorDrawing
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 mainPS();
    }
};

technique SolidDrawing
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 SolidPixelShader();
    }
};

technique ViewportGeometry
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 ViewportGeometryPixelShader();
    }
};
