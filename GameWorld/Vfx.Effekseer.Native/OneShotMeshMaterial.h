#pragma once

// Zero colour-distortion branch of wh3_vfx_one_shot_mesh's shipped pixel shader.
std::string OneShotMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ uvA = (uv1 + sourceTime * $F2$(mesh6.w, mesh7.x)) * mesh7.yz;
        $F2$ uvB = (uv1 + sourceTime * mesh9.xy) * mesh9.zw;
        $F2$ uvC = (uv1 + sourceTime * mesh11.yz) * $F2$(mesh11.w, mesh12.x);
        $F4$ a = $TEX_P0$uvA$TEX_S0$;
        $F4$ b = $TEX_P1$uvB$TEX_S1$;
        $F4$ c = $TEX_P2$uvC$TEX_S2$;
        $F1$ mask = dot(a, $F4$(mesh5.w, mesh6.xyz)) * mesh5.z
            * dot(b, mesh8) * mesh7.w * dot(c, $F4$(mesh10.yzw, mesh11.x)) * mesh10.x;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = mesh13.z > 0.5 ? 1 : abs(dot(viewDirection, meshRender.yzw));
        $F1$ depthFade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh1.z * depthScale, 0.00000001)) : 1;
        mask *= lerp(1, depthFade, mesh1.w);
        mask = pow(abs(mask), mesh12.y);
        $F1$ ndv = saturate(abs(dot(viewDirection, normalize(worldNormal))));
        mask *= lerp(pow(1 - ndv, mesh4.y), pow(ndv, mesh4.y), mesh4.x);
        $F2$ globalUV = (uv1 + sourceTime * mesh2.xy) * mesh2.zw;
        $F4$ globalMask = $TEX_P5$globalUV$TEX_S5$;
        $F2$ oneShotUV = (uv1 + customData1.y * mesh4.zw) * mesh5.xy;
        oneShotUV.y = saturate(oneShotUV.y);
        $F4$ oneShotMask = $TEX_P4$oneShotUV$TEX_S4$;
        mask *= globalMask.r * saturate(oneShotMask.r);
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, mesh13.x);
        mask *= mesh12.z * pow(abs(vertexColour.a), mesh12.w);
        mask *= lerp(1, floor(customData2.a * 1000) * 0.001, mesh13.y);
        $F2$ colourUV = (uv1 + sourceTime * mesh0.yz) * mesh1.xy;
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (mesh0.w - 1));
        colour *= vertexColour.rgb * mesh3.xyz * mesh3.w * customData2.rgb;
        $F1$ opacity = saturate(mask);
        $F3$ emissive = colour * max(mask, 0) / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
