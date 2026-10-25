#pragma once

inline std::string BasicSpriteCode()
{
    return R"(
        $F4$ diffuse = $TEX_P2$uv1$TEX_S2$;
        $F1$ textureAlpha = pow(abs(diffuse.a), 0.454);
        $F1$ thickness = mesh4.y * (1 + mesh4.x * (saturate(customData1.z) - 1)) + 0.001;
        $F1$ fade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(thickness, 0.00000001)) : 1;
        $F1$ opacity = saturate(textureAlpha * customData2.a * fade);
        $F2$ noiseUV = (uv1 + customData1.x * mesh1.yz) * mesh2.xy;
        $F4$ noise = $TEX_P0$noiseUV$TEX_S0$;
        $F2$ maskUV = uv1 + (noise.rg * 2 - 1) * mesh1.w;
        $F4$ maskTexture = $TEX_P1$maskUV$TEX_S1$;
        $F1$ mask = pow(abs(lerp(maskTexture.r, diffuse.r, mesh3.y)), mesh2.z);
        $F3$ multiplyColour = pow(abs(diffuse.rgb * customData2.rgb), mesh0.x) * textureAlpha;
        $F3$ gammaTexture = pow(max(diffuse.rgb, $F3$(0,0,0)), 1.0 / 2.2);
        $F3$ gammaVertex = pow(max(customData2.rgb, $F3$(0,0,0)), 1.0 / 2.2);
        $F3$ gammaProduct = gammaTexture * gammaVertex;
        $F3$ overlayLow = 2 * gammaProduct;
        $F3$ overlayHigh = 1 - 2 * (1 - gammaTexture) * (1 - gammaVertex);
        $F1$ overlaySwitch = ceil(saturate(dot(diffuse.rgb, $F3$(0.3,0.59,0.11)) - 0.269));
        $F3$ overlayColour = pow(saturate(lerp(overlayLow, overlayHigh, overlaySwitch)), 2.2 * mesh0.y);
        $F3$ colour = lerp(multiplyColour, overlayColour, mesh3.z);
        colour += diffuse.rgb * $F3$(mesh0.zw, mesh1.x) * mask * mesh2.w;
        // Intrinsic colour only; the native fog and MBOIT resolve are not reproduced here.
        $F3$ emissive = colour;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
