////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  Procedural infinite grid shader
//  Renders a ground-plane quad with analytically anti-aliased grid lines using
//  frac() + fwidth() + smoothstep(). This produces perfect AA at any zoom/angle.
//  Based on Blender's overlay_grid approach adapted for MonoGame HLSL.
////////////////////////////////////////////////////////////////////////////////////////////////////////////

#if OPENGL
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0
#define PS_SHADERMODEL ps_4_0
#endif

float4x4 World;
float4x4 View;
float4x4 Projection;
float3 CameraPosition;
float3 GridColor;
float  CameraDistance;
int    IsOrthographic;

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  STRUCTS
////////////////////////////////////////////////////////////////////////////////////////////////////////////

struct VertexShaderInput
{
    float3 Position : POSITION0;
};

struct VertexShaderOutput
{
    float4 Position : SV_POSITION;
    float3 WorldPos : TEXCOORD0;
    float  ViewDist : TEXCOORD1;
};

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  VERTEX SHADER
////////////////////////////////////////////////////////////////////////////////////////////////////////////

VertexShaderOutput GridVS(VertexShaderInput input)
{
    VertexShaderOutput output = (VertexShaderOutput)0;

    float3 worldPos = input.Position;
    float4 viewPos = mul(float4(worldPos, 1.0), View);
    output.Position = mul(viewPos, Projection);
    output.WorldPos = worldPos;
    output.ViewDist = length(viewPos.xyz);

    return output;
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  PIXEL SHADER - Procedural grid with analytical anti-aliasing
////////////////////////////////////////////////////////////////////////////////////////////////////////////

float4 GridPS(VertexShaderOutput input) : COLOR0
{
    float2 coord = input.WorldPos.xz;

    // Screen-space derivatives for analytical anti-aliasing and LOD.
    float2 dv = fwidth(coord);

    // Fine grid (every 1 unit). Fade it before one pixel spans an entire cell;
    // otherwise every distant pixel is incorrectly treated as a grid line.
    float2 fineDistance = abs(frac(coord - 0.5) - 0.5);
    float2 fineCoverage = 1.0 - smoothstep(0.0, max(dv * 0.55, 0.00001), fineDistance);
    float fineLod = 1.0 - smoothstep(0.35, 0.85, max(dv.x, dv.y));
    float fineLine = max(fineCoverage.x, fineCoverage.y) * fineLod;

    // Emphasis grid (every 5 units) remains visible after the fine grid fades.
    float2 coord5 = coord * 0.2;  // coord / 5.0
    float2 dv5 = fwidth(coord5);
    float2 emphasisDistance = abs(frac(coord5 - 0.5) - 0.5);
    float2 emphasisCoverage = 1.0 - smoothstep(0.0, max(dv5 * 0.55, 0.00001), emphasisDistance);
    float emphasisLod = 1.0 - smoothstep(0.35, 0.85, max(dv5.x, dv5.y));
    float emphasisLine = max(emphasisCoverage.x, emphasisCoverage.y) * emphasisLod;

    // Axis indicators use the same derivative-based coverage for smooth edges.
    float xAxisLine = 1.0 - smoothstep(0.0, max(dv.y, 0.00001), abs(coord.y));
    float zAxisLine = 1.0 - smoothstep(0.0, max(dv.x, 0.00001), abs(coord.x));

    // --- Distance fadeout (proportional to camera distance) ---
    // Wide, gradual fade for natural appearance (Blender style)
    float fadeStart = CameraDistance;
    float fadeEnd = min(CameraDistance * 10.0, 20000.0);
    float dist = length(input.WorldPos.xz - CameraPosition.xz);
    float distFade = 1.0 - smoothstep(fadeStart, fadeEnd, dist);
    distFade = pow(distFade, 0.75);

    // --- Angle fadeout (grid fades when viewed nearly edge-on) ---
    // Softer threshold: only fade when angle is < ~8 degrees from horizontal (0.02)
    // instead of original < ~3 degrees (0.05). This keeps grid visible when camera
    // is slightly below ground plane (Y ≈ 0), common after model-focused positioning.
    float3 viewDir = normalize(CameraPosition - input.WorldPos);
    float angleFade = smoothstep(0.02, 0.15, abs(viewDir.y));

    // --- Compose final color and alpha ---
    float combinedFade = distFade * angleFade;

    // Fine grid: subtle
    float fineAlpha = fineLine * 0.22 * combinedFade;

    // Emphasis grid: stronger, uses brighter color
    float emphasisAlpha = emphasisLine * 0.42 * combinedFade;

    // Axes: most prominent with dedicated colors
    float xAxisAlpha = xAxisLine * 0.75 * combinedFade;
    float zAxisAlpha = zAxisLine * 0.75 * combinedFade;

    // Pick the dominant contribution
    float alpha = fineAlpha;
    float3 color = GridColor;

    // Emphasis overrides fine
    if (emphasisAlpha > alpha)
    {
        alpha = emphasisAlpha;
        color = GridColor * 1.6; // brighter for emphasis lines
    }

    // X axis = red
    if (xAxisAlpha > alpha)
    {
        alpha = xAxisAlpha;
        color = float3(0.9, 0.3, 0.3);
    }

    // Z axis = blue
    if (zAxisAlpha > alpha)
    {
        alpha = zAxisAlpha;
        color = float3(0.3, 0.5, 0.9);
    }

    // Discard fully transparent pixels for early-Z
    if (alpha < 0.001)
        discard;

    // MonoGame AlphaBlend expects premultiplied RGB. Without this, bright grid
    // colors stay fully bright while alpha fades, causing a hard cutoff.
    return float4(saturate(color) * alpha, alpha);
}

////////////////////////////////////////////////////////////////////////////////////////////////////////////
//  TECHNIQUES
////////////////////////////////////////////////////////////////////////////////////////////////////////////

technique Grid
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL GridVS();
        PixelShader = compile PS_SHADERMODEL GridPS();
    }
}
