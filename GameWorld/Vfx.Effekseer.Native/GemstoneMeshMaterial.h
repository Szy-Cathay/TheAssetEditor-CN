#pragma once

// Opaque branch: authored colour and particle-alpha dissolve, before game deferred lighting.
std::string GemstoneMeshCode()
{
    return R"(
        $F4$ detail = $TEX_P0$(uv1 * mesh3.zw)$TEX_S0$;
        $F4$ dissolveMap = $TEX_P1$uv1$TEX_S1$;
        $F1$ noise = saturate((detail.r + dissolveMap.r) * 0.5);
        noise = noise * noise * (3 - 2 * noise) + 0.1;
        $F1$ dissolveAge = 1 - floor(saturate(customData2.a) * 1000) * 0.001;
        $F1$ extent = 1.1 + 0.5 * saturate(2 * mesh2.y - mesh2.x);
        $F1$ field = noise - dissolveAge * extent;
        $F1$ visible = dissolveAge > 0 && field + mesh2.x < 0 ? 0 : 1;
        $F1$ edgeDistance = mesh2.x - 2 * field;
        $F1$ edgeWeight = saturate((edgeDistance + 1) / (mesh1.x + 1));
        edgeWeight = edgeWeight * edgeWeight * (3 - 2 * edgeWeight);
        $F3$ edgeColour = lerp(mesh1.yzw, $F3$(mesh2.w, mesh3.xy), edgeWeight);
        $F1$ edgeEnd = saturate((edgeDistance - mesh2.y) / mesh2.y);
        edgeEnd = edgeEnd * edgeEnd * (3 - 2 * edgeEnd);
        $F3$ edge = edgeColour * max(saturate(edgeDistance) - edgeEnd, 0) * mesh2.z;
        $F4$ distortionTexture = $TEX_P2$uv1$TEX_S2$;
        $F2$ refractUV = (uv1 + (distortionTexture.rg * 2 - 1) * mesh6.y + mesh6.zw) * mesh7.xy + screenUV;
        $F4$ refractTexture = $TEX_P3$refractUV$TEX_S3$;
        $F4$ surfaceTexture = $TEX_P4$((uv1 + mesh8.zw) * mesh9.xy)$TEX_S4$;
        $F3$ colour = mesh0.yzw + refractTexture.rgb * $F3$(mesh5.zw, mesh6.x)
            + surfaceTexture.rgb * $F3$(mesh7.zw, mesh8.x) * mesh8.y;
        if (dissolveAge > 0 && field < 0) colour = edge;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ fresnel = pow(abs(1 - max(dot(normalize(worldNormal), viewDirection), 0)), mesh5.x);
        fresnel = fresnel * (1 - mesh5.y) + mesh5.y;
        $F3$ emissive = colour + mesh4.xyz * fresnel * mesh4.w;
        $F1$ opacity = 1;
        $F1$ opacityMask = visible;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}
