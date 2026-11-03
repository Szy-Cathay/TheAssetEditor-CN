#pragma once
#include "NativeMboitShaderSources.h"

inline std::string NativeFogBloodMaterialSource(const std::string& original, bool blood)
{
    return HalfSceneMaterialSource(original) + R"AECN_FOG_BLOOD(
cbuffer AecnFogBloodMaterial : register(b2) { float4 AecnFogBloodValues[6]; };
float AecnFbParameter(int index) { return AecnFogBloodValues[index/4][index%4]; }
float3 AecnFbAmbient(float3 n)
{
    return n.x*n.x*(n.x<0?ambientNX.rgb:ambientPX.rgb)
        + n.y*n.y*(n.y<0?ambientNY.rgb:ambientPY.rgb)
        + n.z*n.z*(n.z<0?ambientNZ.rgb:ambientPZ.rgb);
}
float3 AecnBloodLighting(float3 baseColour, float2 material, float3 n, float3 viewVector)
{
    float4 r1=0,r2=0,r3=0,r4=0,r5=0,r6=0,r7=0,r8=0,r9=0,r10=0,r11=0,r12=0,r13=0;
    float4 nativeSunDirection=float4(0,sunDirection.xyz);
    float4 nativeSunColour=float4(sunColour.rgb*(20000*sunDirection.w),0);
    r3.xyz=lerp(.04,baseColour,material.x); r4.xyz=baseColour*(1-material.x);
    r5.xyz=n; r1.z=saturate(1-material.y); float smoothness=1-r1.z;
    r6.xyz=viewVector; r1.w=rsqrt(dot(viewVector,viewVector)); r8.xyz=viewVector*r1.w;
    r2.w=saturate(dot(-r8.xyz,n)); r7.x=dot(n,-sunDirection.xyz); r8.w=saturate(r7.x);
    r9.w=1-smoothness*smoothness*.5; r10.w=smoothness*smoothness*.5;
    r2.w=1/(r2.w*r9.w+r10.w); r5.w=sin(smoothness*asfloat(0x3fc90fdb));
    r10.xyz=reflect(-r8.xyz,n); r13.xyz=1-r3.xyz;
    r9.xyz=AecnFbAmbient(n)*r4.xyz*r13.xyz; r2.x=1;
    // Official Blood PS direct sun arithmetic, with exact DXBC immediate bits.
    r3.w = dot(-(nativeSunDirection.yzw), r10.xyz);
    r3.w = max(r3.w, asfloat(0xbf800000));
    r3.w = min(r3.w, asfloat(0x3f800000));
    r4.w = -(abs(r3.w)) + asfloat(0x3f800000);
    r4.w = sqrt(r4.w);
    r10.x = mad(abs(r3.w), asfloat(0xbc996e30), asfloat(0x3d981627));
    r10.x = mad(r10.x, abs(r3.w), asfloat(0xbe593484));
    r10.x = mad(r10.x, abs(r3.w), asfloat(0x3fc90da4));
    r10.y = r4.w * r10.x;
    r10.y = mad(r10.y, asfloat(0xc0000000), asfloat(0x40490fdb));
    r3.w = asfloat(r3.w < -(r3.w) ? 0xffffffff : 0);
    r3.w = asfloat(asuint(r3.w) & asuint(r10.y));
    r3.w = mad(r10.x, r4.w, r3.w);
    r1.z = mad(r1.z, asfloat(0xbf900828), asfloat(0x3f906f69));
    r10.xyz = r3.www + float3(asfloat(0xbc0efa35),asfloat(0x3c0efa35),asfloat(0x3c0efa35));
    r3.w = asfloat(0x3f800000) / r1.z;
    r10.xyz = r3.www * r10.xyz;
    r11.xy = r10.xy * asfloat(0x3f3504f3);
    r4.w = abs(r11.x) * abs(r11.x);
    r10.y = abs(r11.x) * r4.w;
    r11.z = abs(r11.x) * r10.y;
    r11.x = mad(abs(r11.x), asfloat(0x3e8e8987), asfloat(0x3f800000));
    r4.w = mad(r4.w, asfloat(0x3e6beb18), r11.x);
    r4.w = mad(r10.y, asfloat(0x3a7ecdd1), r4.w);
    r4.w = mad(r11.z, asfloat(0x3d9ff716), r4.w);
    r4.w = r4.w * r4.w;
    r4.w = r4.w * r4.w;
    r4.w = asfloat(0x3f800000) / r4.w;
    r4.w = -(r4.w) + asfloat(0x3f800000);
    r10.y = asfloat(asfloat(0x00000000) < r10.x ? 0xffffffff : 0);
    r10.x = asfloat(r10.x < asfloat(0x00000000) ? 0xffffffff : 0);
    r10.x = asfloat(-(asint(r10.y)) + asint(r10.x));
    r10.x = float(asint(r10.x));
    r4.w = mad(r10.x, r4.w, asfloat(0x3f800000));
    r4.w = r4.w * asfloat(0x3f000000);
    r10.x = r11.y * r11.y;
    r10.y = r11.y * r10.x;
    r11.x = r11.y * r10.y;
    r10.z = mad(r10.z, asfloat(0x3e4993fc), asfloat(0x3f800000));
    r10.x = mad(r10.x, asfloat(0x3e6beb18), r10.z);
    r10.x = mad(r10.y, asfloat(0x3a7ecdd1), r10.x);
    r10.x = mad(r11.x, asfloat(0x3d9ff716), r10.x);
    r10.x = r10.x * r10.x;
    r10.x = r10.x * r10.x;
    r10.x = asfloat(0x3f800000) / r10.x;
    r10.x = -(r10.x) + asfloat(0x40000000);
    r4.w = mad(r10.x, asfloat(0x3f000000), -(r4.w));
    r10.xy = r3.ww * float2(asfloat(0xbf8e2c19),asfloat(0x3f8e2c19));
    r11.xy = r10.xy * r10.xy;
    r10.z = -(r10.x) * r11.x;
    r10.x = -(r10.x) * r10.z;
    r3.w = mad(r3.w, asfloat(0x3e9e51ad), asfloat(0x3f800000));
    r11.xz = mad(r11.xy, asfloat(0x3e6beb18), r3.ww);
    r3.w = mad(r10.z, asfloat(0x3a7ecdd1), r11.x);
    r3.w = mad(r10.x, asfloat(0x3d9ff716), r3.w);
    r3.w = r3.w * r3.w;
    r3.w = r3.w * r3.w;
    r3.w = asfloat(0x3f800000) / r3.w;
    r3.w = -(r3.w) + asfloat(0x3f800000);
    r3.w = -(r3.w) + asfloat(0x3f800000);
    r3.w = r3.w * asfloat(0x3f000000);
    r10.x = r10.y * r11.y;
    r10.y = r10.y * r10.x;
    r10.x = mad(r10.x, asfloat(0x3a7ecdd1), r11.z);
    r10.x = mad(r10.y, asfloat(0x3d9ff716), r10.x);
    r10.x = r10.x * r10.x;
    r10.x = r10.x * r10.x;
    r10.x = asfloat(0x3f800000) / r10.x;
    r10.x = -(r10.x) + asfloat(0x40000000);
    r3.w = mad(r10.x, asfloat(0x3f000000), -(r3.w));
    r3.w = abs(r4.w) / r3.w;
    r3.w = min(r3.w, asfloat(0x3f800000));
    r3.w = r3.w * r3.w;
    r3.w = r3.w * asfloat(0x3f490fdb);
    r4.w = r8.w + asfloat(0xbf800000);
    r4.w = mad(r5.w, r4.w, asfloat(0x3f800000));
    r5.w = mad(r8.w, r9.w, r10.w);
    r5.w = asfloat(0x3f800000) / r5.w;
    r2.w = r2.w * r5.w;
    r2.w = r2.w * asfloat(0x3e800000);
    r2.w = min(r2.w, asfloat(0x3f800000));
    r2.w = r4.w * r2.w;
    r6.xyz = mad(r6.xyz, r1.www, -(nativeSunDirection.yzw));
    r1.w = dot(r6.xyz, r6.xyz);
    r1.w = rsqrt(r1.w);
    r6.xyz = r1.www * r6.xyz;
    r1.w = saturate(dot(r8.xyz, r6.xyz));
    r4.w = mad(r1.w, asfloat(0xc0b1c059), asfloat(0xc0df760c));
    r1.w = r1.w * r4.w;
    r1.w = exp2(r1.w);
    r6.xyz = mad(r13.xyz, r1.www, r3.xyz);
    r1.w = r2.w * r3.w;
    r6.xyz = r6.xyz * r1.www;
    r1.w = mad(r8.w, asfloat(0xc0b1c059), asfloat(0xc0df760c));
    r1.w = r8.w * r1.w;
    r1.w = exp2(r1.w);
    r3.xyz = mad(r13.xyz, r1.www, r3.xyz);
    r3.xyz = -(r3.xyz) + asfloat(0x3f800000);
    r1.w = r1.z * r1.z;
    r10.xy = mad(r1.zz, r1.zz, float2(asfloat(0x3ea8f5c3),asfloat(0x3db851ec)));
    r1.z = r10.x + r10.x;
    r1.z = r1.w / r1.z;
    r1.z = -(r1.z) + asfloat(0x3f800000);
    r1.w = r1.w * asfloat(0x3ee66666);
    r1.w = r1.w / r10.y;
    r2.w = max(r7.x, asfloat(0xbf800000));
    r2.w = min(r2.w, asfloat(0x3f800000));
    r3.w = dot(r5.xyz, r8.xyz);
    r3.w = max(r3.w, asfloat(0xbf800000));
    r3.w = min(r3.w, asfloat(0x3f800000));
    r4.w = -(abs(r2.w)) + asfloat(0x3f800000);
    r4.w = sqrt(r4.w);
    r5.x = mad(abs(r2.w), asfloat(0xbc996e30), asfloat(0x3d981627));
    r5.x = mad(r5.x, abs(r2.w), asfloat(0xbe593484));
    r5.x = mad(r5.x, abs(r2.w), asfloat(0x3fc90da4));
    r5.y = r4.w * r5.x;
    r5.y = mad(r5.y, asfloat(0xc0000000), asfloat(0x40490fdb));
    r5.z = asfloat(r2.w < -(r2.w) ? 0xffffffff : 0);
    r5.y = asfloat(asuint(r5.z) & asuint(r5.y));
    r4.w = mad(r5.x, r4.w, r5.y);
    r5.x = -(abs(r3.w)) + asfloat(0x3f800000);
    r5.x = sqrt(r5.x);
    r5.y = mad(abs(r3.w), asfloat(0xbc996e30), asfloat(0x3d981627));
    r5.y = mad(r5.y, abs(r3.w), asfloat(0xbe593484));
    r5.y = mad(r5.y, abs(r3.w), asfloat(0x3fc90da4));
    r5.z = r5.x * r5.y;
    r5.z = mad(r5.z, asfloat(0xc0000000), asfloat(0x40490fdb));
    r3.w = asfloat(r3.w < -(r3.w) ? 0xffffffff : 0);
    r3.w = asfloat(asuint(r3.w) & asuint(r5.z));
    r3.w = mad(r5.y, r5.x, r3.w);
    r5.x = max(r4.w, r3.w);
    r5.y = min(r4.w, r3.w);
    r3.w = -(r3.w) + r4.w;
    r3.w = cos(r3.w);
    r3.w = max(r3.w, asfloat(0x00000000));
    r1.w = r1.w * r3.w;
    r3.w = sin(r5.x);
    r1.w = r1.w * r3.w;
    r5.x = sin(r5.y);
    r7.x = cos(r5.y);
    r3.w = r5.x / r7.x;
    r1.z = mad(r1.w, r3.w, r1.z);
    r1.w = max(r2.w, asfloat(0x00000000));
    r1.z = r1.w * r1.z;
    r3.xyz = r3.xyz * nativeSunColour.xyz;
    r3.xyz = r4.xyz * r3.xyz;
    r3.xyz = r1.zzz * r3.xyz;
    r3.xyz = r3.xyz * asfloat(0x389fb405);
    r3.xyz = mad(r6.xyz, nativeSunColour.xyz, r3.xyz);
    r3.xyz = mad(r2.xxx, r3.xyz, r9.xyz);
    return r3.xyz;
}
struct AecnFbFragment { float3 radiance; float opacity; float absorption; };
float AecnFbFade(const PS_Input Input, float strength, float softFactor)
{
    float distance=softFactor*(1+strength*(saturate(Input.AecnDynamic0.y)-1))+.001;
    return CalcDepthFade(Input.Position.xy,Input.PosP.z/Input.PosP.w,distance,Input.AecnSoftViewZ);
}
AecnFbFragment AecnEvaluateFogBlood(const PS_Input Input, bool frontFace)
{
    parameters=Input.AecnDynamic0; particleInfo=Input.AecnDynamic1;
    normalUv=Input.AecnDynamic2; softDepthInfo=Input.AecnDynamic3;
    AecnFbFragment result;
)AECN_FOG_BLOOD" + (blood ? R"AECN_BLOOD(
    float2 nextUV=Input.UV1+Input.CustomData1.xy;
    float4 sampled=lerp(source_texture.Sample(source_sampler,Input.UV1),source_texture.Sample(source_sampler,nextUV),Input.CustomData1.z);
    float2 material=lerp(shadingNormal_texture.Sample(shadingNormal_sampler,Input.UV1).rg,
        shadingNormal_texture.Sample(shadingNormal_sampler,nextUV).rg,Input.CustomData1.z);
    float2 xy=lerp(gradient_texture.Sample(gradient_sampler,Input.UV1).ag,
        gradient_texture.Sample(gradient_sampler,nextUV).ag,Input.CustomData1.z);
    xy=(xy+.00196078431372549)*2-1;
    float z=sqrt(max(0,1-dot(xy,xy)))*(frontFace?1:-1);
    float3 n=normalize((-xy.x*Input.AecnDynamic2.x*normalize(Input.WorldT)
        -xy.y*Input.AecnDynamic2.y*normalize(Input.WorldB)-z*normalize(Input.WorldN))*AecnFbParameter(0));
    float fade=AecnFbFade(Input,AecnFbParameter(1),AecnFbParameter(2));
    result.opacity=saturate(sampled.a*Input.VColor.a*fade);
    float3 baseColour=sampled.rgb*Input.VColor.rgb;
    float3 lit=0;
    if(scattering.z>.5)lit=AecnBloodLighting(baseColour,material,n,cameraPosition.xyz-Input.WorldP);
    result.radiance=lit*result.opacity*scattering.w;
    result.absorption=min(result.opacity+asfloat(0x3a83126f),asfloat(0x3f7fbe77));
)AECN_BLOOD" : R"AECN_FOG(
    float time=Input.AecnDynamic2.z;
    float2 shifted=Input.UV1+Input.AecnDynamic2.w;
    float2 noiseUV=shifted*float2(AecnFbParameter(7),AecnFbParameter(8))
        +time*float2(AecnFbParameter(9),AecnFbParameter(10));
    float2 distortionUV=(shifted+.5)*float2(AecnFbParameter(3),AecnFbParameter(4))
        +time*float2(AecnFbParameter(11),AecnFbParameter(12));
    float2 distortion=source_texture.Sample(source_sampler,distortionUV).rg;
    float2 particleUV=Input.UV1+distortion.x*AecnFbParameter(1);
    float4 particle=gradient_texture.Sample(gradient_sampler,particleUV);
    noiseUV=noiseUV*float2(AecnFbParameter(13),AecnFbParameter(14))+distortion.y*AecnFbParameter(2);
    float noise=shadingNormal_texture.Sample(shadingNormal_sampler,noiseUV).r;
    noise=mad(ceil(AecnFbParameter(5)),1-2*noise,noise);
    float noiseGain=mad(AecnFbParameter(6),noise-1,1);
    result.opacity=particle.a*noiseGain*Input.VColor.a*AecnFbFade(Input,AecnFbParameter(15),AecnFbParameter(16));
    float3 gain=0;
    if(scattering.z>.5)
    {
        // The preview's explicit horizontal receiver supplies its worldUp normal; no landscape map is invented.
        float3 n=float3(0,1,0);
        float average=AecnFbParameter(17),g=AecnFbParameter(18),large=AecnFbParameter(19),small=AecnFbParameter(20);
        float density=large+small,cosine=sunColour.w;
        float phase=(small*(1+cosine*cosine)*asfloat(0x3d7474db)
            +large*(1-g)*(1-g)*asfloat(0x3da2f983)/pow(max(0,1+g*g-2*g*cosine),1.5))/density;
        float transmission=exp(-density*average*Input.CustomData1.w*(1-dot(n,sunDirection.xyz))*.5);
        float solar=saturate(transmission+phase*(1-transmission));
        gain=AecnFbAmbient(n)+sunColour.rgb*(20000*sunDirection.w*asfloat(0x389fb405)*solar);
    }
    result.radiance=particle.rgb*Input.VColor.rgb*gain*result.opacity*scattering.w;
    result.absorption=clamp(result.opacity+asfloat(0x3ac49ba6),asfloat(0x3a83126f),asfloat(0x3f7fbe77));
)AECN_FOG") + R"FB_END(
    clip(-log2(1-result.absorption)*asfloat(0x3f317218)-AecnHalfSource.w);
    return result;
}
)FB_END";
}

inline std::string NativeFogBloodMomentPixelSource(const std::string& original, bool blood, bool multisample=false)
{
    auto source=NativeFogBloodMaterialSource(original,blood)+R"(
MboitMoments main(const PS_Input Input, bool frontFace : SV_IsFrontFace)
{
    AecnFbFragment fragment=AecnEvaluateFogBlood(Input,frontFace);
    float tau=-log2(1-fragment.absorption)*asfloat(0x3f317218),z=Input.AecnMomentDepth,z2=z*z,z4=z2*z2;
    MboitMoments result;result.b0=tau;result.b12=tau*float2(z,z2);result.b3456=tau*float4(z2*z,z4,z4*z,z2*z4);
    return result;
}
)";
    return multisample?HalfSceneSampleInputs(std::move(source)):source;
}
inline std::string NativeFogBloodColourPixelSource(const std::string& original, bool blood, bool multisample=false)
{
    auto source=NativeFogBloodMaterialSource(original,blood)+R"(
Texture2D<float> AecnB0 : register(t5);
Texture2D<float4> AecnB3456 : register(t6);
Texture2D<float2> AecnB12 : register(t7);
float4 main(const PS_Input Input, bool frontFace : SV_IsFrontFace) : SV_Target0
{
    int3 pixel=int3(int2(Input.Position.xy*AecnHalfWarp.x),0);
    float b0=AecnB0.Load(pixel);float4 b3456=AecnB3456.Load(pixel);float2 b12=AecnB12.Load(pixel);
    AecnFbFragment fragment=AecnEvaluateFogBlood(Input,frontFace);
    float transmission=NativeMboitTransmittance(b0,b12,b3456,Input.AecnMomentDepth,AecnHalfPlanes.z,AecnHalfPlanes.w);
    return float4(fragment.radiance,fragment.absorption)*transmission;
}
)";
    if(multisample)
    {
        source=HalfSceneSampleInputs(std::move(source));
        BulkShaderReplace(source,"Texture2D<float> AecnB0","Texture2DMS<float,4> AecnB0");
        BulkShaderReplace(source,"Texture2D<float4> AecnB3456","Texture2DMS<float4,4> AecnB3456");
        BulkShaderReplace(source,"Texture2D<float2> AecnB12","Texture2DMS<float2,4> AecnB12");
        BulkShaderReplace(source,"bool frontFace : SV_IsFrontFace) : SV_Target0","bool frontFace : SV_IsFrontFace, uint sampleIndex : SV_SampleIndex) : SV_Target0");
        for(const auto name:{"AecnB0","AecnB3456","AecnB12"})
            BulkShaderReplace(source,std::string(name)+".Load(pixel)",std::string(name)+".Load(pixel.xy,sampleIndex)");
    }
    return source;
}
