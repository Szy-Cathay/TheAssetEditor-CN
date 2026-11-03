#pragma once

inline std::vector<uint8_t> NativeSimpleMeshMaterial(bool additive)
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p=reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(),p,p+4); };
    auto string = [&](const char* value) { auto size=std::strlen(value)+1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(),value,value+size); };
    bytes.insert(bytes.end(), {'E','F','K','M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P','R','M','_'}); auto chunk=bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    integer(additive ? 2 : 0);
    for (int index=0; index<(additive ? 2 : 0); ++index)
    {
        auto name="simpleMeshTexture"+std::to_string(index); string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0);
    }
    integer(2);
    for (auto name : {"simpleMesh0","simpleMeshRender"})
    {
        string(name); string(name); integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size=static_cast<int32_t>(bytes.size()-chunk-4); std::memcpy(bytes.data()+chunk,&size,4);
    const auto code= additive ? std::string(R"(
        $F2$ detailUV = uv1 + customData1.x * $F2$(0.5,0);
        $F4$ base = $TEX_P0$uv1$TEX_S0$;
        $F4$ detail = $TEX_P1$detailUV$TEX_S1$;
        $F1$ alpha = floor(customData2.a * 1000) * 0.001;
        $F3$ emissive = base.rgb * detail.rgb * 5 * simpleMesh0.rgb * simpleMesh0.a * alpha * abs(vcolor.a) * customData2.rgb;
        $F1$ opacity = 0;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )") : std::string(R"(
        $F3$ emissive = customData2.rgb;
        $F1$ opacity = 1;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )");
    bytes.insert(bytes.end(), {'G','E','N','E'}); integer(static_cast<int32_t>(code.size()+5)); string(code.c_str());
    return bytes;
}
