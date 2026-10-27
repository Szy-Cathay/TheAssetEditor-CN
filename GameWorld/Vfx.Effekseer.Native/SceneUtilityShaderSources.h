#pragma once
#include <array>
#include <string>

struct SceneUtilityShaderSource
{
    const char* Name;
    const char* Profile;
    const char* SourceName;
    std::string Source;
};
static constexpr unsigned SceneUtilityShaderCompileFlags = 0;
inline std::array<SceneUtilityShaderSource, 3> SceneUtilityShaderSources()
{
    return {{
        {"DepthResolve", "cs_5_0", "AECN scene depth resolve", R"AECN(Texture2DMS<float> inputDepth : register(t0); RWTexture2D<float> outputDepth : register(u0);[numthreads(16,16,1)] void main(uint3 p:SV_DispatchThreadID) { uint w,h,n; inputDepth.GetDimensions(w,h,n);if(p.x>=w||p.y>=h)return; float z=1; for(uint s=0;s<n;s++)z=min(z,inputDepth.Load(p.xy,s)); outputDepth[p.xy]=z; })AECN"},
        {"DistortionVertex", "vs_5_0", "AECN weighted distortion resolve", R"AECN(float4 main(uint i:SV_VertexID):SV_Position { float2 p=float2((i<<1)&2,i&2); return float4(p*float2(2,-2)+float2(-1,1),0,1); })AECN"},
        {"DistortionResolve", "ps_5_0", "AECN weighted distortion resolve", R"AECN(
                Texture2D<float4> vectors : register(t0), transmittance : register(t1), background : register(t2);
                SamplerState linearClamp : register(s0);
                float4 main(float4 position : SV_Position) : SV_Target
                {
                    uint width, height; vectors.GetDimensions(width, height);
                    float2 inverseSize = 1.0 / float2(width, height);
                    float2 uv = position.xy * inverseSize;
                    // Captured native resolve samples the distortion buffer half a texel before the background.
                    float2 bufferUV = uv - 0.5 * inverseSize;
                    float3 accumulated = vectors.SampleLevel(linearClamp, bufferUV, 0).rgb;
                    // Native mode 1 squares 1-T even when floating transmittance leaves [0,1].
                    float visibility = 1 - transmittance.SampleLevel(linearClamp, bufferUV, 0).r;
                    if (visibility == 0 || accumulated.z <= 0) discard;
                    float2 offset = 0.02 * accumulated.xy / (accumulated.z < 0.001 ? 1 : accumulated.z)
                        * visibility * visibility;
                    if (all(offset == 0)) discard;
                    return float4(background.SampleLevel(linearClamp, uv + offset, 0).rgb, 0);
                }
            )AECN"},
    }};
}
