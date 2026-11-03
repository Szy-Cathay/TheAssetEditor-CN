#pragma once

inline std::string MeshColourDistortionHeader(bool vertexStage = false)
{
    if (vertexStage) return "float4 AecnMeshColourDistortion(float2 uv) { return float4(0,0,0,0); }\n";
    return R"(
Texture2D aecnMeshColourDistortion : register(t8);
SamplerState aecnMeshColourDistortionSampler : register(s8);
float4 AecnMeshColourDistortion(float2 uv) { return aecnMeshColourDistortion.Sample(aecnMeshColourDistortionSampler,uv); }
)";
}
