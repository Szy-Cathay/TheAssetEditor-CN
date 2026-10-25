#pragma once

inline std::string PremultiplyAnimatedCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F2$ firstUV = (uv1 + sourceTime * mesh0.zw) * $F2$(mesh1.w, mesh2.x);
        $F2$ secondUV = (uv1 + sourceTime * mesh1.xy) * mesh2.yz;
        $F4$ distortion = $TEX_P2$firstUV$TEX_S2$;
        // The first authored UV drives distortion; diffuse one samples its scaled RG output.
        $F2$ diffuseUV = distortion.rg * mesh2.w;
        $F4$ diffuse1 = $TEX_P0$diffuseUV$TEX_S0$;
        $F4$ diffuse2 = $TEX_P1$secondUV$TEX_S1$;
        $F4$ globalMask = $TEX_P3$uv1$TEX_S3$;
        $F2$ maskUV1 = (uv1 + sourceTime * mesh4.xy) * mesh5.xy;
        $F2$ maskUV2 = (uv1 + sourceTime * mesh4.zw) * mesh5.zw;
        $F4$ mask1 = $TEX_P4$maskUV1$TEX_S4$;
        $F4$ mask2 = $TEX_P5$maskUV2$TEX_S5$;
        $F1$ mask = diffuse1.r * diffuse2.r * mask1.r * mask2.r * mesh6.x * pow(abs(globalMask.r * mesh6.w), mesh6.z);
        $F1$ fade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh0.y, 0.00000001)) : 1;
        $F1$ alpha = saturate(mask * fade) * floor(customData2.a * 1000) * 0.001;
        $F3$ colour = pow(abs(diffuse1.rgb + diffuse2.rgb), mesh1.z) * mesh3.yzw * mesh0.x * customData2.rgb;
        $F1$ opacity = saturate(alpha);
        $F3$ emissive = colour * max(alpha, 0) / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
