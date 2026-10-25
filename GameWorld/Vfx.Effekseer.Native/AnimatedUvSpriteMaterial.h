#pragma once

inline std::string AnimatedUvSpriteCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ alphaUV1 = (uv1 + sourceTime * mesh0.xy) * mesh1.xy;
        $F2$ alphaUV2 = (uv1 + sourceTime * mesh0.zw) * mesh1.zw;
        $F4$ alpha1 = $TEX_P0$alphaUV1$TEX_S0$;
        $F4$ alpha2 = $TEX_P1$alphaUV2$TEX_S1$;
        $F2$ globalUV = mesh8.xy + frac(uv1) * mesh8.zw;
        $F4$ globalMask = $TEX_P3$globalUV$TEX_S3$;
        $F2$ mainUV1 = (uv1 + sourceTime * $F2$(mesh3.w, mesh4.x)) * $F2$(mesh4.w, mesh5.x);
        $F2$ mainUV2 = (uv1 + sourceTime * mesh4.yz) * mesh5.yz;
        $F4$ main1 = $TEX_P4$mainUV1$TEX_S4$;
        $F4$ main2 = $TEX_P5$mainUV2$TEX_S5$;
        $F1$ sizeFactor = 1 + mesh5.w * (saturate(customData1.z) - 1);
        $F1$ thickness = mesh6.x * sizeFactor + 0.001;
        $F1$ fade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(thickness, 0.00000001)) : 1;
        $F1$ opacity = saturate(alpha1.r * alpha2.r * globalMask.r * main1.r * main2.r * customData2.a * fade);
        $F2$ noiseUV = mesh2.z * ((uv1 + sourceTime * mesh2.xy) * mesh3.xy + main2.rg);
        $F2$ packedNoiseUV = mesh7.xy + frac(noiseUV) * mesh7.zw;
        $F4$ noise = $TEX_P3$packedNoiseUV$TEX_S3$;
        $F4$ detail = $TEX_P2$noise.rg$TEX_S2$;
        $F3$ detailColour = min(pow(abs(detail.rgb * mesh2.w), mesh6.z), $F3$(1,1,1));
        $F3$ colour = (main1.rgb + main2.rgb + detailColour) * customData2.rgb;
        // Preserve the shipped intrinsic colour terms; final fog and MBOIT remain separate.
        $F3$ premultiplied = colour * mesh3.z * opacity * (1 + opacity);
        $F3$ emissive = premultiplied / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
