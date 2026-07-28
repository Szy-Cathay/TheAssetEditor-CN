#include "../helpers/CAMetalRoughnessHelper.hlsli"
#include "../helpers/tone_mapping.hlsli"
#include "../helpers/constants.hlsli"
#include "../helpers/GradientSampling.hlsli"

#include "../Shared/MainVertexShader.hlsli"
#include "../Shared/const_layout.hlsli"

#include "../TextureSamplers.hlsli"
#include "../inputlayouts.hlsli"

#include "../Capabilites/Emissive.hlsli"
#include "../Capabilites/Tint.hlsli"

bool SelectionMaskEnabled;

//#include "Helpers.hlsli"

// **************************************************************************************************************************************
// *		PIXEL SHADER CODE
// **************************************************************************************************************************************
GBufferMaterial GetMaterial(in PixelInputType input)
{
    GBufferMaterial material;

    // default values
    material.diffuse = float4(0.2f, 0.2f, 0.2f, 1);
    material.specular = float4(0, 0, 0, 0);
    material.roughness = 1.0f;
    material.metalness = 0.0f;
    material.pixelNormal = input.normal;
    material.maskValue = float4(0, 0, 0, 0);

    float2 texCord = float2(nfmod(input.tex.x, 1), nfmod(input.tex.y, 1));

    if (UseSpecular)
    {
        material.specular.rgb = _linear(SpecularTexture.Sample(SampleType, texCord).rgb);
    }

    if (UseDiffuse)
    {
        float4 diffuseValue = DiffuseTexture.Sample(SampleType, texCord);
        material.diffuse.rgb = _linear(diffuseValue.rgb);
        material.diffuse.a = diffuseValue.a;
    }

    if (UseGloss)
    {
        float4 glossTexSample = GlossTexture.Sample(SampleType, texCord);
        material.metalness = (glossTexSample.r);
        material.roughness = (glossTexSample.g);

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

MainPixelOutput DefaultPixelShader(in PixelInputType input, bool bIsFrontFace : SV_IsFrontFace)
{
    GBufferMaterial material = GetMaterial(input);

    if (UseAlpha == 1)
        alpha_test(material.diffuse.a);

    float3 normalizedViewDirection = -normalize(CameraPos - input.worldPosition);

    const float occlusion = 1.0f;

    R2_5_StandardLightingModelMaterial_For_GBuffer standard_mat_compressed =
        R2_5_create_standard_lighting_material_for_gbuffer(
        material.diffuse.rgb,
        material.pixelNormal,
        material.roughness,
        material.metalness,
        occlusion);

    R2_5_StandardLightingModelMaterial_For_Lighting slm_uncompressed = R2_5_get_slm_for_lighting(standard_mat_compressed);
    slm_uncompressed.Diffuse_Colour.rgb = ApplyTintAndFactionColours(slm_uncompressed.Diffuse_Colour.rgb, material.maskValue);

    float3 reflected_view_vec = reflect(normalizedViewDirection, slm_uncompressed.Normal);

    // Environment light (CubeMap) - AE 7.0 style
    float3 env_light = standard_lighting_model_environment_light_SM4_private(
        normalizedViewDirection,
        reflected_view_vec,
        slm_uncompressed);

    // ====== SINGLE LIGHT FOLLOWING CAMERA ======
    float unchartedSunFactor = 3.0f;

    // Light direction follows camera view direction - light comes from where camera is looking
    float3 L_main = normalize(CameraPos - input.worldPosition);  // Light direction from camera to object

    // Single main light
    float3 lightCol_main = get_sun_colour() * unchartedSunFactor;

    float3 combined_dir_light = standard_lighting_model_directional_light_SM4_private(
        lightCol_main, L_main, normalizedViewDirection, reflected_view_vec, slm_uncompressed);

    // Combine environment + directional light
    float3 hdr_linear_col = env_light + combined_dir_light;

    // Apply global constant light colour
    hdr_linear_col *= Constant_LightColour;

    // Tone-map (AE 7.0 uses Uncharted2)
    float3 ldr_linear_col = saturate(Uncharted2ToneMapping(hdr_linear_col));

    // Use main light direction for emissive calculations
    float3 emissiveColour = GetEmissiveColour(input.tex, material.maskValue, L_main, material.pixelNormal);

    // Combine all colours
    float3 color = ldr_linear_col + emissiveColour;

    MainPixelOutput output;
    output.Colour = float4(_gamma(color), 1.0f);
    output.SelectionMask = SelectionMaskEnabled
        ? float4(1.0f, 1.0f, 1.0f, 1.0f)
        : float4(0.0f, 0.0f, 0.0f, 0.0f);
    return output;
}

float4 EmissiveLayerPixelShader(in PixelInputType input, bool bIsFrontFace : SV_IsFrontFace) : SV_TARGET0
{
    GBufferMaterial material = GetMaterial(input);
    float3 normlizedViewDirection = normalize(input.viewDirection);
    float3 emissiveColour = GetEmissiveColour(input.tex, material.maskValue, normlizedViewDirection, material.pixelNormal);

    if (UseAlpha == 1)
        alpha_test(material.diffuse.a);

    return float4(emissiveColour, 1);
}

technique BasicColorDrawing
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 DefaultPixelShader();
    }
};

technique GlowDrawing
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 EmissiveLayerPixelShader();
    }
};
