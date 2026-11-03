#pragma once

inline std::vector<uint8_t> RigidGlowMaterial(bool distortion)
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p+4); };
    auto string = [&](const char* value) { auto size = std::strlen(value)+1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(),value,value+size); };
    bytes.insert(bytes.end(), {'E','F','K','M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P','R','M','_'}); auto chunk = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4); integer(3);
    for (int index=0; index<3; ++index)
    {
        auto name="rigidGlowTexture"+std::to_string(index); string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0);
    }
    integer(2);
    for (auto name : {"rigidGlowParameters","rigidGlowRender"})
    {
        string(name); string(name); integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size=static_cast<int32_t>(bytes.size()-chunk-4); std::memcpy(bytes.data()+chunk,&size,4);
    auto code=distortion ? std::string(R"(
        $F1$ sourceTime = customData1.x;
        $F2$ detailUV = uv1 * 0.35 + $F2$(sin(sourceTime * 0.03), sin((sourceTime+1)*0.03));
        $F4$ detail = $TEX_P2$detailUV$TEX_S2$;
        $F2$ displacedUV = uv1 + detail.rb * 0.5;
        $F4$ noise = $TEX_P1$displacedUV$TEX_S1$;
        // Native distortion PS returns (noise.r,noise.r,0,1), without particle alpha or depth weights.
        $F3$ emissive = rigidGlowRender.z > 0.5 ? $F3$(0,0,0) : $F3$(noise.r,noise.r,0);
        $F1$ opacity = 1;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )") : std::string(R"(
        $F2$ rigidDitherUV = screenUV * rigidGlowRender.xy * 0.125;
        $F4$ dither = $TEX_P0$rigidDitherUV$TEX_S0$;
        $F1$ alpha = floor(customData2.a * 1000) * 0.001;
        $F1$ weightedAlpha = alpha * pow(abs(vcolor.a), 2.2);
        $F1$ opacity = (alpha - dither.r) * weightedAlpha;
        $F3$ emissive = opacity * weightedAlpha * rigidGlowParameters.rgb * rigidGlowParameters.a * customData2.rgb;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )");
    bytes.insert(bytes.end(), {'G','E','N','E'}); integer(static_cast<int32_t>(code.size()+5)); string(code.c_str());
    return bytes;
}
