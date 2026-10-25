#pragma once

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
        $F1$ age = customData1.z;
        $F2$ dissolveUV = (uv1 + mesh1.yz + age * $F2$(mesh0.w, mesh1.x)) * $F2$(mesh1.w, mesh2.x);
        $F2$ noiseUV = (uv1 + age * mesh2.yz) * $F2$(mesh2.w, mesh3.x);
        $F2$ rigidDitherUV = screenUV * meshRender.xy * 0.125;
        $F2$ globalUV = (uv1 + customData1.x * $F2$(mesh9.w, mesh10.x)) * mesh10.yz;
        $F2$ uvA = (uv1 + age * mesh5.zw) * mesh6.xy;
        $F2$ uvB = (uv1 + customData1.x * $F2$(mesh7.w, mesh8.x)) * mesh8.yz;
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
    )");
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
        $F1$ field = dissolve.b * noise.g - threshold;
        // DXBC max(NaN, 0) removes the edge on the discarded side of the field.
        $F1$ edge = field >= 0 ? max(1 - sqrt(max(field, 0)) * 3.333333, 0) : 0;
        $F1$ coverage = globalMask.g * (ceil(saturate(field)) + edge);
        $F4$ ditherMask = ceil(globalMask.g * (coverage - dither.rrgb));
        $F1$ emission = dot(a, $F4$(mesh4.zw, mesh5.xy)) * mesh4.y
            * dot(b, $F4$(mesh6.w, mesh7.xyz)) * mesh6.z * emissionMask.r;
        $F3$ glow = emission * mesh8.w * mesh9.xyz * customData2.rgb + edge * mesh3.yzw * mesh4.x;
        glow *= max(ditherMask.yzw, 0);
        // Intrinsic colour/emission only; the deferred lighting pass remains diagnosed separately.
        $F3$ emissive = saturate(baseColour.rgb * mesh0.xyz) + glow;
        $F1$ opacity = 1;
        $F1$ opacityMask = coverage - dither.r - mesh10.w >= 0 ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    return code;
}
