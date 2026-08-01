// Screen-space edit edges with analytic anti-aliasing.
// Each edge is expanded from a line segment into an instanced quad.

#if OPENGL
#define SV_POSITION POSITION
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 ViewProjection;
float4x4 World;
float ViewportHeight;
float ViewportWidth;
float OverlayOpacity = 1.0;
float BaseOpacity = 1.0;
float EdgeDepthBias = 0.0;
float EdgeHalfWidth = 1.0;
float3 OverlayColor;
bool CapabilityFlag_ApplyAnimation = false;
float4x4 Animation_Tranforms[256];
int Animation_WeightCount = 0;

struct VSInstanceInput
{
    float3 InstanceP0 : POSITION1;
    float3 InstanceP1 : NORMAL1;
    float3 InstanceC0 : NORMAL2;
    float3 InstanceC1 : NORMAL3;
    float InstanceWidth : NORMAL4;
};

struct VSAnimatedInstanceInput
{
    float3 BindP0 : POSITION1;
    float4 Weights0 : COLOR1;
    float4 BoneIndices0 : BLENDINDICES1;
    float3 BindP1 : POSITION2;
    float4 Weights1 : COLOR2;
    float4 BoneIndices1 : BLENDINDICES2;
};

struct VSInput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float EdgeDist : TEXCOORD1;
    float HalfWidthPixels : TEXCOORD2;
    float ProjectedLengthPixels : TEXCOORD3;
};

VSOutput CreateEdgeQuadOutput(
    VSInput input,
    float4 clip0,
    float4 clip1,
    float3 color0,
    float3 color1,
    float halfWidth)
{
    VSOutput output = (VSOutput)0;
    const float minClipW = 0.0001;
    if (clip0.w < minClipW && clip1.w >= minClipW)
    {
        float clipAmount =
            (minClipW - clip0.w) / (clip1.w - clip0.w);
        clip0 = lerp(clip0, clip1, saturate(clipAmount));
    }
    else if (clip1.w < minClipW && clip0.w >= minClipW)
    {
        float clipAmount =
            (minClipW - clip1.w) / (clip0.w - clip1.w);
        clip1 = lerp(clip1, clip0, saturate(clipAmount));
    }

    float2 ndc0 = clip0.xy / clip0.w;
    float2 ndc1 = clip1.xy / clip1.w;
    float2 viewportSize =
        float2(ViewportWidth, ViewportHeight);
    float2 edgePixels =
        (ndc1 - ndc0) * viewportSize * 0.5;
    float edgeLen = length(edgePixels);

    float2 edgeDir = edgePixels / max(edgeLen, 0.0001);
    float2 perpDir = float2(-edgeDir.y, edgeDir.x);
    float t = input.Position.x + 0.5;
    float side = input.Position.y * 2.0;
    float2 baseNdc = lerp(ndc0, ndc1, t);
    float2 offsetNdc =
        perpDir * halfWidth * side *
        2.0 / viewportSize;
    float2 finalNdc = baseNdc + offsetNdc;
    float w = lerp(clip0.w, clip1.w, t);
    float z = lerp(clip0.z, clip1.z, t);

    output.Position = float4(finalNdc * w, z, w);
    output.Position.z = max(
        0.0,
        output.Position.z -
            EdgeDepthBias * abs(output.Position.w));
    output.Color = float4(
        lerp(color0, color1, t),
        1.0);
    output.EdgeDist = side;
    output.HalfWidthPixels = halfWidth;
    output.ProjectedLengthPixels = edgeLen;
    return output;
}

VSOutput EdgeQuadVS(VSInput input, VSInstanceInput instance)
{
    return CreateEdgeQuadOutput(
        input,
        mul(float4(instance.InstanceP0, 1.0), ViewProjection),
        mul(float4(instance.InstanceP1, 1.0), ViewProjection),
        instance.InstanceC0,
        instance.InstanceC1,
        instance.InstanceWidth);
}

float4 GetAnimatedPosition(
    float3 bindPosition,
    float4 weights,
    float4 boneIndices)
{
    if (!CapabilityFlag_ApplyAnimation)
        return float4(bindPosition, 1.0);

    float4 position = 0;
    [unroll]
    for (int weightIndex = 0;
         weightIndex < Animation_WeightCount;
         weightIndex++)
    {
        int boneIndex = (int)boneIndices[weightIndex];
        position +=
            weights[weightIndex] *
            mul(
                float4(bindPosition, 1.0),
                Animation_Tranforms[boneIndex]);
    }

    return position;
}

VSOutput AnimatedEdgeQuadVS(
    VSInput input,
    VSAnimatedInstanceInput instance)
{
    float4 worldP0 = mul(
        GetAnimatedPosition(
            instance.BindP0,
            instance.Weights0,
            instance.BoneIndices0),
        World);
    float4 worldP1 = mul(
        GetAnimatedPosition(
            instance.BindP1,
            instance.Weights1,
            instance.BoneIndices1),
        World);

    return CreateEdgeQuadOutput(
        input,
        mul(worldP0, ViewProjection),
        mul(worldP1, ViewProjection),
        OverlayColor,
        OverlayColor,
        EdgeHalfWidth);
}

float4 EdgeQuadPS(VSOutput input) : COLOR0
{
    float dist = abs(input.EdgeDist);
    float coverage = saturate(
        (1.0 - dist) * input.HalfWidthPixels);
    float lengthFade = smoothstep(
        0.35,
        1.5,
        input.ProjectedLengthPixels);
    float alpha =
        coverage * lengthFade * OverlayOpacity *
        BaseOpacity * input.Color.a;
    return float4(input.Color.rgb * alpha, alpha);
}

technique EdgeQuad
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL EdgeQuadVS();
        PixelShader = compile PS_SHADERMODEL EdgeQuadPS();
    }
}

technique AnimatedEdgeQuad
{
    pass Pass0
    {
        VertexShader = compile vs_5_0 AnimatedEdgeQuadVS();
        PixelShader = compile ps_5_0 EdgeQuadPS();
    }
}
