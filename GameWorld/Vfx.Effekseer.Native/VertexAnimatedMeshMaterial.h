#pragma once

inline std::string VertexAnimatedParameterHeader()
{
    return R"(
cbuffer AecnVertexAnimatedParameters : register(b2) { float4 vertexAnimParams[16]; };
#define Va(i) vertexAnimParams[(i) / 4][(i) % 4]
#define Va2(i) float2(Va(i), Va((i) + 1))
#define Va3(i) float3(Va(i), Va((i) + 1), Va((i) + 2))
#define Va4(i) float4(Va(i), Va((i) + 1), Va((i) + 2), Va((i) + 3))
float AecnVertexAnimatedOffset(float3 position, float3 normal, float2 uv, float red, float time)
{
    // The shipped graph broadcasts the X oscillator and normal-X displacement to XYZ.
    float phase = (dot(position, float3(1,1,1)) * Va(56) + time * Va(48)) * Va(58);
    float oscillation = sin(phase) * Va(57) * red;
    float2 displacementUV = (uv + time * Va2(51)) * Va2(53);
    float displacement = vertexAnimDisplacementTexture.SampleLevel(vertexAnimDisplacementSampler, displacementUV, 0).b;
    displacement *= normal.x * Va(50) * (1 - uv.y) * Va(55);
    return Va(47) * oscillation + displacement;
}
)";
}

inline std::string VertexAnimatedVertexOffset()
{
    return R"(
    float vertexAnimTime = lerp(customData1.z, customData1.x, Va(45));
    worldPos += AecnVertexAnimatedOffset(worldPos, worldNormal, uv1, vcolor.r, vertexAnimTime);
)";
}

inline std::string VertexAnimatedMeshCode()
{
    return R"(
        $F1$ sourceTime = customData1.x;
        $F1$ animationTime = lerp(customData1.z, sourceTime, Va(45));
        // Native PS evaluates the offset again for its facing vector, after VS displacement.
        $F3$ facingPosition = worldPos + AecnVertexAnimatedOffset(worldPos, worldNormal, uv1, vcolor.r, animationTime);
        $F3$ viewDirection = normalize(cameraPosition.xyz - facingPosition);
        $F1$ ndv = abs(dot(viewDirection, normalize(worldNormal)));
        $F1$ facing = lerp(pow(abs(1 - ndv), Va(28)), pow(ndv, Va(28)), Va(27));
        $F2$ uvA = (uv1 + sourceTime * Va2(33)) * Va2(39);
        $F2$ uvB = (uv1 + sourceTime * Va2(35)) * Va2(41);
        $F2$ uvC = (uv1 + sourceTime * Va2(37)) * Va2(43);
        $F4$ sampleA = $TEX_P0$uvA$TEX_S0$;
        $F4$ sampleB = $TEX_P1$uvB$TEX_S1$;
        $F4$ sampleC = $TEX_P2$uvC$TEX_S2$;
        $F1$ mask = dot(sampleA, Va4(0)) * Va(30) * dot(sampleB, Va4(4)) * Va(31) * dot(sampleC, Va4(8)) * Va(32);
        $F2$ globalUV = (uv1 + sourceTime * Va2(19)) * Va2(21);
        $F4$ globalMask = $TEX_P4$globalUV$TEX_S4$;
        $F1$ vertexAlpha = lerp(1, vcolor.a, Va(49));
        $F1$ depthScale = vertexAnimCamera.w > 0.5 ? 1 : abs(dot(normalize(cameraPosition.xyz - worldPos), vertexAnimCamera.xyz));
        $F1$ vertexAnimSoftDistance = max(Va(29) * depthScale, 0.00000001);
        $F1$ soft = vertexAnimRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, vertexAnimSoftDistance) : 1;
        mask *= lerp(1, soft, Va(17)) * facing * globalMask.r * Va(46) * vertexAlpha * vertexAlpha;
        mask *= lerp(1, floor(customData2.a * 1000) * 0.001, Va(63));
        mask = max(mask, 0);
        $F2$ colourUV = (uv1 + sourceTime * Va2(12)) * Va2(15);
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (Va(14) - 1)) * Va3(23) * Va(26) * customData2.rgb;
        $F1$ opacity = saturate(mask);
        // Preserve the native ONE / INV_SRC_ALPHA contribution with the public SRC_ALPHA renderer.
        $F3$ emissive = colour * mask / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
}

inline std::vector<uint8_t> VertexAnimatedMeshMaterial()
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p + 4); };
    auto string = [&](const char* value) { auto length = std::strlen(value) + 1; integer(static_cast<int32_t>(length)); bytes.insert(bytes.end(), value, value + length); };
    bytes.insert(bytes.end(), {'E','F','K','M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P','R','M','_'}); auto chunk = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    integer(6);
    for (int index = 0; index < 6; ++index)
    {
        auto name = "meshTexture" + std::to_string(index);
        string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0);
    }
    integer(2);
    for (auto name : {"vertexAnimRender", "vertexAnimCamera"})
    {
        string(name); string(name); integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunk - 4); std::memcpy(bytes.data() + chunk, &size, 4);
    auto code = VertexAnimatedMeshCode();
    bytes.insert(bytes.end(), {'G','E','N','E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
