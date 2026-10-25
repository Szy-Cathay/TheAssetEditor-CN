#pragma once

inline std::string PrelitSpriteCode()
{
    return R"(
        $F4$ diffuse = $TEX_P3$uv1$TEX_S3$;
        $F4$ materialMap = $TEX_P2$uv1$TEX_S2$;
        $F1$ textureAlpha = pow(abs(diffuse.a), 0.454);
        $F1$ thickness = mesh5.x * (1 + mesh4.w * (saturate(customData1.z) - 1)) + 0.001;
        $F1$ fade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(thickness, 0.00000001)) : 1;
        $F1$ coverage = saturate(textureAlpha * customData2.a * fade);
        $F3$ multiplyColour = pow(abs(diffuse.rgb * customData2.rgb), mesh0.x) * textureAlpha;
        $F3$ gammaTexture = pow(max(diffuse.rgb, $F3$(0,0,0)), 1.0 / 2.2);
        $F3$ gammaVertex = pow(max(customData2.rgb, $F3$(0,0,0)), 1.0 / 2.2);
        $F3$ overlayLow = 2 * gammaTexture * gammaVertex;
        $F3$ overlayHigh = 1 - 2 * (1 - gammaTexture) * (1 - gammaVertex);
        $F1$ overlaySwitch = ceil(saturate(dot(diffuse.rgb, $F3$(0.3,0.59,0.11)) - 0.269));
        $F3$ overlayColour = pow(saturate(lerp(overlayLow, overlayHigh, overlaySwitch)), 2.2 * mesh0.y);
        $F3$ colour = lerp(multiplyColour, overlayColour, mesh4.z) * (1 - saturate(materialMap.r));
        $F2$ noiseUV = (uv1 + customData1.x * mesh2.yz) * mesh3.xy;
        $F4$ noise = $TEX_P0$noiseUV$TEX_S0$;
        $F2$ maskUV = uv1 + (noise.rg * 2 - 1) * mesh2.w;
        $F4$ maskTexture = $TEX_P1$maskUV$TEX_S1$;
        // The shipped non-sun-scaled PS uses green and the tint multiplier, not emissive_colour.
        $F3$ glow = maskTexture.g * mesh1.y * $F3$(mesh1.zw, mesh2.x) * customData2.a * textureAlpha;
        $F1$ opacity = saturate(coverage + 0.0005 * mesh3.w * maskTexture.g * customData2.a * textureAlpha);
        // Preserve the intrinsic source terms; scene PBR, fog and MBOIT resolve remain diagnosed.
        $F3$ emissive = (colour * coverage + glow) / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
