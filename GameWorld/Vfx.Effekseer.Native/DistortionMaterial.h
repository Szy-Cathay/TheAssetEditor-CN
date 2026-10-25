#pragma once

// Preserve native opacity inputs and accumulate weighted vectors separately from transmittance.
std::vector<uint8_t> DistortionMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p + 4); };
    auto string = [&](const char* value) { auto size = std::strlen(value) + 1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(), value, value + size); };
    bytes.insert(bytes.end(), {'E', 'F', 'K', 'M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P', 'R', 'M', '_'}); auto chunkSize = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    integer(3);
    for (auto name : {"distortionNormal", "distortionMask", "distortionGradient"})
    {
        string(name); string(name); string("");
        integer(std::strcmp(name, "distortionNormal") == 0 ? 0 : std::strcmp(name, "distortionMask") == 0 ? 1 : 2);
        integer(0); integer(0); integer(1); integer(1);
    }
    integer(6);
    for (auto name : {"distortionInfo", "distortionParticle", "distortionFlags", "distortionProjection", "distortionDistances", "distortionSampling"})
    {
        string(name); string(name);
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunkSize - 4); std::memcpy(bytes.data() + chunkSize, &size, 4);
    std::string code = R"(
        $F1$ vertexAlpha = vcolor.a * distortionParticle.x;
        $F1$ alpha = vertexAlpha;
        if (distortionInfo.y > 0.5)
        {
            $F1$ raw = lerp($TEX_P1$uv1$TEX_S1$.a,
                $TEX_P1$uv1 + customData1.xy$TEX_S1$.a, customData1.z);
            alpha = raw <= 0.04045 ? raw / 12.92 : pow((raw + 0.055) / 1.055, 2.4);
        }
        if (distortionInfo.z > 0.5 && distortionInfo.w > 0.5)
        {
            $F2$ alphaUV = $F2$((saturate(alpha) * 255 + 0.5) / 256, 0.5);
            alpha = saturate($TEX_P2$alphaUV$TEX_S2$.a);
        }
        if (distortionInfo.y > 0.5) alpha = saturate(alpha * vertexAlpha);
        if (distortionParticle.w > 0.5)
        {
            alpha = saturate(alpha - distortionParticle.z) / (saturate(1 - distortionParticle.z) + 0.001);
            if (distortionParticle.w > 1.5 && alpha < distortionParticle.z) alpha = 0;
        }
        if (distortionDistances.w > 0.5)
        {
            $F1$ softDistance = distortionInfo.x
                * (1 + distortionDistances.z * (saturate(customData1.w) - 1)) + 0.001;
            alpha *= CalcDepthFade(screenUV, meshZ, softDistance);
        }
        // The game uses the third geometry basis directly when ps_texture has no normal.
        $F2$ aecnDistortion = customData2.w > 0.5 ? customData2.xy : worldNormal.xy;
        if (distortionFlags.x > 0.5)
        {
            $F2$ xy0 = ($TEX_P0$uv1$TEX_S0$.rg + 0.00196078431372549) * 2 - 1;
            $F2$ xy1 = ($TEX_P0$uv1 + customData1.xy$TEX_S0$.rg + 0.00196078431372549) * 2 - 1;
            $F3$ n0 = $F3$(xy0, sqrt(max(0, 1 - dot(xy0, xy0))));
            $F3$ n1 = $F3$(xy1, sqrt(max(0, 1 - dot(xy1, xy1))));
            $F3$ n = normalize(lerp(n0, n1, customData1.z));
            n.xy *= distortionFlags.yz;
            // Normal source: 1 keeps the mesh basis; 2 carries exact sprite T.xy and B.xy.
            $F2$ tangent = distortionFlags.x > 1.5 ? customData2.xy : worldTangent.xy;
            $F2$ binormal = distortionFlags.x > 1.5 ? customData2.zw : worldBinormal.xy;
            aecnDistortion = n.x * tangent + n.y * binormal;
        }
        $F1$ visibility = alpha * length(aecnDistortion);
        $F1$ viewDepth = abs((meshZ * distortionProjection.w - distortionProjection.y)
            / (distortionProjection.x - meshZ * distortionProjection.z));
        $F1$ normalizedDepth = saturate((viewDepth - distortionDistances.x)
            / max(distortionDistances.y - distortionDistances.x, 0.000001));
        $F1$ depthWeight = min(10000 * pow(1 - 0.99 * normalizedDepth, 3), 1);
        $F1$ weight = clamp(depthWeight * visibility, 0.001, 150);
        // Native floating transmittance preserves alpha above one, including negative factors.
        $F3$ emissive = distortionFlags.w > 0.5 ? $F3$(1,1,1) * (1 - visibility)
            : weight * visibility * $F3$(aecnDistortion * distortionParticle.y, 1);
        // Both existing Add and Mul blend states use the already weighted RGB without an extra alpha factor.
        $F1$ opacity = 1;
        $F1$ opacityMask = visibility > 0 ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0, 0, 0);
    )";
    bytes.insert(bytes.end(), {'G', 'E', 'N', 'E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
