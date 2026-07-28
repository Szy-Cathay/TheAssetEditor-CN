#include "Pbr/inputlayouts.hlsli"
#include "Pbr/Capabilites/Animation.hlsli"

float4x4 World;
float4x4 View;
float4x4 Projection;
float4 SelectionColour;
float SelectionDepthBias;

struct SelectionVertexOutput
{
    float4 Position : SV_POSITION;
};

SelectionVertexOutput MainVertexShader(
    in VertexInputType input)
{
    SelectionVertexOutput output =
        (SelectionVertexOutput)0;
    float4 position;
    float3 normal;
    float3 tangent;
    float3 binormal;
    DoSkinning(
        input,
        position,
        normal,
        tangent,
        binormal);

    output.Position = mul(position, World);
    output.Position = mul(output.Position, View);
    output.Position = mul(output.Position, Projection);
    output.Position.z = max(
        0.0f,
        output.Position.z -
            SelectionDepthBias *
            abs(output.Position.w));
    return output;
}

SelectionVertexOutput StaticVertexShader(
    in VertexInputType input)
{
    SelectionVertexOutput output =
        (SelectionVertexOutput)0;
    output.Position = mul(input.position, World);
    output.Position = mul(output.Position, View);
    output.Position = mul(output.Position, Projection);
    output.Position.z = max(
        0.0f,
        output.Position.z -
            SelectionDepthBias *
            abs(output.Position.w));
    return output;
}

float4 MainPixelShader(
    SelectionVertexOutput input) : SV_TARGET0
{
    return SelectionColour;
}

technique AnimatedSelection
{
    pass P0
    {
        VertexShader = compile vs_5_0 MainVertexShader();
        PixelShader = compile ps_5_0 MainPixelShader();
    }
};

technique StaticSelection
{
    pass P0
    {
        VertexShader = compile vs_5_0 StaticVertexShader();
        PixelShader = compile ps_5_0 MainPixelShader();
    }
};
