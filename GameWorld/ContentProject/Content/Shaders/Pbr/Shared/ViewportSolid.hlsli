float ViewportSurfaceOpacity = 1.0f;
bool ViewportWireframe = false;
bool ViewportWireframeObjectSelection = true;
int ViewportSolidLighting = 0;
Texture2D ViewportMatcap;
Texture2D ViewportGeometry;
bool ViewportGeometryEnabled = false;
float2 ViewportSize;
float4x4 ViewportInverseProjection;
float ViewportCavityStrength = 0;
float ViewportShadowStrength = 0;
SamplerState ViewportLinearSampler
{
    MinFilter = Linear;
    MagFilter = Linear;
    MipFilter = Linear;
    AddressU = Clamp;
    AddressV = Clamp;
};
SamplerState ViewportPointSampler
{
    MinFilter = Point;
    MagFilter = Point;
    MipFilter = Point;
    AddressU = Clamp;
    AddressV = Clamp;
};

float4 ViewportSurfaceColour(float3 colour)
{
    return float4(colour * ViewportSurfaceOpacity, ViewportSurfaceOpacity);
}

float3 ViewportViewNormal(float3 surfaceNormal, float3 worldPosition)
{
    float3 normal = normalize(mul(surfaceNormal, (float3x3) View));
    float3 position = mul(float4(worldPosition, 1), View).xyz;
    float3 toEye = Projection[3][3] > 0.5f ? float3(0, 0, 1) : -position;
    return dot(normal, toEye) < 0 ? -normal : normal;
}

float4 ViewportGeometryPixelShader(PixelInputType input) : SV_TARGET0
{
    return float4(ViewportViewNormal(input.normal, input.worldPosition),
        -mul(float4(input.worldPosition, 1), View).z);
}

float3 ViewportViewPosition(float2 uv, float depth)
{
    float4 position = mul(float4(uv * float2(2, -2) + float2(-1, 1), 1, 1), ViewportInverseProjection);
    position.xyz /= position.w;
    if (Projection[3][3] > 0.5f)
        position.z = -depth;
    else
        position.xyz *= depth / max(-position.z, 0.00001f);
    return position.xyz;
}

float ViewportSurfaceDetail(float2 pixelPosition, float3 normal)
{
    if (!ViewportGeometryEnabled)
        return 1.0f;
    float2 uv = pixelPosition / ViewportSize;
    float depth = ViewportGeometry.SampleLevel(ViewportPointSampler, uv, 0).w;
    if (depth <= 0)
        return 1.0f;
    float3 position = ViewportViewPosition(uv, depth);
    float pixelWorld = 2.0f / (ViewportSize.y * abs(Projection[1][1]));
    if (Projection[3][3] < 0.5f)
        pixelWorld *= depth;
    float occlusion = 0;
    float valley = 0;
    float ridge = 0;
    // Fixed screen-space radii give stable detail when switching projections.
    [unroll] for (int i = 0; i < 16; i++)
    {
        float angle = (i % 8) * 0.785398163f;
        float radius = i < 8 ? 2.0f : 14.0f;
        float2 sampleUv = uv + float2(cos(angle), sin(angle)) * radius / ViewportSize;
        if (any(sampleUv < 0) || any(sampleUv > 1))
            continue;
        float4 neighbour = ViewportGeometry.SampleLevel(ViewportPointSampler, sampleUv, 0);
        if (neighbour.w <= 0)
            continue;
        float3 delta = ViewportViewPosition(sampleUv, neighbour.w) - position;
        float distance = length(delta);
        float range = saturate(1.0f - distance / (pixelWorld * radius * 4.0f));
        float horizon = dot(normal, delta) / max(distance, pixelWorld * 0.25f);
        if (i < 8)
        {
            valley += max(horizon - 0.025f, 0) * range;
            ridge += max(-horizon - 0.025f, 0) * range;
        }
        else
            occlusion += max(horizon - 0.05f, 0) * range;
    }
    return clamp(1 - ViewportShadowStrength * occlusion * 0.35f -
        ViewportCavityStrength * valley * 0.3f + ViewportCavityStrength * ridge * 0.15f, 0.25f, 1.15f);
}

float3 ShadeViewportSolid(float3 surfaceNormal, float3 worldPosition, float3 cameraPosition, float2 pixelPosition)
{
    float3 normal = ViewportViewNormal(surfaceNormal, worldPosition);
    float3 colour;
    if (ViewportSolidLighting != 0)
        colour = ViewportMatcap.SampleLevel(ViewportLinearSampler, normal.xy * float2(0.5f, -0.5f) + 0.5f, 0).rgb;
    else
    {
        float key = saturate(dot(normal, normalize(float3(-0.35f, 0.55f, 1))));
        float fill = saturate(dot(normal, normalize(float3(0.55f, -0.15f, 1))));
        float light = 0.18f + key * 0.65f + fill * 0.16f;
        colour = pow(saturate(float3(0.56f, 0.57f, 0.59f) * light), 1.0f / 2.2f);
    }
    return colour * ViewportSurfaceDetail(pixelPosition, normal);
}
