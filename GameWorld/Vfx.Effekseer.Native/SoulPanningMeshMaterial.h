#pragma once

// vfx_soulpanning's shipped PS: numeric maps, crossed noise pans and additive RGB.
std::string SoulPanningMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ sourceUV = mesh1.x > 0.5 ? worldPos.xy : uv1;
        $F2$ noiseUV1 = (sourceUV + sourceTime * mesh1.yz) * $F2$(mesh1.w, mesh2.x);
        $F2$ noiseUV2 = (sourceUV + sourceTime * mesh2.yz) * $F2$(mesh2.w, mesh3.x);
        $F3$ noise1 = $TEX_P2$noiseUV1$TEX_S2$.rgb;
        $F3$ noise2 = $TEX_P1$noiseUV2$TEX_S1$.rgb;
        $F3$ gradient = $TEX_P3$uv1$TEX_S3$.rgb;
        $F2$ baseUV = (sourceUV + sourceTime * mesh3.yz) * $F2$(mesh3.w, mesh4.x);
        $F4$ baseColour = $TEX_P0$baseUV$TEX_S0$;
        $F3$ colour = baseColour.rgb * sqrt(baseColour.a) * (noise1 + noise2) * 0.5 * gradient;
        colour *= mesh0.y * customData2.rgb * floor(customData2.a * 1000) * 0.001;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = mesh4.y > 0.5 ? 1 : abs(dot(viewDirection, meshRender.yzw));
        $F1$ depthFade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh0.x * depthScale, 0.00000001)) : 1;
        $F1$ ndv = max(dot(worldNormal, viewDirection), 0);
        $F1$ response = (1 - mesh0.z) * (1 - pow(abs(1 - ndv), mesh0.w));
        colour *= depthFade * response;
        $F1$ opacity = 0.00001;
        $F3$ emissive = colour / opacity;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
