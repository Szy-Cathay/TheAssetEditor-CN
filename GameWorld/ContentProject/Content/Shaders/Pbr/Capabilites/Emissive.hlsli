#include "../Helpers/GradientSampling.hlsli"
#include "../TextureSamplers.hlsli"
#include "../helpers/MathFunctions.hlsli"

// Input parameters
bool                    CapabilityFlag_ApplyEmissive = false;

Texture2D<float4>       Emissive_Texture;
bool                    Emissive_UseTexture = true;
Texture2D<float4>        Emissive_DistortionTexture;
bool                    Emissive_UseDistortionTexture = false;
float                   MaterialTimeSeconds = 0;
float2                  Emissive_Direction = float2(0, 0);
float                   Emissive_Speed = 0;
float                   Emissive_DistortStrength = 0;
float                   Emissive_PulseSpeed = 0;
float                   Emissive_PulseStrength = 0;

float3                  Emissive_Tint = float3(1, 1, 1);
float                   Emissive_Strength = 1;
float2                  Emissive_Tiling = float2(1, 1);
float3                  Emissive_GradientColours[4];
float                   Emissive_GradientTimes[4];
float                   Emissive_FresnelStrength = 1;

float3 SampleEmissiveGradient(float position)
{
    if (position <= Emissive_GradientTimes[0]) return Emissive_GradientColours[0];
    if (position > Emissive_GradientTimes[3]) return Emissive_GradientColours[3];
    // Native character materials keep a small denominator bias between gradient stops.
    [unroll] for (int index = 0; index < 3; index++)
        if (position <= Emissive_GradientTimes[index + 1])
            return lerp(Emissive_GradientColours[index], Emissive_GradientColours[index + 1],
                (position - Emissive_GradientTimes[index]) /
                (Emissive_GradientTimes[index + 1] - Emissive_GradientTimes[index] + 0.001));
    return Emissive_GradientColours[3];
}

float3 GetEmissiveColour(float2 uv, float4 maskValue, float3 normalizedViewDirection, float3 normalizedNormal)
{
    if (CapabilityFlag_ApplyEmissive == false)
        return float3(0, 0, 0);
    
    float2 distortion = float2(0, 0);
    if (Emissive_UseDistortionTexture)
        distortion = Emissive_DistortionTexture.Sample(SampleType, frac(uv)).rb * Emissive_DistortStrength;
    float2 texCord = frac((uv + distortion + MaterialTimeSeconds * Emissive_Speed * Emissive_Direction) * Emissive_Tiling);

    float emissiveTexture = 1;
    if (Emissive_UseTexture)
        emissiveTexture = Emissive_Texture.Sample(SampleType, texCord).r;

    float facing = 1 - saturate(dot(normalizedViewDirection, normalizedNormal));
    float pulse = (sin(MaterialTimeSeconds * Emissive_PulseSpeed) + 0.99) * Emissive_PulseStrength * 0.1;
    float gradientPosition = (emissiveTexture + pulse + Emissive_FresnelStrength * facing * facing) * maskValue.a;
    float3 gradientColour = SampleEmissiveGradient(gradientPosition);

    float3 emissiveColour = gradientColour * Emissive_Tint * Emissive_Strength;
    return emissiveColour;
}

float3 ApplyEmissiveMaterial(inout GBufferMaterial material, float2 uv, float3 viewDirection)
{
    if (!CapabilityFlag_ApplyEmissive) return float3(0, 0, 0);
    float3 emission = GetEmissiveColour(uv, material.maskValue, viewDirection, material.pixelNormal);
    float scale = max(1, max(emission.r, max(emission.g, emission.b)));
    float luminance = dot(emission, float3(0.2126, 0.7152, 0.0722));

    // Character shaders encode emission through the modified G-buffer material.
    material.diffuse.rgb = saturate(1 - (1 - material.diffuse.rgb) * (1 - emission / scale));
    material.metalness = lerp(material.metalness, 0, saturate(luminance));
    material.roughness = lerp(material.roughness, 1, saturate(luminance));
    float encodedEmission = floor(saturate(scale * luminance * 0.02) * 1023 + 0.5) / 1023;
    float encodedMetalness = floor(saturate(material.metalness) * 255 + 0.5) / 255;
    return material.diffuse.rgb * (1 - encodedMetalness) * encodedEmission * 255;
}

