// Vertex point shader for rendering edit mode vertices as camera-facing circular points.
// Based on Blender's overlay_edit_mesh_vert.glsl approach.
// Renders instanced quads as billboarded circles with Z-bias for selected vertices.

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
float2 ViewportSize;
bool CapabilityFlag_ApplyAnimation = false;
float4x4 Animation_Tranforms[256];
int Animation_WeightCount = 0;

struct VSQuadInput
{
    float4 Position : POSITION1;
    float2 TexCoord : TEXCOORD2;
};

struct VSStaticInstanceInput
{
    float3 InstancePosition : POSITION2;
    float InstanceScale : NORMAL1;
    float3 InstanceColor : NORMAL2;
    float InstanceWeight : NORMAL3;
};

struct VSAnimatedInstanceInput
{
    float4 BindPosition : POSITION0;
    float4 Weights : COLOR0;
    float4 BoneIndices : BLENDINDICES0;
    float InstanceScale : NORMAL1;
    float3 InstanceColor : NORMAL2;
    float InstanceWeight : NORMAL3;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float2 TexCoord : TEXCOORD0;
    float4 Color : COLOR0;
    float Weight : TEXCOORD1;
};

VSOutput CreateVertexPointOutput(
    VSQuadInput input,
    float4 clipCenter,
    float instanceScale,
    float3 instanceColor,
    float instanceWeight)
{
    VSOutput output = (VSOutput)0;

    float2 clipOffset =
        input.Position.xy *
        instanceScale *
        2.0f /
        ViewportSize *
        clipCenter.w;
    output.Position = clipCenter;
    output.Position.xy += clipOffset;
    output.TexCoord = input.TexCoord;
    output.Color = float4(instanceColor, 1.0);
    output.Weight = instanceWeight;

    output.Position.z = max(
        0.0f,
        output.Position.z -
            2e-5 * abs(output.Position.w));

    if (instanceWeight > 0.5)
    {
        output.Position.z = max(
            0.0f,
            output.Position.z -
                1e-5 * abs(output.Position.w));
    }

    return output;
}

VSOutput VertexPointVS(
    VSQuadInput input,
    VSStaticInstanceInput instance)
{
    float4 clipCenter = mul(
        float4(instance.InstancePosition, 1.0),
        ViewProjection);
    return CreateVertexPointOutput(
        input,
        clipCenter,
        instance.InstanceScale,
        instance.InstanceColor,
        instance.InstanceWeight);
}

float4 GetAnimatedPosition(
    VSAnimatedInstanceInput instance)
{
    if (!CapabilityFlag_ApplyAnimation)
        return instance.BindPosition;

    float4 position = 0;
    [unroll]
    for (int weightIndex = 0;
         weightIndex < Animation_WeightCount;
         weightIndex++)
    {
        int boneIndex =
            (int)instance.BoneIndices[weightIndex];
        position +=
            instance.Weights[weightIndex] *
            mul(
                float4(instance.BindPosition.xyz, 1.0),
                Animation_Tranforms[boneIndex]);
    }

    return position;
}

VSOutput AnimatedVertexPointVS(
    VSQuadInput input,
    VSAnimatedInstanceInput instance)
{
    float4 worldPosition = mul(
        GetAnimatedPosition(instance),
        World);
    float4 clipCenter = mul(
        worldPosition,
        ViewProjection);
    return CreateVertexPointOutput(
        input,
        clipCenter,
        instance.InstanceScale,
        instance.InstanceColor,
        instance.InstanceWeight);
}

// Pixel shader: circle clipping with anti-aliasing
// Blender 3D viewport style: solid circle with AA edge, no outline ring
float4 VertexPointPS(VSOutput input) : COLOR0
{
    // Distance from center (0.5, 0.5) in UV space
    float2 center = float2(0.5, 0.5);
    float dist = length(input.TexCoord - center);

    // Discard pixels outside the circle
    if (dist > 0.5)
        discard;

    // Anti-aliased outer edge using smoothstep
    float alpha = smoothstep(0.5, 0.42, dist);

    // Solid circle with AA edge (Blender 3D viewport style - no outline ring)
    return float4(input.Color.rgb, alpha);
}

technique VertexPoint
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL VertexPointVS();
        PixelShader = compile PS_SHADERMODEL VertexPointPS();
    }
}

technique AnimatedVertexPoint
{
    pass Pass0
    {
        VertexShader =
            compile vs_5_0 AnimatedVertexPointVS();
        PixelShader =
            compile ps_5_0 VertexPointPS();
    }
}
