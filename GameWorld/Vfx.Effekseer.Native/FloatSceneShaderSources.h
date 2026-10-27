#pragma once
#include <array>
#include <string>

// One source for build-time bytecode. These are the original runtime compile inputs.
struct FloatSceneShaderSource
{
    const char* Name;
    const char* Profile;
    std::string Source;
};
static constexpr unsigned FloatSceneShaderCompileFlags = 0;
static constexpr const char* FloatSceneShaderSourceName = "AECN VFX colour transfer";
inline std::array<FloatSceneShaderSource, 8> FloatSceneShaderSources()
{
            const char* vs = "float4 main(uint i:SV_VertexID):SV_Position { float2 p=float2((i<<1)&2,i&2); return float4(p*float2(2,-2)+float2(-1,1),0,1); }";
            // Shipped hdr_to_screen transfer and separable default LUT, applied after particle blending.
            const std::string transfer = R"(
                cbuffer Display : register(b0) { float4 display; float4 mode; float4 bloom; }
                Texture2D<float4> halo : register(t2);
                SamplerState linearClamp : register(s0);
                static const float lut[32] = { 0,8,16,24,33,41,49,57,65,73,82,91,99,107,115,123,
                    132,140,148,156,164,173,182,190,198,206,214,222,231,239,247,255 };
                float3 ToSrgb(float3 c) { return lerp(12.92*c, 1.055*pow(max(c,0),1/2.4)-.055, step(.0031308,c)); }
                float3 FromSrgb(float3 c) { return lerp(c/12.92, pow((c+.055)/1.055,2.4), step(.04045,c)); }
                float WhiteReciprocal() { return display.z == 0 ? 0 : 1 / (1 / display.z - .999); }
                float Lookup(float c)
                {
                    float p = clamp(c*32-.5,0,31); uint i = (uint)p;
                    return lerp(lut[i], lut[min(i+1,31)], p-i) / 255;
                }
                float InverseLookup(float c)
                {
                    if (c <= 0 || c >= 1) return saturate(c);
                    float value = c*255;
                    [unroll] for (uint i=0;i<31;i++)
                        if (value <= lut[i+1]) return (i+.5+(value-lut[i])/(lut[i+1]-lut[i]))/32;
                    return 1;
                }
                float3 Encode(float3 rgb)
                {
                    rgb = max(rgb,0);
                    float exposure = display.y*display.y;
                    float x = dot(rgb,float3(.2126,.7152,.0722))*exposure;
                    float3 encoded = pow(saturate(rgb*exposure*(1+x*WhiteReciprocal())/(1+x)),1/2.2);
                    if (display.w != 0) encoded = (1 + display.w) * (encoded - .5) + .5;
                    return float3(Lookup(encoded.r),Lookup(encoded.g),Lookup(encoded.b));
                }
                float3 Decode(float3 encoded)
                {
                    float3 corrected = float3(InverseLookup(encoded.r),InverseLookup(encoded.g),InverseLookup(encoded.b));
                    if (display.w != 0) corrected = max((corrected - .5) / (1 + display.w) + .5, 0);
                    float3 c = pow(corrected,2.2);
                    float y = dot(c,float3(.2126,.7152,.0722)), w = WhiteReciprocal();
                    float x = w > 0 ? 2*y/max(sqrt((1-y)*(1-y)+4*w*y)+1-y,1e-8) : y/max(1-y,1e-6);
                    return c*(x/max(y,1e-8))/(display.y*display.y);
                }
                float3 BackgroundRadiance(float4 background)
                {
                    return Decode(mode.y > .5 ? ToSrgb(background.rgb) : background.rgb);
                }
                float3 Bright(float3 colour)
                {
                    colour = max(colour,0);
                    return colour * saturate(dot(colour,float3(.2126,.7152,.0722))-max(bloom.x,0)) * bloom.y;
                }
                float4 Extract(float4 value, float4 background)
                {
                    // Only the VFX increment blooms; the existing model/grid display is not a new light source.
                    float3 before = f16tof32(f32tof16(BackgroundRadiance(background)));
                    return float4(max(Bright(value.rgb)-Bright(before),0),0);
                }
                float4 Transfer(float4 value, float4 background, float2 uv)
                {
                    if (display.x < .5) return value;
                    float3 linearBackground = BackgroundRadiance(background);
                    if (mode.x < .5) return float4(linearBackground,background.a);
                    float3 glow = bloom.y > 0 ? halo.SampleLevel(linearClamp,uv,0).rgb : 0;
                    // Preserve untouched display pixels exactly, including prior PBR tonemapping and alpha.
                    if (all(glow == 0) && all(value.rgb == f16tof32(f32tof16(linearBackground))) && value.a == f16tof32(f32tof16(background.a)))
                        return background;
                    float3 encoded = Encode(value.rgb + glow);
                    return float4(mode.y > .5 ? FromSrgb(encoded) : encoded,value.a);
                }
            )";
            const auto ps = transfer + "Texture2D<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position):SV_Target { int3 q=int3(int2(p.xy),0); return Transfer(source.Load(q),background.Load(q),p.xy*bloom.zw); }";
            const auto ms = transfer + "Texture2DMS<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position,uint sample:SV_SampleIndex):SV_Target { int2 q=int2(p.xy); return Transfer(source.Load(q,sample),background.Load(q,sample),p.xy*bloom.zw); }";
            const auto extract = transfer + "Texture2D<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position):SV_Target { int3 q=int3(int2(p.xy),0); return Extract(source.Load(q),background.Load(q)); }";
            const auto extractMS = transfer + R"(
                Texture2DMS<float4> source:register(t0), background:register(t1);
                float4 main(float4 p:SV_Position):SV_Target {
                    uint width,height,samples; source.GetDimensions(width,height,samples);
                    float4 result=0;
                    for(uint i=0;i<samples;i++) result += Extract(source.Load(int2(p.xy),i),background.Load(int2(p.xy),i));
                    return result/samples;
                })";
            const std::string filter = R"(
                cbuffer Display : register(b0) { float4 display; float4 mode; float4 bloom; }
                Texture2D<float4> inputA:register(t0), inputB:register(t1);
                SamplerState linearClamp:register(s0);
            )";
            const auto downsample = filter + "float4 main(float4 p:SV_Position):SV_Target { return inputA.SampleLevel(linearClamp,p.xy*bloom.zw,0); }";
            const auto upsample = filter + "float4 main(float4 p:SV_Position):SV_Target { float2 uv=p.xy*bloom.zw; return inputA.SampleLevel(linearClamp,uv,0)+inputB.SampleLevel(linearClamp,uv,0); }";
            const auto blur = filter + R"(
                static const float weights[11]={.000003,.000229,.005977,.060598,.241730,.382925,.241730,.060598,.005977,.000229,.000003};
                float4 main(float4 p:SV_Position):SV_Target {
                    float4 result=0;
                    [unroll] for(int i=0;i<11;i++) result += inputA.SampleLevel(linearClamp,p.xy*bloom.zw+(i-5)*mode.zw,0)*weights[i];
                    return result;
                })";
    return {{
        {"Vertex", "vs_5_0", vs}, {"Single", "ps_5_0", ps}, {"Multisample", "ps_5_0", ms},
        {"Extract", "ps_5_0", extract}, {"ExtractMultisample", "ps_5_0", extractMS},
        {"Downsample", "ps_5_0", downsample}, {"Blur", "ps_5_0", blur}, {"Upsample", "ps_5_0", upsample}
    }};
}
