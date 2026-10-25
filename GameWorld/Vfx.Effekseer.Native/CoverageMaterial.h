#pragma once

// Build a minimal official EFKM v3 material, supported by the pinned 170e loader.
// Game data stays unchanged; both passes sample the original texture before splitting it.
std::vector<uint8_t> PremultipliedMaterial(bool projectedDecal = false)
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p + 4); };
    auto string = [&](const char* value) { auto size = std::strlen(value) + 1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(), value, value + size); };
    bytes.insert(bytes.end(), {'E', 'F', 'K', 'M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P', 'R', 'M', '_'}); auto chunkSize = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4); // unlit, no refraction, customData1/2.xyzw
    integer(3);
    for (int i = 0; i < 3; ++i)
    {
        auto name = i == 0 ? "source" : i == 1 ? "shadingNormal" : "gradient";
        string(name); string(name); string("");
        integer(i); integer(0); integer(0); integer(1); integer(1); // value textures, clamp
    }
    integer(projectedDecal ? 14 : 15);
    for (auto name : {"parameters", "ambientPX", "ambientNX", "ambientPY", "ambientNY", "ambientPZ", "ambientNZ", "sunDirection", "sunColour", "lighting", "scattering"})
    {
        if (projectedDecal && std::strcmp(name, "parameters") != 0) continue;
        string(name); string(name);
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    if (projectedDecal)
        for (auto name : {"decalInfo", "decalProjection", "decalDistances", "decalUV", "decalPivot",
            "decalClip0", "decalClip1", "decalClip2", "decalClip3", "decalGround"})
        {
            string(name); string(name);
            integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
        }
    string("gradientInfo"); string("gradientInfo");
    integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    string("particleInfo"); string("particleInfo");
    integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    string("normalUv"); string("normalUv");
    integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    if (!projectedDecal)
    {
        string("softDepthInfo"); string("softDepthInfo");
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunkSize - 4); std::memcpy(bytes.data() + chunkSize, &size, 4);
    std::string code = projectedDecal ? R"(
        $F4$ ambientPX = $F4$(0,0,0,0), ambientNX = ambientPX, ambientPY = ambientPX, ambientNY = ambientPX;
        $F4$ ambientPZ = ambientPX, ambientNZ = ambientPX, sunDirection = ambientPX, sunColour = ambientPX;
        $F4$ lighting = ambientPX, scattering = ambientPX;
        // Public material depth fade reconstructs the receiver's view distance from the copied scene depth.
        $F1$ receiverDistance = CalcDepthFade(screenUV, 0, 1000000) * 1000000 + decalDistances.x;
        $F2$ ndcXY = $F2$(screenUV.x * 2 - 1, 1 - screenUV.y * 2);
        $F1$ receiverZ = (-receiverDistance * decalProjection.x + decalProjection.y)
            / (-receiverDistance * decalProjection.z + decalProjection.w);
        $F4$ localH = ndcXY.x * decalClip0 + ndcXY.y * decalClip1 + receiverZ * decalClip2 + decalClip3;
        $F3$ receiver = localH.xyz / localH.w;
        $F1$ decalMask = decalInfo.x;
        if (decalInfo.y > 0.5 && (decalInfo.x < 0.5 || receiverDistance >= decalDistances.y * 0.9999))
        {
            // Only the standalone editor supplies an explicit flat receiver for otherwise empty background.
            $F4$ nearH = ndcXY.x * decalClip0 + ndcXY.y * decalClip1 + decalClip3;
            $F4$ farH = nearH + decalClip2;
            $F3$ start = nearH.xyz / nearH.w;
            $F3$ ray = farH.xyz / farH.w - start;
            $F1$ divisor = dot(decalGround.xyz, ray);
            $F1$ t = -(dot(decalGround.xyz, start) + decalGround.w) / (abs(divisor) > 0.0000001 ? divisor : 0.0000001);
            receiver = start + ray * t;
            decalMask = abs(divisor) > 0.0000001 && t >= 0 ? 1 : 0;
        }
        $F2$ localUV = $F2$(receiver.x + decalPivot.x, 1 - receiver.y - decalPivot.y);
        decalMask *= all(localUV >= 0) && all(localUV <= 1) && abs(receiver.z) <= 0.5 ? 1 : 0;
        if (decalInfo.z > 1.5) localUV = $F2$(1 - localUV.y, localUV.x);
        else if (decalInfo.z > 0.5) localUV = $F2$(localUV.y, 1 - localUV.x);
        uv1 = decalUV.xy + localUV * decalUV.zw;
    )" : "";
    code += R"(
        $F4$ sampled = lerp($TEX_P0$uv1$TEX_S0$, $TEX_P0$uv1 + customData1.xy$TEX_S0$, customData1.z);
        $F1$ alpha = sampled.a <= 0.04045 ? sampled.a / 12.92 : pow((sampled.a + 0.055) / 1.055, 2.4);
        if (gradientInfo.x > 0.5)
        {
            $F2$ colourUV = $F2$((saturate(sampled.r) * 255 + 0.5) / 256, 0.5);
            sampled.rgb = $TEX_P2$colourUV$TEX_S2$.rgb * gradientInfo.z;
            if (gradientInfo.y > 0.5)
            {
                $F2$ alphaUV = $F2$((saturate(alpha) * 255 + 0.5) / 256, 0.5);
                alpha = saturate($TEX_P2$alphaUV$TEX_S2$.a);
            }
        }
        $F1$ fade = parameters.y > 0 ? CalcDepthFade(screenUV, meshZ, parameters.y) : 1;
        $F1$ vertexAlpha = vcolor.a * particleInfo.x;
        $F1$ sourceOpacity = saturate(alpha * vertexAlpha);
        $F3$ covered = min(sampled.rgb, alpha) * saturate(vertexAlpha) / max(sourceOpacity, 0.00000001);
        $F3$ emission = max(sampled.rgb - alpha, 0);
        $F3$ coverageColour = parameters.z < 0.5 ? sampled.rgb : covered;
        if (scattering.z > 0.5 && lighting.x > 0.5 && parameters.z < 1.5 && customData1.w > 0)
        {
            $F4$ raw0 = $TEX_P1$uv1$TEX_S1$;
            $F4$ raw1 = $TEX_P1$uv1 + customData1.xy$TEX_S1$;
            $F2$ xy0 = (raw0.ag + 0.00196078431372549) * 2 - 1;
            $F2$ xy1 = (raw1.ag + 0.00196078431372549) * 2 - 1;
            $F3$ t0 = $F3$(xy0, sqrt(max(0, 1 - dot(xy0, xy0))));
            $F3$ t1 = $F3$(xy1, sqrt(max(0, 1 - dot(xy1, xy1))));
            $F3$ tangentNormal = parameters.w > 0.5 ? normalize(lerp(t0, t1, customData1.z)) : $F3$(0, 0, 1);
            // MonoGame's RH billboard right is opposite the native camera's right.
            tangentNormal.xy *= -normalUv.xy;
            $F4$ q = customData2;
            $F3$ n = normalize(tangentNormal + 2 * cross(q.xyz, cross(q.xyz, tangentNormal) + q.w * tangentNormal));
            $F3$ ambient = n.x*n.x * (n.x >= 0 ? ambientPX.rgb : ambientNX.rgb)
                + n.y*n.y * (n.y >= 0 ? ambientPY.rgb : ambientNY.rgb)
                + n.z*n.z * (n.z >= 0 ? ambientPZ.rgb : ambientNZ.rgb);
            $F1$ e = lighting.y;
            $F1$ density = lighting.z + lighting.w;
            $F1$ solar = 0;
            if (density > 0)
            {
                $F1$ transmission = exp(-density * customData1.w * scattering.y * (1 - dot(n, sunDirection.xyz)) / 2);
                $F1$ cosine = sunColour.w;
                $F1$ rayleigh = 0.05968310365946075 * (1 + cosine*cosine);
                $F1$ hg = lighting.w > 0 ? 0.07957747154594767 * (1-scattering.x)*(1-scattering.x)
                    / pow(max(1 + scattering.x*scattering.x - 2*scattering.x*cosine, 0.00000001), 1.5) : 0;
                $F1$ phase = (lighting.z * rayleigh + lighting.w * hg) / density;
                // Captured native particle shader radiance factor, before tone mapping or bloom.
                solar = 1.5230484132189304 * sunDirection.w * saturate(transmission + phase * (1-transmission));
            }
            $F3$ gain = lighting.x < 1.5 ? ambientPY.rgb * (1 - saturate(e)) + e*saturate(e)
                : ambient + sunColour.rgb * solar * (1 - saturate(e)) + e*saturate(e);
            coverageColour *= gain * scattering.w;
        }
        $F3$ emissive = (parameters.z < 1.5 ? coverageColour : emission) * vcolor.rgb * parameters.x;
        // Native default-pass emission has its own shape before particle tint, independent of covered opacity.
        $F1$ emissionShape = particleInfo.y > 0
            ? saturate(dot(emission, $F3$(0.2126,0.7152,0.0722)) * vertexAlpha) * particleInfo.y
            : saturate(vertexAlpha);
        $F1$ opacity = parameters.z < 1.5 ? sourceOpacity : emissionShape;
        if (particleInfo.w > 0.5)
        {
            // Native ps_alpha_crush trims covered and emissive visibility independently.
            opacity = saturate(opacity - particleInfo.z) / (saturate(1 - particleInfo.z) + 0.001);
            if (particleInfo.w > 1.5 && opacity < particleInfo.z) opacity = 0;
        }
        opacity *= fade;
        // Default-pass emission softens both radiance and its shape, after crushing.
        if (parameters.z > 1.5 && particleInfo.y > 0) opacity *= fade;
    )";
    if (projectedDecal) code += R"(
        if (parameters.z > 2.5)
        {
            // Shipped ps_main_deferred_decal_emissive writes radiance into an additive RGB target.
            $F1$ sourceOpacity = saturate(alpha * vcolor.a);
            $F1$ correction = parameters.z > 3.5 ? saturate(vcolor.a) : sourceOpacity;
            $F3$ diffusePart = parameters.z > 3.5 ? min(sampled.rgb, alpha) : sampled.rgb;
            $F3$ emissionPart = parameters.z > 3.5 ? saturate(sampled.rgb - alpha) : $F3$(0,0,0);
            $F1$ emissiveness = saturate(dot(emissionPart, $F3$(0.2126,0.7152,0.0722)) * vcolor.a);
            emissive = 2500 * (diffusePart * correction + emissionPart * emissiveness)
                * vcolor.rgb * parameters.x * sourceOpacity * correction;
            opacity = 1;
        }
    )";
    code += projectedDecal ? "$F1$ opacityMask = decalMask;" : "$F1$ opacityMask = 1;";
    code += "$F3$ worldPositionOffset = $F3$(0, 0, 0);";
    bytes.insert(bytes.end(), {'G', 'E', 'N', 'E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
