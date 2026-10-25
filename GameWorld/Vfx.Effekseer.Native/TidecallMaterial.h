#pragma once

inline std::string TidecallParameterHeader()
{
    return R"(
cbuffer AecnTidecallParameters : register(b2) { float4 tideParams[26]; };
#define Tide(i) tideParams[(i) / 4][(i) % 4]
#define Tide2(i) float2(Tide(i), Tide((i) + 1))
#define Tide3(i) float3(Tide(i), Tide((i) + 1), Tide((i) + 2))
#define Tide4(i) float4(Tide(i), Tide((i) + 1), Tide((i) + 2), Tide((i) + 3))
)";
}

inline std::string TidecallVertexDisplacement()
{
    // Shipped main VS: mip-zero blue displacement along the normalized world normal.
    return R"(
    float2 tideDisplacementUV = (uv1 + customData1.z * Tide2(17)) * Tide2(20);
    float tideDisplacement = tideDisplacementTexture.SampleLevel(tideDisplacementSampler, tideDisplacementUV, 0).b;
    worldPos += worldNormal * Tide(19) * tideDisplacement * (1 - uv1.y);
)";
}

inline std::string TidecallCode()
{
    auto sample = [](const std::string& name, int logical, const std::string& uv)
    {
        std::string code = "$F4$ " + name + ";\n";
        for (int slot = 0; slot < 11; ++slot)
        {
            code += slot == 0 ? "if (" : slot == 10 ? "else " : "else if (";
            if (slot < 10) code += "Tide(" + std::to_string(92 + logical) + ") < " + std::to_string(slot + 0.5f) + ") ";
            code += name + " = " + (slot < 6
                ? "$TEX_P" + std::to_string(slot) + "$" + uv + "$TEX_S" + std::to_string(slot) + "$"
                : "AecnTideExtraSample(" + std::to_string(slot - 6) + "," + uv + ")") + ";\n";
        }
        return code;
    };
    auto code = std::string(R"(
        $F1$ sourceTime = customData1.x;
        $F1$ particleAge = customData1.z;
        $F1$ particleAlpha = floor(customData2.a * 1000) * 0.001;
        $F2$ noiseAUV = (uv1 + sourceTime * Tide2(50)) * Tide2(56);
        $F2$ noiseBUV = (uv1 + $F2$(0.2,-0.3) + sourceTime * Tide2(59)) * Tide2(65);
        $F2$ foamAUV = (uv1 + sourceTime * Tide2(23)) * Tide2(29);
        $F2$ foamBUV = (uv1 + sourceTime * Tide2(32)) * Tide2(38);
        $F2$ foamMaskUV = uv1 * Tide2(46);
        $F2$ yMaskUV = (uv1 + Tide2(86) + sourceTime * Tide2(88)) * Tide2(90);
        $F2$ waveUV = (uv1 + Tide2(83) + particleAge * Tide2(79)) * Tide2(81);
        $F2$ dissolveUV = (uv1 + sourceTime * Tide2(5)) * Tide2(7);
        $F2$ dissolveMaskUV = (uv1 + particleAge * Tide2(9)) * Tide2(15);
        $F2$ tideDitherUV = screenUV * tideRender.yz * 0.125;
    )");
    code += sample("noiseA", 7, "noiseAUV");
    code += sample("noiseB", 8, "noiseBUV");
    code += sample("foamA", 4, "foamAUV");
    code += sample("foamB", 5, "foamBUV");
    code += sample("foamMask", 6, "foamMaskUV");
    code += sample("yMask", 11, "yMaskUV");
    code += sample("waveMask", 10, "waveUV");
    code += sample("dissolve", 0, "dissolveUV");
    code += sample("dissolveMask", 1, "dissolveMaskUV");
    code += sample("dither", 3, "tideDitherUV");
    code += R"(
        $F1$ noiseAChannel = dot(noiseA, Tide4(52));
        $F1$ noiseBChannel = dot(noiseB, Tide4(61));
        $F1$ noiseShape = min(pow(abs(noiseAChannel * Tide(49) + noiseBChannel * Tide(58)), Tide(45)), 1);
        $F1$ alpha = lerp(1, noiseShape, foamMask.r) * Tide(78);
        alpha *= lerp(yMask.g, 1 - yMask.g, Tide(85)) * waveMask.r;
        alpha *= lerp(1, particleAlpha, Tide(48));
        $F1$ threshold = lerp(1 - particleAlpha, Tide(77), Tide(76));
        $F1$ field = dot(dissolve, Tide4(1)) * dot(dissolveMask, Tide4(11)) - threshold;
        // Preserve the native sqrt/MAX result for a negative field without propagating NaN.
        $F1$ dissolveEdge = field < 0 ? 0 : ceil(saturate(field)) + max(1 - sqrt(max(field, 0)) * 3.333333333, 0);
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = tideCamera.w > 0.5 ? 1 : abs(dot(viewDirection, tideCamera.xyz));
        $F1$ tideSoftDistance = max(Tide(71) * depthScale, 0.00000001);
        $F1$ depthFade = tideRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, tideSoftDistance) : 1;
        alpha *= lerp(1, depthFade, Tide(0)) * dissolveEdge * particleAlpha;
        $F1$ noiseRed = noiseA.r + noiseB.r;
        $F1$ foamFirst = dot(foamA, Tide4(25)) * Tide(22);
        $F1$ foamSecond = saturate(dot(foamB, Tide4(34)) * Tide(31) - noiseRed);
        $F1$ foamBlend = lerp(foamFirst, foamSecond, foamMask.r);
        $F3$ sea = Tide3(72) * customData2.rgb * (0.05 + 0.95 * (1 - uv1.y));
        // Intrinsic G-buffer base colour; deferred PBR lighting remains a separately diagnosed boundary.
        $F3$ emissive = saturate(lerp(sea, Tide3(40), foamBlend) + Tide(44) * saturate(foamMask.r - noiseRed));
        $F1$ opacity = 1;
        $F1$ opacityMask = alpha >= dither.r ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    return code;
}

inline std::vector<uint8_t> TidecallMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto pointer = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), pointer, pointer + 4); };
    auto string = [&](const char* value) { auto length = std::strlen(value) + 1; integer(static_cast<int32_t>(length)); bytes.insert(bytes.end(), value, value + length); };
    bytes.insert(bytes.end(), {'E','F','K','M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P','R','M','_'}); auto chunk = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    integer(6);
    for (int slot = 0; slot < 6; ++slot)
    {
        auto name = "meshTexture" + std::to_string(slot);
        string(name.c_str()); string(name.c_str()); string("");
        integer(slot); integer(0); integer(0); integer(1); integer(0);
    }
    integer(2);
    for (auto name : {"tideRender", "tideCamera"})
    {
        string(name); string(name);
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunk - 4); std::memcpy(bytes.data() + chunk, &size, 4);
    auto code = TidecallCode();
    bytes.insert(bytes.end(), {'G','E','N','E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
