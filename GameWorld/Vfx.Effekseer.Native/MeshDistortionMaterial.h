#pragma once

inline std::vector<uint8_t> MeshDistortionMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer=[&](int32_t value){auto p=reinterpret_cast<const uint8_t*>(&value);bytes.insert(bytes.end(),p,p+4);};
    auto string=[&](const char* value){auto n=std::strlen(value)+1;integer(static_cast<int32_t>(n));bytes.insert(bytes.end(),value,value+n);};
    bytes.insert(bytes.end(),{'E','F','K','M'});integer(3);integer(0);integer(0);
    bytes.insert(bytes.end(),{'P','R','M','_'});auto chunk=bytes.size();integer(0);
    integer(1);integer(0);integer(0);integer(4);integer(4);integer(2);
    for(int i=0;i<2;++i)
    {
        auto name="meshDistortionTexture"+std::to_string(i);string(name.c_str());string(name.c_str());string("");
        integer(i);integer(0);integer(0);integer(1);integer(0);
    }
    integer(9);
    for(auto name:{"meshDistortion0","meshDistortion1","meshDistortion2","meshDistortionRender",
        "meshDistortionInverse0","meshDistortionInverse1","meshDistortionInverse2","meshDistortionInverse3","meshDistortionViewport"})
    {
        string(name);string(name);integer(0);integer(0);integer(4);integer(0);integer(0);integer(0);integer(0);
    }
    auto size=static_cast<int32_t>(bytes.size()-chunk-4);std::memcpy(bytes.data()+chunk,&size,4);
    std::string code=R"(
        $F1$ sourceTime = customData1.x;
        $F2$ detailUV = (uv1 + sourceTime * meshDistortion0.zw) * meshDistortion1.xy;
        $F2$ detail = $TEX_P0$detailUV$TEX_S0$.rg;
        $F2$ mainUV = (uv1 + sourceTime * $F2$(meshDistortion1.w,meshDistortion2.x)) * meshDistortion2.yz + detail;
        $F4$ mainTexture = $TEX_P1$mainUV$TEX_S1$;
        $F1$ distortionSoftDistance = meshDistortion0.y;
        $F1$ depthFade = meshDistortionRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, distortionSoftDistance) : 1;
        $F1$ facing = dot(normalize(cameraPosition.xyz-worldPos),normalize(worldNormal));
        $F1$ instanceAlpha = floor(customData2.a*1000)*0.001;
        $F1$ distortionVector = 2*(mainTexture.r-mainTexture.b)*meshDistortion1.z
            * facing * vcolor.a * instanceAlpha * depthFade * depthFade;
        // The original independent output alpha controls revealage, not the vector contribution.
        $F3$ emissive = meshDistortionRender.z > 0.5 ? $F3$(1,1,1)*(1-meshDistortion0.x) : $F3$(distortionVector,distortionVector,0);
        $F1$ opacity = 1;
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    bytes.insert(bytes.end(),{'G','E','N','E'});integer(static_cast<int32_t>(code.size()+5));string(code.c_str());
    return bytes;
}
