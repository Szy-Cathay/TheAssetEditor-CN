#ifndef FLAG_WIND_HLSLI
#define FLAG_WIND_HLSLI

#include "../TextureSamplers.hlsli"

bool FlagWindEnabled = false;
float2 FlagWindVector = float2(0, 0);
float FlagWindTimeSeconds = 0;
float FlagWindStrength = 0.1;
float FlagNormalTextureRotate = 0;
float FlagVertexWindScale = 1;
Texture2D<float4> FlagClothNormal1;
Texture2D<float4> FlagClothNormal2;

void DoNativeFlagSkinning(VertexInputType input, out float4 position,
    out float3 normal, out float3 tangent, out float3 binormal)
{
    float4 weights = input.Weights;
    uint4 indices = input.BoneIndices;
    if (Animation_WeightCount == 2)
        weights.y = 1 - weights.x;
    else
    {
        // Shipping weighted4 VS reconstructs the last weight and suppresses unused bone fetches.
        float originalSum = weights.x + weights.y + weights.z;
        weights.y = weights.y > (1.0 / 256.0) ? weights.y : 0;
        weights.z = weights.z > (1.0 / 256.0) ? weights.z : 0;
        weights.w = originalSum < (255.0 / 256.0) ? 1 - originalSum : 0;
        if (input.Weights.y <= (1.0 / 256.0))
            indices.yzw = indices.xxx;
    }
    float3 sourceNormal = normalize(input.normal);
    float3 sourceTangent = normalize(input.tangent);
    float3 sourceBinormal = normalize(input.binormal);
    position = 0;
    normal = 0;
    tangent = 0;
    binormal = 0;
    [unroll] for (int bone = 0; bone < Animation_WeightCount; bone++)
    {
        float4x4 transform = Animation_Tranforms[indices[bone]];
        position.xyz += weights[bone] * mul(float4(input.position.xyz, 1), transform).xyz;
        normal += weights[bone] * mul(sourceNormal, (float3x3)transform);
        tangent += weights[bone] * mul(sourceTangent, (float3x3)transform);
        binormal += weights[bone] * mul(sourceBinormal, (float3x3)transform);
    }
    position.w = 1;
}

float FlagWindMagnitude()
{
    // Zero is the editor's explicit no-wind state; native normalize(0) is undefined.
    if (dot(FlagWindVector, FlagWindVector) == 0)
        return 0;
    return clamp(abs(FlagWindVector.x + 0.001) + abs(FlagWindVector.y), 0, 0.26);
}

float3 ApplyNativeFlagWind(float3 position, float4 vertexColour)
{
    [branch] if (!FlagWindEnabled || dot(FlagWindVector, FlagWindVector) == 0)
        return position;

    // Native NODE_FUNCTION_SQR is sqrt, confirmed against the shipping vertex shader.
    float wind = sqrt(FlagWindMagnitude());
    float phase = -6 * wind * (FlagWindTimeSeconds - 0.78 * vertexColour.b);
    float slowX = 0.5 * (2 + sin(0.5 * (World[3].x + phase)));
    float slowY = 0.5 * (2 + sin(position.y + phase - 0.2));
    float detail = 0.5 + sin(3 * (World[3].z + 2 * vertexColour.g + phase));
    float2 direction = normalize(FlagWindVector);
    float3 axis = cross(float3(direction.x, 0, direction.y), World[0].xyz);
    float amplitude = FlagVertexWindScale * FlagWindStrength * 0.48 * wind * slowX * slowY * detail
        * pow(vertexColour.a, 1.1);
    return position + axis * amplitude;
}

float3 DecodeNativeFlagNormal(float4 value)
{
    float2 xy = (value.ag + (0.5 / 255.0)) * 2 - 1;
    return float3(xy, sqrt(max(1 - dot(xy, xy), 0)));
}

float3 FlagNormalToWorld(float3 normal, PixelInputType input)
{
    float3x3 basis = float3x3(normalize(input.tangent), normalize(input.binormal), normalize(input.normal));
    return normalize(mul(normal, basis));
}

float3 GetNativeFlagPixelNormal(PixelInputType input)
{
    float magnitude = FlagWindMagnitude();
    float wind = sqrt(magnitude);
    float clock = wind * (FlagWindTimeSeconds - 0.78 * input.vertexColour.b);
    float phase = -6 * clock;
    float slowX = 0.5 * (2 + sin(0.5 * (0.5 * input.worldPosition.x + phase)));
    float slowY = 0.5 * (2 + sin(input.worldPosition.y + phase - 0.2));
    float detail = 0.5 + sin(3 * (input.worldPosition.z + phase));
    float wave = 0.48 * wind * slowX * slowY * detail;
    float sine, cosine;
    sincos(FlagNormalTextureRotate, sine, cosine);
    float2 uv = float2(dot(input.tex, float2(cosine, sine)), dot(input.tex, float2(-sine, cosine)));
    float2 uv1 = 2 * (uv + float2(0.05 * FlagWindStrength * wave, 0));
    float2 uv2 = uv - float2(0.3 * FlagWindStrength * clock, 0);
    float weight = 1.4 * (1 - magnitude) * input.vertexColour.r;
    float3 cloth1 = DecodeNativeFlagNormal(FlagClothNormal1.Sample(s_normal, uv1)) * float3(weight, weight, 1);
    float3 cloth2 = DecodeNativeFlagNormal(FlagClothNormal2.Sample(s_normal, uv2)) * float3(weight, weight, 1);
    float3 baseNormal = FlagNormalToWorld(DecodeNativeFlagNormal(NormalTexture.Sample(s_normal, input.tex)), input);
    return normalize(FlagNormalToWorld(cloth1, input) + FlagNormalToWorld(cloth2, input)
        + baseNormal * float3(2, 2, 1));
}

#endif
