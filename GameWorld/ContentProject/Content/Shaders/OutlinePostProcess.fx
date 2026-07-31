////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  Screen-space outline post-process for selection highlight
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float2 InverseResolution;
float3 OutlineColor = float3(1.0, 0.5, 0.0);

Texture2D ScreenTexture;

SamplerState LinearSampler
{
    Texture = <ScreenTexture>;

    MagFilter = LINEAR;
    MinFilter = LINEAR;
    Mipfilter = LINEAR;

    AddressU = CLAMP;
    AddressV = CLAMP;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCTS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VertexShaderInput
{
    float3 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

struct VertexShaderOutput
{
    float4 Position : POSITION0;
    float2 TexCoord : TEXCOORD0;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VERTEX SHADER
////////////////////////////////////////////////////////////////////////////////////////////////////////////

VertexShaderOutput VertexShaderFunction(VertexShaderInput input)
{
    VertexShaderOutput output;
    output.Position = float4(input.Position, 1);
    output.TexCoord = input.TexCoord;
    return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  PIXEL SHADER - Screen-space edge detection outline
//  Samples the selection mask in a ring around each pixel.
//  If the center is empty but neighbours have mask content, output outline color.
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 OutlinePS(float4 pos : SV_POSITION, float2 texCoord : TEXCOORD0) : SV_TARGET0
{
    float center = ScreenTexture.Sample(LinearSampler, texCoord).a;
    float2 px = InverseResolution;
    float nearAlpha = 0;

    // Keep a strong one-pixel ring without the second dilation pass that made
    // small distant parts merge into blocks.
    float2 o1 = px;
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2(-o1.x, -o1.y)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2( 0,     -o1.y)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2( o1.x,  -o1.y)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2(-o1.x,   0)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2( o1.x,   0)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2(-o1.x,   o1.y)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2( 0,       o1.y)).a);
    nearAlpha = max(nearAlpha, ScreenTexture.Sample(LinearSampler, texCoord + float2( o1.x,    o1.y)).a);

    float nearCoverage = saturate(nearAlpha - center);
    float outlineAlpha = smoothstep(
        0.0,
        0.85,
        nearCoverage);
    if (outlineAlpha <= 0.001)
        discard;

    return float4(OutlineColor * outlineAlpha, outlineAlpha);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique Outline
{
    pass Pass1
    {
        VertexShader = compile vs_4_0 VertexShaderFunction();
        PixelShader = compile ps_4_0 OutlinePS();
    }
}
