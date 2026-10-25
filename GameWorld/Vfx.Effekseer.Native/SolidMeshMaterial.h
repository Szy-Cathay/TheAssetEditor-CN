#pragma once

inline std::string SolidMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ uvA = (uv1 + sourceTime * mesh6.xy) * mesh6.zw;
        $F2$ uvB = (uv1 + sourceTime * mesh8.xy) * mesh8.zw;
        $F2$ uvC = (uv1 + sourceTime * mesh10.yz) * $F2$(mesh10.w, mesh11.x);
        $F4$ maskA = $TEX_P0$uvA$TEX_S0$;
        $F4$ maskB = $TEX_P1$uvB$TEX_S1$;
        $F4$ maskC = $TEX_P2$uvC$TEX_S2$;
        // The shipped PS applies texture_a_brightness to both A and B.
        $F1$ mask = dot(maskA, mesh5) * mesh4.w * dot(maskB, mesh7) * mesh4.w;
        mask *= dot(maskC, $F4$(mesh9.yzw, mesh10.x)) * mesh9.x;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = mesh12.y > 0.5 ? 1 : abs(dot(viewDirection, meshRender.yzw));
        $F1$ depthFade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh4.z * depthScale, 0.00000001)) : 1;
        mask *= lerp(1, depthFade, mesh1.y);
        $F2$ globalUV = (uv1 + sourceTime * mesh1.zw) * mesh2.xy;
        $F4$ globalMask = $TEX_P4$globalUV$TEX_S4$;
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, mesh12.x);
        mask *= globalMask.r * mesh11.z * pow(abs(vertexColour.a), mesh11.w);
        mask *= floor(customData2.a * 1000) * 0.001;
        // Constant-white dither is a hard threshold; clipped pixels must not write depth.
        $F1$ field = mask - 1;
        $F1$ positive = ceil(saturate(field));
        $F4$ overlay = $TEX_P3$((uv1 + sourceTime * mesh0.xy) * $F2$(mesh0.w, mesh1.x))$TEX_S3$;
        $F3$ colour = overlay.rgb * lerp(1, mesh0.z, overlay.a) * vertexColour.rgb * customData2.rgb;
        $F3$ glow = colour * $F3$(mesh2.zw, mesh3.x) * mesh3.y;
        $F3$ hdr = field * positive * glow;
        $F1$ normalizer = max(1, max(hdr.r, max(hdr.g, hdr.b)));
        $F3$ albedo = 1 - (1 - saturate(colour)) * (1 - hdr / normalizer);
        // Preserve the shipped G-buffer albedo branch; deferred lighting remains a candidate.
        $F3$ emissive = saturate(lerp(albedo, field * positive * glow, positive));
        $F1$ opacity = 1;
        $F1$ opacityMask = field >= 0 ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
