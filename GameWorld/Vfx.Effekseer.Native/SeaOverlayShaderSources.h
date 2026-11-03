#pragma once
#include "NativeMboitShaderSources.h"

// SEA_OVERLAY owns colour/normal accumulation and multiplicative revealage.
inline std::string SeaOverlayVertexSource(const std::string& original)
{
    auto source = HalfSceneVertexSource(original);
    BulkShaderReplace(source,"Output.AecnMomentDepth = 2 * (logDistance / logRange) - 1;",
        "Output.AecnMomentDepth = -cameraPos.z;");
    return source;
}
inline std::string SeaOverlayPixelSource(const std::string& original, bool multisample = false)
{
    auto material = HalfSceneMaterialSource(original);
    BulkShaderReplace(material,"float3 emission = max(sampled.rgb - alpha, 0);",
        "float3 emission = saturate(sampled.rgb - alpha);");
    BulkShaderReplace(material,"\n        opacity *= fade;",R"(
        if (parameters.z > 1.5 && particleInfo.w > .5)
        {
            // Native crush also rescales emissive RGB before multiplying its crushed shape.
            float originalShape = saturate(dot(emission,float3(.2126,.7152,.0722))*vertexAlpha);
            emissive *= originalShape != 0 ? opacity/originalShape : 1;
        }
        opacity *= fade;)" );
    auto source = material + R"(
struct AecnSeaOutput
{
    float4 colour : SV_Target0;
    float4 normal : SV_Target1;
    float revealage : SV_Target2;
};
float3 AecnSeaNormal(const PS_Input Input)
{
    float3 tangent = float3(0, 0, 1);
    if (Input.AecnDynamic0.w > .5)
    {
        float4 a = shadingNormal_texture.Sample(shadingNormal_sampler, Input.UV1);
        float4 b = shadingNormal_texture.Sample(shadingNormal_sampler, Input.UV1 + Input.CustomData1.xy);
        float2 xy0 = (a.ag + .00196078431372549) * 2 - 1;
        float2 xy1 = (b.ag + .00196078431372549) * 2 - 1;
        tangent = normalize(lerp(float3(xy0, sqrt(max(0, 1-dot(xy0,xy0)))),
            float3(xy1, sqrt(max(0, 1-dot(xy1,xy1)))), Input.CustomData1.z));
    }
    tangent.xy *= -Input.AecnDynamic2.xy;
    float4 q = Input.CustomData2;
    return normalize(tangent + 2 * cross(q.xyz, cross(q.xyz, tangent) + q.w*tangent));
}
AecnSeaOutput main(const PS_Input Input)
{
    float4 covered = AecnHalfMaterial(Input,AecnHalfSource.x>.5?1:0,AecnHalfSource.y);
    float4 emission = 0;
    if (AecnHalfSource.x>.5 && AecnHalfSource.z>0)
        emission = AecnHalfMaterial(Input,2,AecnHalfSource.z);
    AecnHalfFragment fragment;
    fragment.radiance = covered.rgb*covered.a + emission.rgb*emission.a*saturate(AecnHalfSource.z);
    fragment.coverage = covered.a;
    float depth = saturate((Input.AecnMomentDepth-AecnHalfPlanes.x)/(AecnHalfPlanes.y-AecnHalfPlanes.x));
    float z = 1-.99*depth;
    float weight = clamp(min(10000*z*z*z,1)*fragment.coverage,.001,150);
    float absorption = fragment.coverage*weight;
    AecnSeaOutput result;
    // The extra covered-opacity factor is present in the shipped sea PS, including emission.
    result.colour = float4(fragment.radiance*absorption,absorption);
    result.normal = float4(AecnSeaNormal(Input)*absorption,absorption);
    result.revealage = fragment.coverage;
    return result;
}
)";
    return multisample ? HalfSceneSampleInputs(std::move(source)) : source;
}
inline std::string SeaOverlayComposePixelSource(bool multisample = false)
{
    std::string source = HalfSceneParameterSource() + R"(
Texture2D<float4> seaColour : register(t0);
Texture2D<float> seaRevealage : register(t1);
Texture2D<float> receiverDepth : register(t2);
float4 main(float4 pixel : SV_Position) : SV_Target0
{
    int3 coordinate = int3(int2(pixel.xy),0);
    if (AecnSeaReceiver.x != 1) return 0;
    float2 raster = floor(pixel.xy)+.5;
    float2 ndc = raster*AecnHalfWarp.zw*float2(2,-2)+float2(-1,1);
    float4 nearWorld = mul(AecnSeaInverseViewProjection,float4(ndc,0,1));
    float4 farWorld = mul(AecnSeaInverseViewProjection,float4(ndc,1,1));
    float nearDistance = nearWorld.y-AecnSeaReceiver.y*nearWorld.w;
    float farDistance = farWorld.y-AecnSeaReceiver.y*farWorld.w;
    float planeDepth = nearDistance/(nearDistance-farDistance);
    float sceneDepth = receiverDepth.Load(coordinate);
    // UNORM storage rounds by half of its fixed quantum, independently of float ULP.
    // One additional float step covers conversion/reconstruction at that stored upper bound.
    float storedUpperDepth = sceneDepth+AecnSeaReceiver.z;
    float followingDepth = asfloat(asuint(storedUpperDepth)+1);
    if (!isfinite(planeDepth) || planeDepth<0 || planeDepth>1 || planeDepth>followingDepth) return 0;
    float4 colour = seaColour.Load(coordinate);
    float revealage = seaRevealage.Load(coordinate);
    float divisor = colour.a < .001 ? 1 : colour.a;
    float opacity = 1-revealage;
    // battle_water adds the resolved overlay; it does not attenuate the water colour.
    // Preview uses the explicit flat receiver and sp_vfx_overlay_multiplier=1.
    return float4(colour.rgb/divisor*opacity,opacity);
}
)";
    if (multisample)
    {
        BulkShaderReplace(source,"Texture2D<float4> seaColour","Texture2DMS<float4,4> seaColour");
        BulkShaderReplace(source,"Texture2D<float> seaRevealage","Texture2DMS<float,4> seaRevealage");
        BulkShaderReplace(source,"Texture2D<float> receiverDepth","Texture2DMS<float,4> receiverDepth");
        BulkShaderReplace(source,"float4 main(float4 pixel : SV_Position) : SV_Target0",
            "float4 main(float4 pixel : SV_Position, uint sampleIndex : SV_SampleIndex) : SV_Target0");
        BulkShaderReplace(source,"seaColour.Load(coordinate)","seaColour.Load(coordinate.xy,sampleIndex)");
        BulkShaderReplace(source,"seaRevealage.Load(coordinate)","seaRevealage.Load(coordinate.xy,sampleIndex)");
        BulkShaderReplace(source,"receiverDepth.Load(coordinate)","receiverDepth.Load(coordinate.xy,sampleIndex)");
        BulkShaderReplace(source,"float2 raster = floor(pixel.xy)+.5;",
            "float2 raster = floor(pixel.xy)+.5+receiverDepth.GetSamplePosition(sampleIndex);");
    }
    return source;
}
