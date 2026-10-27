#pragma once
#include "BulkSpriteShaderSources.h"

// Native seven-moment arithmetic; material/raster evaluation remains separate.
inline std::string NativeMboitKernelSource()
{
    return R"AECN_MBOIT(
float2 NativeMboitOpacity(float coveredOpacity, float emissionShape)
{
    // These are actual immediate float bits, not six-decimal disassembly text.
    float u = min(max(coveredOpacity + min(emissionShape, 1.0) * asfloat(0x3a03126e)
        + asfloat(0x3a83126f), asfloat(0x3a83126f)), asfloat(0x3f7fbe77));
    float tau = -log2(1.0 - u) * asfloat(0x3f317218);
    return float2(u, tau);
}

float NativeMboitDepth(float positiveViewZ, float nearPlane, float farPlane)
{
    float numerator = log2(positiveViewZ / nearPlane) * asfloat(0x3f317218);
    float denominator = log2(farPlane / nearPlane) * asfloat(0x3f317218);
    return 2.0 * (numerator / denominator) - 1.0;
}

struct MboitMoments
{
    float b0 : SV_Target0;
    float4 b3456 : SV_Target1;
    float2 b12 : SV_Target2;
};

MboitMoments NativeMboitGenerate(float coverage, float emission, float z, float killThreshold)
{
    float2 absorption = NativeMboitOpacity(coverage, emission);
    clip(absorption.y - killThreshold);
    float z2 = z*z;
    float z4 = z2*z2;
    MboitMoments o;
    o.b0 = absorption.y;
    o.b12 = absorption.y * float2(z, z2);
    o.b3456 = absorption.y * float4(z2*z, z4, z4*z, z2*z4);
    return o;
}

// The cubic root solver uses the same analytic form and atan polynomial as PScc0e.
// Named-variable expansion can differ in final float rounding/FMA from the DXBC.
// Recompile-and-replay comparisons remain mandatory before production use.
float3 NativeMboitCubicRoots(float3 monic)
{
    // monic = constant, x, x^2; x^3 coefficient is one.
    float2 third = monic.yz * (1.0 / 3.0);
    float pThird = monic.y * (1.0 / 3.0) - third.y * third.y;
    float cMinusAB9 = monic.x - third.x * third.y;
    float ac3MinusB29 = third.y * monic.x - third.x * third.x;
    float discriminant = 4.0 * pThird * ac3MinusB29 - cMinusAB9 * cMinusAB9;
    float q = monic.z * pThird * (-2.0 / 3.0) + cMinusAB9;
    float d = sqrt(discriminant);
    float ratio = min(abs(q), d) / max(abs(q), d);
    float square = ratio * ratio;
    float polynomial = ((((0.0208351 * square - 0.0851330) * square + 0.1801410)
        * square - 0.3302995) * square + 0.9998660);
    float angle = polynomial * ratio;
    if (abs(q) < d) angle = 1.5707963267948966 - angle;
    if (q > 0) angle = 3.1415926535897932 - angle;
    angle *= 1.0 / 3.0;
    float s, c;
    sincos(angle, s, c);
    float3 roots = float3(c, -0.5*c - 0.8660254037844386*s, -0.5*c + 0.8660254037844386*s);
    return 2.0 * sqrt(-pThird) * roots - third.y;
}

float NativeMboitTransmittance(float b0, float2 b12, float4 b3456, float queryZ,
    float momentBias, float overestimation)
{
    clip(b0 - asfloat(0x3a832338));
    float3 odd = float3(b12.x, b3456.x, b3456.z) / b0;
    float3 even = float3(b12.y, b3456.y, b3456.w) / b0;
    odd = mad(-momentBias, odd, odd);
    even = mad(momentBias, float3(0.48, 0.451, 0.45) - even, even);
    float m1=odd.x, m2=even.x, m3=odd.y, m4=even.y, m5=odd.z, m6=even.z;
    // H[i,j] = m[i+j], m0=1. LDL decomposition is PScc0e instructions 283..296.
    float d1 = m2 - m1*m1;
    float h21 = m3 - m1*m2;
    float h31 = m4 - m1*m3;
    float l21 = h21 / d1;
    float l31 = h31 / d1;
    float d2 = m4 - m2*m2 - h21*l21;
    float h32 = m5 - m2*m3 - h21*l31;
    float l32 = h32 / d2;
    float d3 = m6 - m3*m3 - h31*l31 - h32*l32;
    float y1 = queryZ - m1;
    float y2 = queryZ*queryZ - (m2 + l21*y1);
    float y3 = queryZ*queryZ*queryZ - (m3 + l31*y1 + l32*y2);
    float c3 = y3 / d3;
    float c2 = y2 / d2 - l32*c3;
    float c1 = y1 / d1 - l21*c2 - l31*c3;
    float c0 = 1.0 - dot(float3(m1,m2,m3), float3(c1,c2,c3));
    float3 roots = NativeMboitCubicRoots(float3(c0,c1,c2)/c3);
    float3 stepValues = queryZ >= roots;
    // Interpolate [overestimation, step0, step1, step2] at [queryZ, root0, root1, root2].
    float d01 = (stepValues.x-overestimation)/(roots.x-queryZ);
    float d12 = (stepValues.y-stepValues.x)/(roots.y-roots.x);
    float d23 = (stepValues.z-stepValues.y)/(roots.z-roots.y);
    float d012 = (d12-d01)/(roots.y-queryZ);
    float d123 = (d23-d12)/(roots.z-roots.x);
    float p3 = (d123-d012)/(roots.z-queryZ);
    float partial2 = d012 - p3*roots.y;
    float partial1 = d01 - partial2*roots.x;
    float p2 = partial2 - p3*roots.x;
    float4 polynomial = float4(overestimation - partial1*queryZ,
        partial1 - p2*queryZ, p2 - p3*queryZ, p3);
    float cdf = dot(polynomial, float4(1,m1,m2,m3));
    return min(exp2(-b0*cdf*asfloat(0x3fb8aa3b)),1.0);
}

float4 NativeMboitColour(float3 combinedPremultipliedRadiance, float coverage, float emission,
    float transmittance, float killThreshold)
{
    float2 absorption = NativeMboitOpacity(coverage, emission);
    clip(absorption.y-killThreshold);
    return float4(combinedPremultipliedRadiance, absorption.x) * transmittance;
}

// Actual PS1bf1d sampled all four accumulators before this arithmetic.
// Original nofilter composition uses linear colour upsampling, not the filtered depth-aware candidate.
float4 NativeMboitCompose(float b0, float4 gameFull, float4 gameHalf, float4 uiFull, float4 uiHalf)
{
    float totalTransmittance = exp2(-b0*asfloat(0x3fb8aa3b));
    float4 game = gameFull + gameHalf;
    float4 ui = uiFull + uiHalf;
    float scale = (1.0-totalTransmittance)/(game.a+ui.a);
    scale = isfinite(scale) ? scale : 0;
    return float4(game.rgb*scale,totalTransmittance);
}

)AECN_MBOIT";
}

inline std::string HalfSceneParameterSource()
{
    return R"(
cbuffer AecnHalfScene : register(b1)
{
    float4 AecnHalfPlanes; // near, far, moment bias, overestimation
    float4 AecnHalfSource; // premultiplied, covered multiplier, emission multiplier, absorption kill
    float4 AecnHalfSurface; // half allocation width/height, full allocation width/height
    float4 AecnHalfWarp; // moment coordinate scale, reserved, reciprocal full allocation
};
)";
}
inline std::string HalfSceneVertexSource(std::string text)
{
    text = BulkSpriteVertexSource(std::move(text));
    BulkShaderReplace(text, "float AecnSoftViewZ : TEXCOORD10;", "float AecnSoftViewZ : TEXCOORD10;\n float AecnMomentDepth : TEXCOORD11;");
    BulkShaderReplace(text, "Output.AecnSoftViewZ = -cameraPos.z;", R"(
        Output.AecnSoftViewZ = -cameraPos.z;
        float logDistance = log2(-cameraPos.z / AecnHalfPlanes.x) * asfloat(0x3f317218);
        float logRange = log2(AecnHalfPlanes.y / AecnHalfPlanes.x) * asfloat(0x3f317218);
        Output.AecnMomentDepth = 2 * (logDistance / logRange) - 1;
)");
    return HalfSceneParameterSource() + text;
}
inline std::string HalfSceneMaterialSource(std::string text)
{
    // This independent route owns a native trilinear sampler; retain hardware fractional-LOD precision.
    for (const auto name : {"source", "shadingNormal", "gradient"})
    {
        const std::string from=std::string("AecnTrilinear(")+name+"_texture, "+name+"_sampler,";
        const std::string to=std::string(name)+"_texture.Sample("+name+"_sampler,";
        size_t at=0, count=0;
        while ((at=text.find(from,at))!=std::string::npos) { text.replace(at,from.size(),to); at+=to.size(); ++count; }
        if (!count) throw std::runtime_error("Missing native half-scene texture sampling contract");
    }
    BulkShaderFields(text, "PS_Input", true); BulkShaderGlobals(text, false);
    BulkShaderReplace(text, "float AecnSoftViewZ : TEXCOORD10;", "float AecnSoftViewZ : TEXCOORD10;\n float AecnMomentDepth : TEXCOORD11;");
    BulkShaderReplace(text, "float4 main( const PS_Input Input ) : SV_Target\n{",
        "float4 AecnHalfMaterial(const PS_Input Input, float sourceMode, float sourceScale)\n{"
        + BulkShaderAssignments() + "\n parameters.z = sourceMode; parameters.x = sourceScale;\n");
    // Native absorption has its own floor/kill; the legacy C/E opacity discard must not kill it first.
    BulkShaderReplace(text, "if(opacity <= 0.0) discard;", "// Absorption is tested by NativeMboitGenerate/Colour.");
    return HalfSceneParameterSource() + text + NativeMboitKernelSource() + R"(
struct AecnHalfFragment { float3 radiance; float coverage; float emission; };
AecnHalfFragment AecnEvaluateHalf(const PS_Input Input)
{
    float4 covered = AecnHalfMaterial(Input, AecnHalfSource.x > .5 ? 1 : 0, AecnHalfSource.y);
    float4 emission = 0;
    if (AecnHalfSource.x > .5 && AecnHalfSource.z > 0)
        emission = AecnHalfMaterial(Input, 2, AecnHalfSource.z);
    AecnHalfFragment result;
    result.radiance = covered.rgb * covered.a + emission.rgb * emission.a;
    result.coverage = covered.a;
    result.emission = emission.a;
    return result;
}
)";
}
inline std::string HalfSceneSampleInputs(std::string text)
{
    BulkShaderReplace(text, "#define C_CENTROID centroid", "#define C_CENTROID sample");
    BulkShaderReplace(text, "float AecnSoftViewZ : TEXCOORD10;", "sample float AecnSoftViewZ : TEXCOORD10;");
    BulkShaderReplace(text, "float AecnMomentDepth : TEXCOORD11;", "sample float AecnMomentDepth : TEXCOORD11;");
    BulkShaderReplace(text, "float4 PosP : TEXCOORD6;", "sample float4 PosP : TEXCOORD6;");
    return text;
}
inline std::string HalfSceneMomentPixelSource(const std::string& original, bool multisample = false)
{
    auto source = HalfSceneMaterialSource(original) + R"(
MboitMoments main(const PS_Input Input)
{
    AecnHalfFragment fragment = AecnEvaluateHalf(Input);
    return NativeMboitGenerate(fragment.coverage, fragment.emission, Input.AecnMomentDepth, AecnHalfSource.w);
}
)";
    return multisample ? HalfSceneSampleInputs(std::move(source)) : source;
}
inline std::string HalfSceneColourPixelSource(const std::string& original, bool multisample = false)
{
    auto source = HalfSceneMaterialSource(original) + R"(
Texture2D<float> AecnB0 : register(t5);
Texture2D<float4> AecnB3456 : register(t6);
Texture2D<float2> AecnB12 : register(t7);
float4 main(const PS_Input Input) : SV_Target0
{
    int3 pixel = int3(int2(Input.Position.xy * AecnHalfWarp.x), 0);
    float b0 = AecnB0.Load(pixel);
    float4 b3456 = AecnB3456.Load(pixel);
    float2 b12 = AecnB12.Load(pixel);
    AecnHalfFragment fragment = AecnEvaluateHalf(Input);
    float transmission = NativeMboitTransmittance(b0, b12, b3456, Input.AecnMomentDepth,
        AecnHalfPlanes.z, AecnHalfPlanes.w);
    return NativeMboitColour(fragment.radiance, fragment.coverage, fragment.emission, transmission, AecnHalfSource.w);
}
)";
    if (multisample)
    {
        source = HalfSceneSampleInputs(std::move(source));
        BulkShaderReplace(source, "Texture2D<float> AecnB0", "Texture2DMS<float, 4> AecnB0");
        BulkShaderReplace(source, "Texture2D<float4> AecnB3456", "Texture2DMS<float4, 4> AecnB3456");
        BulkShaderReplace(source, "Texture2D<float2> AecnB12", "Texture2DMS<float2, 4> AecnB12");
        BulkShaderReplace(source, "float4 main(const PS_Input Input) : SV_Target0", "float4 main(const PS_Input Input, uint sampleIndex : SV_SampleIndex) : SV_Target0");
        for (const auto name : {"AecnB0", "AecnB3456", "AecnB12"})
            BulkShaderReplace(source, std::string(name) + ".Load(pixel)", std::string(name) + ".Load(pixel.xy, sampleIndex)");
    }
    return source;
}
inline std::string HalfSceneFullscreenVertexSource()
{
    return R"(
float4 main(uint id : SV_VertexID) : SV_Position
{
    float2 uv = float2((id << 1) & 2, id & 2);
    return float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
}
)";
}
inline std::string HalfSceneMergePixelSource()
{
    return R"(
Texture2D<float> halfB0 : register(t0);
Texture2D<float4> halfB3456 : register(t1);
Texture2D<float2> halfB12 : register(t2);
struct AecnMergedMoments { float b0 : SV_Target0; float4 b3456 : SV_Target1; float2 b12 : SV_Target2; };
AecnMergedMoments main(float4 pixel : SV_Position)
{
    int3 source = int3(int2(floor(pixel.xy * .5)), 0);
    AecnMergedMoments result;
    result.b0 = halfB0.Load(source); result.b3456 = halfB3456.Load(source); result.b12 = halfB12.Load(source);
    return result;
}
)";
}
inline std::string HalfSceneComposePixelSource(bool multisample = false)
{
    auto source = HalfSceneParameterSource() + R"(
Texture2D<float> fullB0 : register(t0);
Texture2D<float4> fullColour : register(t1);
Texture2D<float4> halfColour : register(t2);
SamplerState linearClamp : register(s0);
float4 main(float4 pixel : SV_Position) : SV_Target0
{
    float2 uv = trunc(pixel.xy) * AecnHalfWarp.zw;
    float b0 = fullB0.SampleLevel(linearClamp, uv, 0);
    float4 colour = fullColour.SampleLevel(linearClamp, uv, 0) + halfColour.SampleLevel(linearClamp, uv, 0);
    float transmission = exp2(-b0 * asfloat(0x3fb8aa3b));
    float scale = (1 - transmission) / colour.a;
    scale = isfinite(scale) ? scale : 0;
    // Native uses source alpha T with ONE/SRC_ALPHA. AECN preserves scene coverage alpha.
    return float4(colour.rgb * scale, 1 - transmission);
}
)";
    if (multisample)
    {
        BulkShaderReplace(source, "Texture2D<float> fullB0", "Texture2DMS<float, 4> fullB0");
        BulkShaderReplace(source, "Texture2D<float4> fullColour", "Texture2DMS<float4, 4> fullColour");
        BulkShaderReplace(source, "float4 main(float4 pixel : SV_Position) : SV_Target0", R"(
float AecnSampleB0(float2 uv, uint sampleIndex)
{
    float2 coordinate = uv * AecnHalfSurface.zw - .5;
    int2 base = int2(floor(coordinate)), last = int2(AecnHalfSurface.zw) - 1;
    float2 weight = frac(coordinate);
    return lerp(lerp(fullB0.Load(clamp(base, int2(0,0), last), sampleIndex),
        fullB0.Load(clamp(base+int2(1,0), int2(0,0), last), sampleIndex), weight.x),
        lerp(fullB0.Load(clamp(base+int2(0,1), int2(0,0), last), sampleIndex),
        fullB0.Load(clamp(base+int2(1,1), int2(0,0), last), sampleIndex), weight.x), weight.y);
}
float4 AecnSampleFullColour(float2 uv, uint sampleIndex)
{
    float2 coordinate = uv * AecnHalfSurface.zw - .5;
    int2 base = int2(floor(coordinate)), last = int2(AecnHalfSurface.zw) - 1;
    float2 weight = frac(coordinate);
    return lerp(lerp(fullColour.Load(clamp(base, int2(0,0), last), sampleIndex),
        fullColour.Load(clamp(base+int2(1,0), int2(0,0), last), sampleIndex), weight.x),
        lerp(fullColour.Load(clamp(base+int2(0,1), int2(0,0), last), sampleIndex),
        fullColour.Load(clamp(base+int2(1,1), int2(0,0), last), sampleIndex), weight.x), weight.y);
}
float4 main(float4 pixel : SV_Position, uint sampleIndex : SV_SampleIndex) : SV_Target0
)");
        BulkShaderReplace(source, "fullB0.SampleLevel(linearClamp, uv, 0)", "AecnSampleB0(uv, sampleIndex)");
        BulkShaderReplace(source, "fullColour.SampleLevel(linearClamp, uv, 0)", "AecnSampleFullColour(uv, sampleIndex)");
    }
    return source;
}
inline std::string HalfSceneDepthPixelSource()
{
    return R"(
Texture2D<float> sourceDepth : register(t0);
float main(float4 pixel : SV_Position) : SV_Depth
{
    int2 source = int2(pixel.xy) * 2;
    return max(0, max(max(sourceDepth.Load(int3(source, 0)), sourceDepth.Load(int3(source + int2(1, 0), 0))),
        max(sourceDepth.Load(int3(source + int2(0, 1), 0)), sourceDepth.Load(int3(source + int2(1, 1), 0)))));
}
)";
}
