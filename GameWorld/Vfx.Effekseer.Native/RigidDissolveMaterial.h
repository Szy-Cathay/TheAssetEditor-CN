#pragma once

inline std::string LegacyRigidDissolveParameterHeader()
{
    std::string header = "cbuffer AecnLegacyDissolveParameters : register(b2) {\n";
    for (int index = 0; index < 17; ++index)
        header += "float4 legacy" + std::to_string(index) + ";\n";
    return header + "};\n";
}

inline std::string LegacyRigidDissolveCode()
{
    auto sample = [](const std::string& name, const std::string& alias, const std::string& uv)
    {
        std::string code;
        for (int slot = 0; slot < 6; ++slot)
        {
            code += slot == 0 ? "if (" : slot == 5 ? "else " : "else if (";
            if (slot < 5) code += alias + " < " + std::to_string(slot + 0.5f) + ") ";
            code += name + " = $TEX_P" + std::to_string(slot) + "$" + uv + "$TEX_S" + std::to_string(slot) + "$;\n";
        }
        return code;
    };
    auto code = std::string(R"(
        $F1$ age = customData1.z;
        $F1$ sourceTime = customData1.x;
        $F2$ instanceUV = uv1 + customData1.yw;
        $F2$ dissolveUV = (instanceUV + sourceTime * $F2$(legacy0.w, legacy1.x)) * legacy1.yz;
        $F2$ noiseUV = (uv1 + age * $F2$(legacy1.w, legacy2.x)) * legacy2.yz;
        $F2$ globalUV = (uv1 + sourceTime * legacy11.yz) * $F2$(legacy11.w, legacy12.x);
        $F2$ uvA = (uv1 + age * legacy5.xy) * legacy5.zw;
        $F2$ uvB = (instanceUV + sourceTime * legacy7.yz) * $F2$(legacy7.w, legacy8.x);
        $F2$ uvC = (instanceUV + sourceTime * legacy9.zw) * legacy10.xy;
        $F2$ rigidDitherUV = screenUV * meshRender.xy * 0.125;
        $F4$ baseColour, dissolve, noise, dither, a, b, c, globalMask;
        if (legacy16.w > 0.5)
        {
            baseColour = $TEX_P0$uv1$TEX_S0$;
            dither = $TEX_P1$rigidDitherUV$TEX_S1$;
            globalMask = $TEX_P2$globalUV$TEX_S2$;
            c = $TEX_P3$uvC$TEX_S3$;
            b = $TEX_P4$uvB$TEX_S4$;
            $F2$ packedDissolveUV = frac(dissolveUV) * legacy13.zw + legacy13.xy;
            $F2$ packedNoiseUV = frac(noiseUV) * legacy14.zw + legacy14.xy;
            $F2$ packedEmissionUV = frac(uvA) * legacy15.zw + legacy15.xy;
            dissolve = $TEX_P5$packedDissolveUV$TEX_S5$;
            noise = $TEX_P5$packedNoiseUV$TEX_S5$;
            a = $TEX_P5$packedEmissionUV$TEX_S5$;
        }
        else
        {
    )");
    code += sample("baseColour", "legacy14.x", "uv1");
    code += sample("dissolve", "legacy14.y", "dissolveUV");
    code += sample("noise", "legacy14.z", "noiseUV");
    code += sample("dither", "legacy14.w", "rigidDitherUV");
    code += sample("a", "legacy15.x", "uvA");
    code += sample("b", "legacy15.y", "uvB");
    code += sample("c", "legacy15.z", "uvC");
    code += sample("globalMask", "legacy15.w", "globalUV");
    code += R"(
        }
        $F1$ alpha = floor(customData2.a * 1000) * 0.001;
        $F1$ threshold = lerp(1 - alpha, legacy12.w, legacy12.z);
        $F1$ field = dissolve.b * noise.r - threshold;
        $F1$ edge = field >= 0 ? max(1 - sqrt(max(field, 0)) * 3.333333, 0) : 0;
        $F1$ coverage = globalMask.r * (ceil(saturate(field)) + edge);
        $F4$ ditherMask = ceil(globalMask.r * (coverage - dither.rrgb));
        $F1$ emission = dot(a, legacy4) * legacy3.w
            * dot(b, $F4$(legacy6.yzw, legacy7.x)) * legacy6.x
            * dot(c, $F4$(legacy8.zw, legacy9.xy)) * legacy8.y;
        $F3$ glow = emission * $F3$(legacy10.zw, legacy11.x)
            + edge * $F3$(legacy2.w, legacy3.xy) * legacy3.z;
        glow *= max(ditherMask.yzw, 0);
        emissive = saturate(baseColour.rgb * legacy0.xyz) + glow;
        opacityMask = coverage - dither.r - legacy12.y >= 0 ? 1 : 0;
    )";
    return code;
}

inline std::string RigidDissolveCode()
{
    // Alias shared resources, or pack three independent masks for distinct authored bindings.
    auto sample = [](const std::string& name, const std::string& alias, const std::string& uv)
    {
        std::string code;
        for (int slot = 0; slot < 6; ++slot)
        {
            code += slot == 0 ? "if (" : slot == 5 ? "else " : "else if (";
            if (slot < 5) code += alias + " < " + std::to_string(slot + 0.5f) + ") ";
            code += name + " = $TEX_P" + std::to_string(slot) + "$" + uv + "$TEX_S" + std::to_string(slot) + "$;\n";
        }
        return code;
    };
    auto code = std::string(R"(
        $F3$ emissive;
        $F1$ opacityMask;
        if (legacy16.z > 0.5)
        {
    )") + LegacyRigidDissolveCode() + R"(
        }
        else
        {
        $F1$ age = customData1.z;
        $F2$ dissolveUV = (uv1 + mesh1.yz + age * $F2$(mesh0.w, mesh1.x)) * $F2$(mesh1.w, mesh2.x);
        if (mesh14.z > 0.5)
            dissolveUV = (uv1 + customData1.yw + customData1.x * $F2$(mesh0.w, mesh1.x)) * $F2$(mesh1.w, mesh2.x);
        $F2$ noiseUV = (uv1 + age * mesh2.yz) * $F2$(mesh2.w, mesh3.x);
        $F2$ rigidDitherUV = screenUV * meshRender.xy * 0.125;
        $F2$ globalUV = (uv1 + customData1.x * $F2$(mesh9.w, mesh10.x)) * mesh10.yz;
        $F2$ uvA = (uv1 + age * mesh5.zw) * mesh6.xy;
        $F2$ uvB = (uv1 + customData1.yw + customData1.x * $F2$(mesh7.w, mesh8.x)) * mesh8.yz;
        $F4$ baseColour, dissolve, noise, dither, a, b, emissionMask, globalMask;
        if (mesh14.w > 0.5)
        {
            $F2$ packedDissolveUV = frac(dissolveUV) * mesh12.xy + mesh11.zw;
            $F2$ packedNoiseUV = frac(noiseUV) * mesh13.xy + mesh12.zw;
            $F2$ packedEmissionUV = frac(uvA) * mesh14.xy + mesh13.zw;
            baseColour = $TEX_P0$uv1$TEX_S0$;
            dither = $TEX_P1$rigidDitherUV$TEX_S1$;
            globalMask = $TEX_P2$globalUV$TEX_S2$;
            emissionMask = $TEX_P3$uv1$TEX_S3$;
            b = $TEX_P4$uvB$TEX_S4$;
            dissolve = $TEX_P5$packedDissolveUV$TEX_S5$;
            noise = $TEX_P5$packedNoiseUV$TEX_S5$;
            a = $TEX_P5$packedEmissionUV$TEX_S5$;
        }
        else
        {
    )";
    code += sample("baseColour", "mesh12.x", "uv1");
    code += sample("dissolve", "mesh12.y", "dissolveUV");
    code += sample("noise", "mesh12.z", "noiseUV");
    code += sample("dither", "mesh12.w", "rigidDitherUV");
    code += sample("a", "mesh13.x", "uvA");
    code += sample("b", "mesh13.y", "uvB");
    code += sample("emissionMask", "mesh13.z", "uv1");
    code += sample("globalMask", "mesh13.w", "globalUV");
    code += R"(
        }
        $F1$ alpha = floor(customData2.a * 1000) * 0.001;
        $F1$ threshold = lerp(1 - alpha, mesh11.y, mesh11.x);
        $F1$ field = dissolve.b * noise.r - threshold;
        // DXBC max(NaN, 0) removes the edge on the discarded side of the field.
        $F1$ edge = field >= 0 ? max(1 - sqrt(max(field, 0)) * 3.333333, 0) : 0;
        $F1$ coverage = globalMask.r * (ceil(saturate(field)) + edge);
        $F4$ ditherMask = ceil(globalMask.r * (coverage - dither.rrgb));
        $F1$ emission = dot(a, $F4$(mesh4.zw, mesh5.xy)) * mesh4.y
            * dot(b, $F4$(mesh6.w, mesh7.xyz)) * mesh6.z * emissionMask.a;
        $F3$ glow = emission * mesh8.w * mesh9.xyz * customData2.rgb + edge * mesh3.yzw * mesh4.x;
        glow *= max(ditherMask.yzw, 0);
        // Intrinsic colour/emission only; the deferred lighting pass remains diagnosed separately.
        emissive = saturate(baseColour.rgb * mesh0.xyz) + glow;
        if (mesh14.z > 0.5)
        {
            // The 176-byte shader blends its encoded G-buffer colour with its original emission mask.
            $F1$ peak = max(glow.r, max(glow.g, glow.b));
            $F1$ scale = 1 + floor(saturate(peak)) * (peak - 1);
            $F3$ intrinsic = 1 - (1 - baseColour.rgb * mesh0.xyz) * (1 - glow / scale);
            emissive = saturate(lerp(intrinsic, glow, min(emission * ditherMask.x, 1)));
        }
        opacityMask = coverage - dither.r - mesh10.w >= 0 ? 1 : 0;
        }
        $F1$ opacity = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    return code;
}
