#pragma once

inline std::string GrowingVinesCode()
{
    return R"(
        $F4$ diffuse = $TEX_P0$uv1$TEX_S0$;
        $F4$ gradient = $TEX_P2$uv1$TEX_S2$;
        // Native instance alpha is stored at 1/1000 precision. It drives clipping, not blending.
        $F1$ alpha = floor(customData2.a * 1000) * 0.001;
        $F1$ trunk = saturate(alpha / 0.6) - (1 - uv1.y);
        $F1$ leaves = saturate(2 * (alpha - mesh2.z)) - (1 - gradient.r) - (1 - diffuse.a) - mesh2.w;
        $F1$ field = lerp(trunk, leaves, mesh2.y);
        $F4$ normalTexture = $TEX_P4$uv1$TEX_S4$;
        $F2$ normalXY = (normalTexture.ag + 0.0019607843) * 2 - 1;
        $F3$ normal = normalize(normalize(worldTangent) * normalXY.x + normalize(worldBinormal) * normalXY.y
            + normalize(worldNormal) * sqrt(max(1 - dot(normalXY, normalXY), 0)));
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ fresnel = pow(abs(1 - dot(viewDirection, normal)), mesh2.x);
        $F2$ maskUV = (uv1 + customData1.x * $F2$(mesh0.w, mesh1.x)) * mesh1.yz;
        $F4$ mask = $TEX_P1$maskUV$TEX_S1$;
        $F4$ materialMap = $TEX_P3$uv1$TEX_S3$;
        $F3$ albedo = saturate(diffuse.rgb * mesh0.xyz);
        $F1$ emission = saturate(fresnel * mesh1.w * mask.r);
        // Match native G-buffer precision and the deferred emissive decode.
        emission = floor(emission * 1023 + 0.5) / 1023;
        $F1$ metalness = floor(saturate(materialMap.r) * 255 + 0.5) / 255;
        // The intrinsic base colour remains a fallback until deferred PBR is supported.
        $F3$ emissive = albedo + albedo * (1 - metalness) * emission * 255;
        $F1$ opacity = 1;
        $F1$ opacityMask = field >= 0 ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
