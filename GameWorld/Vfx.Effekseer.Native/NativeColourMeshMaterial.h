#pragma once

inline std::string NativeColourMeshParameters()
{
    return R"(
cbuffer AecnColourMeshParameters : register(b2) { float4 colourMeshParameters[18]; };
#define Cm(i) colourMeshParameters[(i) / 4][(i) % 4]
#define Cm2(i) float2(Cm(i), Cm((i) + 1))
#define Cm3(i) float3(Cm(i), Cm((i) + 1), Cm((i) + 2))
#define Cm4(i) float4(Cm(i), Cm((i) + 1), Cm((i) + 2), Cm((i) + 3))
)";
}

inline std::string NativeColourMeshDepth()
{
    return R"(
float AecnColourMeshDepthFade(Texture2D sourceDepth, float2 pixel, float3 position, float distance,
    float4 inverse0, float4 inverse1, float4 inverse2, float4 inverse3, float4 viewport)
{
    uint width, height; sourceDepth.GetDimensions(width, height);
    int2 coordinate = clamp(int2(floor(pixel)), int2(0,0), int2(width,height)-1);
    float depth = sourceDepth.Load(int3(coordinate,0)).x;
    float2 ndc = (pixel - viewport.zw) / viewport.xy * float2(2,-2) + float2(-1,1);
    float4 reconstructed = ndc.x * inverse0 + ndc.y * inverse1 + depth * inverse2 + inverse3;
    return saturate(length(reconstructed.xyz / reconstructed.w - position) / distance);
}
)";
}


inline std::string NativeDistortedColourMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ distortionUV = (uv1 * 0.5 + sourceTime * $F2$(0,-0.25) * Cm2(20)) * Cm2(18);
        $F4$ distortion = $TEX_P4$distortionUV$TEX_S4$;
        $F2$ distortedUV = uv1 + (distortion.xy * 2 - 1) * 0.02;
        $F2$ uvA = (uv1 + sourceTime * Cm2(41)) * Cm2(47) + distortedUV;
        $F2$ uvB = (uv1 + sourceTime * Cm2(43)) * Cm2(49) + distortedUV;
        $F2$ uvC = (uv1 + sourceTime * Cm2(45)) * Cm2(51) + distortedUV;
        $F4$ sampleA = $TEX_P0$uvA$TEX_S0$;
        $F4$ sampleB = $TEX_P1$uvB$TEX_S1$;
        $F4$ sampleC = $TEX_P2$uvC$TEX_S2$;
        // Each mask lerps from a colour, using the sampled green channel plus authored bias.
        $F3$ mask = lerp(Cm3(32), dot(sampleA,Cm4(0)) * Cm(38), Cm(0) + Cm(22));
        mask *= lerp(Cm3(32), dot(sampleB,Cm4(4)) * Cm(39), sampleB.g + Cm(22));
        mask *= lerp(Cm3(32), dot(sampleC,Cm4(8)) * Cm(40), sampleC.g + Cm(22));
        $F1$ colourMeshSoftDistance = max(Cm(37), 0.00000001);
        $F1$ depthFade = colourMeshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, colourMeshSoftDistance) : 1;
        $F1$ ndv = abs(dot(normalize(cameraPosition.xyz-worldPos), normalize(worldNormal)));
        $F1$ facing = lerp(pow(abs(1-ndv), Cm(36)), pow(ndv,Cm(36)), Cm(35));
        $F1$ globalTime = lerp(sourceTime, customData1.z, Cm(27));
        $F2$ globalUV = (uv1 + globalTime * Cm2(23)) * Cm2(25);
        $F4$ globalMask = $TEX_P5$globalUV$TEX_S5$;
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, Cm(55));
        $F1$ alpha = floor(customData2.a*1000) * 0.001;
        mask = max(mask * lerp(1,depthFade,Cm(17)) * facing * globalMask.r * Cm(53)
            * pow(abs(vertexColour.a),Cm(54)) * lerp(1,alpha,Cm(56)), 0);
        $F2$ colourUV = (uv1 + sourceTime * Cm2(12)) * Cm2(15);
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ emissive = mask * overlay.rgb * (1 + overlay.a*(Cm(14)-1))
            * Cm3(28) * Cm(31) * vertexColour.rgb * customData2.rgb;
        $F1$ opacity = mask.r;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}

inline std::string NativeColourMeshCode(int mode)
{
    if (mode == 3) return NativeDistortedColourMeshCode();
    const bool bubble = mode == 0, lightning = mode == 1;
    std::string code = R"(
        $F1$ sourceTime = customData1.x;
        $F1$ age = customData1.z;
    )";
    code += bubble ? R"(
        $F2$ uvA = (uv1 + sourceTime * Cm2(27)) * Cm2(31);
        $F2$ uvB = (uv1 + sourceTime * Cm2(29)) * Cm2(33);
        $F4$ sampleA = $TEX_P0$uvA$TEX_S0$;
        $F4$ sampleB = $TEX_P1$uvB$TEX_S1$;
        $F1$ mask = dot(sampleA,Cm4(0)) * Cm(25) * dot(sampleB,Cm4(4)) * Cm(26);
        $F1$ colourMeshSoftDistance = max(Cm(24), 0.00000001);
        $F1$ fadeSwitch = Cm(13);
        $F1$ facingMode = Cm(22), facingPower = Cm(23);
        $F2$ globalUV = (uv1 + sourceTime * Cm2(14)) * Cm2(16);
        $F4$ globalMask = $TEX_P3$globalUV$TEX_S3$;
    )" : R"(
        $F2$ uvA = (uv1 + sourceTime * Cm2(32)) * Cm2(38);
        $F2$ uvB = (uv1 + sourceTime * Cm2(34)) * Cm2(40);
        $F2$ uvC = (uv1 + sourceTime * Cm2(36)) * Cm2(42);
        $F4$ sampleA = $TEX_P0$uvA$TEX_S0$;
        $F4$ sampleB = $TEX_P1$uvB$TEX_S1$;
        $F4$ sampleC = $TEX_P2$uvC$TEX_S2$;
        $F1$ mask = dot(sampleA,Cm4(0)) * Cm(29) * dot(sampleB,Cm4(4)) * Cm(30) * dot(sampleC,Cm4(8)) * Cm(31);
        $F1$ colourMeshSoftDistance = max(Cm(28), 0.00000001);
        $F1$ fadeSwitch = Cm(17);
        $F1$ facingMode = Cm(26), facingPower = Cm(27);
    )";
    if (!bubble)
    {
        code += lightning ? "$F2$ globalUV = (uv1 + age * Cm2(18)) * Cm2(20);\n"
            : "$F2$ globalUV = (uv1 + sourceTime * Cm2(18)) * Cm2(20);\n";
        code += "$F4$ globalMask = $TEX_P4$globalUV$TEX_S4$;\n";
    }
    code += R"(
        $F1$ depthFade = colourMeshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, colourMeshSoftDistance) : 1;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ ndv = abs(dot(viewDirection, normalize(worldNormal)));
        $F1$ facing = lerp(pow(abs(1-ndv), facingPower), pow(ndv, facingPower), facingMode);
        mask *= lerp(1, depthFade, fadeSwitch) * facing * globalMask.r;
        $F1$ particleAlpha = floor(customData2.a * 1000) * 0.001;
    )";
    code += bubble ? R"(
        mask *= Cm(35);
        $F2$ colourUV = (uv1 + sourceTime * Cm2(8)) * Cm2(11);
        $F4$ overlay = $TEX_P2$colourUV$TEX_S2$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (Cm(10)-1)) * Cm3(18) * Cm(21) * customData2.rgb;
        $F1$ opacity = max(mask * lerp(1, particleAlpha, Cm(36)), 0);
        // The bubble graph includes its pre-instance mask a second time in RGB.
        $F3$ emissive = colour * mask * opacity;
    )" : R"(
        $F2$ colourUV = (uv1 + sourceTime * Cm2(12)) * Cm2(15);
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (Cm(14)-1)) * Cm3(22) * Cm(25) * customData2.rgb;
    )";
    if (!bubble)
    {
        code += lightning ? R"(
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, Cm(45));
        mask *= Cm(44) * abs(vertexColour.a) * abs(vertexColour.a);
        $F1$ opacity = max(mask * lerp(1, particleAlpha, Cm(46)), 0);
    )" : R"(
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, Cm(46));
        mask *= Cm(44) * pow(abs(vertexColour.a), Cm(45));
        $F1$ opacity = max(mask * lerp(1, particleAlpha, Cm(47)), 0);
    )";
        code += "$F3$ emissive = colour * vertexColour.rgb * opacity;\n";
    }
    code += R"(
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    return code;
}

inline std::vector<uint8_t> NativeColourMeshMaterial(int mode)
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p+4); };
    auto string = [&](const char* value) { auto size = std::strlen(value)+1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(),value,value+size); };
    bytes.insert(bytes.end(), {'E','F','K','M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P','R','M','_'}); auto chunk = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    const int textures = mode == 0 ? 4 : mode == 3 ? 6 : 5; integer(textures);
    for (int index=0; index<textures; ++index)
    {
        auto name="colourMeshTexture"+std::to_string(index); string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0);
    }
    integer(6);
    for (auto name : {"colourMeshRender","colourMeshInverse0","colourMeshInverse1","colourMeshInverse2","colourMeshInverse3","colourMeshViewport"})
    {
        string(name); string(name); integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size=static_cast<int32_t>(bytes.size()-chunk-4); std::memcpy(bytes.data()+chunk,&size,4);
    const auto code=NativeColourMeshCode(mode);
    bytes.insert(bytes.end(), {'G','E','N','E'}); integer(static_cast<int32_t>(code.size()+5)); string(code.c_str());
    return bytes;
}
