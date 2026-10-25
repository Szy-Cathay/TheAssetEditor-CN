#pragma once

inline std::vector<uint8_t> RigidBasicDitherMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p + 4); };
    auto string = [&](const char* value) { auto size = std::strlen(value) + 1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(), value, value + size); };
    bytes.insert(bytes.end(), {'E', 'F', 'K', 'M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P', 'R', 'M', '_'}); auto chunkSize = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    integer(4);
    for (int index = 0; index < 4; ++index)
    {
        const auto name = "rigidTexture" + std::to_string(index);
        string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0);
    }
    integer(3);
    for (const auto name : {"mesh0", "mesh1", "meshRender"})
    {
        string(name); string(name);
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunkSize - 4); std::memcpy(bytes.data() + chunkSize, &size, 4);
    const std::string code = R"(
        $F4$ baseColour = $TEX_P0$uv1$TEX_S0$;
        $F2$ dissolveUV = (uv1 + mesh0.xy + customData1.x * mesh0.zw) * mesh1.xy;
        $F1$ dissolve = $TEX_P1$dissolveUV$TEX_S1$.r;
        // Rigid rendering rounds alpha to a byte before packing its integer thousandths.
        $F1$ alphaByte = floor(saturate(customData2.a) * 255 + 0.5);
        $F1$ alpha = floor(alphaByte * (1.0 / 255.0) * 1000) * 0.001;
        $F1$ threshold = 1 - alpha;
        // The shipped PS never uses base alpha, particle RGB, or a screen-space dither pattern.
        $F3$ emissive = saturate(baseColour.rgb);
        $F1$ opacity = 1;
        $F1$ opacityMask = dissolve >= threshold ? 1 : 0;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    bytes.insert(bytes.end(), {'G', 'E', 'N', 'E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
