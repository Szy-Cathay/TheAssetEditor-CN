#pragma once

// Zero colour-distortion branch of wh3_vfx_basic_mesh's shipped pixel shader.
std::string BasicMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ uvA = (uv1 + sourceTime * mesh3.xy) * mesh3.zw;
        $F2$ uvB = (uv1 + sourceTime * mesh5.yz) * $F2$(mesh5.w, mesh6.x);
        $F2$ uvC = (uv1 + sourceTime * mesh7.zw) * mesh8.xy;
        $F4$ a = $TEX_P0$uvA$TEX_S0$;
        $F4$ b = $TEX_P1$uvB$TEX_S1$;
        $F4$ c = $TEX_P2$uvC$TEX_S2$;
        $F1$ mask = dot(a, mesh2) * mesh1.w
            * dot(b, $F4$(mesh4.yzw, mesh5.x)) * mesh4.x
            * dot(c, $F4$(mesh6.zw, mesh7.xy)) * mesh6.y;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = mesh13.w > 0.5 ? 1 : abs(dot(viewDirection, meshRender.yzw));
        $F1$ depthFade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh1.y * depthScale, 0.00000001)) : 1;
        mask *= lerp(1, depthFade, mesh1.z);
        mask = pow(abs(mask), mesh12.z);
        $F1$ ndv = saturate(abs(dot(viewDirection, normalize(worldNormal))));
        mask *= lerp(pow(1 - ndv, mesh12.y), pow(ndv, mesh12.y), mesh12.x);
        $F2$ uvMask1 = (uv1 + sourceTime * $F2$(mesh9.w, mesh10.x)) * mesh10.yz;
        $F2$ uvMask2 = (uv1 + sourceTime * $F2$(mesh10.w, mesh11.x)) * mesh11.yz;
        $F4$ mask1 = $TEX_P4$uvMask1$TEX_S4$;
        $F4$ mask2 = $TEX_P5$uvMask2$TEX_S5$;
        mask *= saturate(mask1.r * mask2.r * mesh11.w);
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, mesh13.y);
        mask *= mesh12.w * pow(abs(vertexColour.a), mesh13.x);
        mask *= lerp(1, floor(customData2.a * 1000) * 0.001, mesh13.z);
        $F2$ colourUV = (uv1 + sourceTime * mesh0.xy) * $F2$(mesh0.w, mesh1.x);
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (mesh0.z - 1));
        colour *= vertexColour.rgb * $F3$(mesh8.w, mesh9.xy) * mesh9.z * customData2.rgb;
        $F1$ opacity = saturate(mask);
        $F3$ emissive = colour * max(mask, 0) / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
