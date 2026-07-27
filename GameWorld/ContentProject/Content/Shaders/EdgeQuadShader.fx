// Edge quad shader for rendering wireframe edges as screen-space quads with anti-aliasing.
// Based on Blender's overlay_edit_mesh_edge_vert.glsl approach.
// Each edge segment = 1 instance (4-vertex quad, 2 triangles).
// Supports gradient coloring per endpoint (GPU interpolates automatically).

#if OPENGL
#define SV_POSITION POSITION
#define VS_SHADERMODEL vs_3_0
#define PS_SHADERMODEL ps_3_0
#else
#define VS_SHADERMODEL vs_4_0_level_9_1
#define PS_SHADERMODEL ps_4_0_level_9_1
#endif

float4x4 ViewProjection;
float ViewportHeight;
float ViewportWidth;

// Instance data: two endpoints, two colors, screen-space half-width
struct VSInstanceInput
{
    float3 InstanceP0 : POSITION1;      // Edge start world position
    float3 InstanceP1 : NORMAL1;        // Edge end world position
    float3 InstanceC0 : NORMAL2;        // Start endpoint color
    float3 InstanceC1 : NORMAL3;        // End endpoint color
    float InstanceWidth : NORMAL4;      // Screen-space half-width in pixels
};

struct VSInput
{
    float4 Position : POSITION0;        // Quad corner: x,y in [-0.5, +0.5], z=0, w=1
    float2 TexCoord : TEXCOORD0;        // UV for potential future use
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color : COLOR0;
    float EdgeDist : TEXCOORD1;         // Distance from edge center (0=center, 1=edge)
};

// Vertex shader: expand each line segment into a screen-space quad
VSOutput EdgeQuadVS(VSInput input, VSInstanceInput instance)
{
    VSOutput output = (VSOutput)0;

    // 1. Project both endpoints to clip space
    float4 clip0 = mul(float4(instance.InstanceP0, 1.0), ViewProjection);
    float4 clip1 = mul(float4(instance.InstanceP1, 1.0), ViewProjection);

    // 2. Convert to NDC (Normalized Device Coordinates, [-1, 1] range)
    float2 ndc0 = clip0.xy / clip0.w;
    float2 ndc1 = clip1.xy / clip1.w;

    // 3. Compute edge direction and perpendicular in NDC space
    float2 edgeVec = ndc1 - ndc0;
    float edgeLen = length(edgeVec);

    // Handle degenerate edges (zero length)
    if (edgeLen < 0.0001)
    {
        output.Position = clip0;
        output.Color = float4(instance.InstanceC0, 1.0);
        output.EdgeDist = 0;
        return output;
    }

    float2 edgeDir = edgeVec / edgeLen;
    float2 perpDir = float2(-edgeDir.y, edgeDir.x);  // Perpendicular to edge

    // 4. Convert pixel half-width to NDC units
    // NDC Y range [-1, 1] maps to ViewportHeight pixels
    // So 1 pixel = 2.0 / ViewportHeight in NDC Y
    // For consistent width, use the smaller dimension to avoid distortion
    float ndcScale = 2.0 / ViewportHeight;
    float halfWidthNdc = instance.InstanceWidth * ndcScale;

    // 5. Select corner position based on input.Position
    // input.Position.x: -0.5 = start endpoint, +0.5 = end endpoint
    // input.Position.y: -0.5 = left side (perpDir negative), +0.5 = right side (perpDir positive)
    float t = input.Position.x + 0.5;       // 0 at start, 1 at end
    float side = input.Position.y * 2.0;    // -1 on left, +1 on right

    // 6. Compute final NDC position
    float2 baseNdc = lerp(ndc0, ndc1, t);
    float2 offsetNdc = perpDir * halfWidthNdc * side;
    float2 finalNdc = baseNdc + offsetNdc;

    // 7. Convert back to clip space
    float w = lerp(clip0.w, clip1.w, t);
    float z = lerp(clip0.z, clip1.z, t);
    output.Position = float4(finalNdc * w, z, w);

    // 8. Interpolate color between endpoints (GPU will also interpolate across quad)
    float3 color = lerp(instance.InstanceC0, instance.InstanceC1, t);
    output.Color = float4(color, 1.0);

    // 9. Store edge distance for AA (normalized: 0 at center, 1 at edge)
    // side goes from -1 to +1, but we want 0 at center
    // The actual distance from center is |side| * halfWidthNdc
    // Normalize by the full width so edge is at 1.0
    output.EdgeDist = side;  // Interpolates from -1 through 0 to +1

    return output;
}

// Pixel shader: anti-aliased edge with smoothstep (Blender style)
float4 EdgeQuadPS(VSOutput input) : COLOR0
{
    // Distance from edge center (0 = center, 1 = edge)
    float dist = abs(input.EdgeDist);

    // Blender-style smooth AA: smoothstep from edge to transparent
    // LINE_SMOOTH_START ≈ 0.5 - 0.59 ≈ -0.09 (covered)
    // LINE_SMOOTH_END ≈ 0.5 + 0.59 ≈ 1.09 (transparent)
    // Simplified: fade from full opacity at dist=0.3 to transparent at dist=1.0
    float alpha = smoothstep(1.0, 0.3, dist);

    return float4(input.Color.rgb, alpha * input.Color.a);
}

technique EdgeQuad
{
    pass Pass0
    {
        VertexShader = compile VS_SHADERMODEL EdgeQuadVS();
        PixelShader = compile PS_SHADERMODEL EdgeQuadPS();
    }
}
