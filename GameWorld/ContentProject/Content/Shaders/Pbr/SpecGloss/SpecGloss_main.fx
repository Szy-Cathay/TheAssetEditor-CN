#include "../Helpers/CASpecglosshelper.hlsli"
#include "../helpers/constants.hlsli"
#include "../Helpers/tone_mapping.hlsli"

#include "../Shared/const_layout.hlsli"
#include "../Shared/MainVertexShader.hlsli"

#include "../TextureSamplers.hlsli"
#include "../inputlayouts.hlsli"
#include "../Capabilites/Tint.hlsli"

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
    float3 rotatedNormalizedLightDirection = normalize(mul(light_Direction_Constant, (float3x3) DirLightTransform));

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

    const float directlightIntensity = 3.0f;
    const float3 diretLightColor = float3(1, 1, 1); // TODO: make cpu side constant    
    
    // Light the pixel...
    float3 hdr_linear_col = standard_lighting_model_directional_light(diretLightColor * directlightIntensity, rotatedNormalizedLightDirection, normalizedViewDirection, standard_mat);
    
    //  Tone-map the pixel...            
    float3 ldr_linear_col = saturate(tone_map_linear_hdr_pixel_value(hdr_linear_col*exposure));        
    
    MainPixelOutput output;
    output.Colour = float4(_gamma(ldr_linear_col), 1.0f);
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
