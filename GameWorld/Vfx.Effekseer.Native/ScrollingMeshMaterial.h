#pragma once

// wh_vfx_mesh's shipped PS: three panning channel masks, vertex alpha, overlay and premultiplied output.
std::vector<uint8_t> ScrollingMeshMaterial(bool oneShot = false, bool basicMesh = false, bool gemstone = false, bool animatedUV = false, bool basicSprite = false, bool premultiplyAnimated = false, bool solid = false, bool prelitSprite = false, bool soulPanning = false, bool growingVines = false, bool rigidDissolve = false)
{
    std::vector<uint8_t> bytes;
    auto integer = [&](int32_t value) { auto p = reinterpret_cast<const uint8_t*>(&value); bytes.insert(bytes.end(), p, p + 4); };
    auto string = [&](const char* value) { auto size = std::strlen(value) + 1; integer(static_cast<int32_t>(size)); bytes.insert(bytes.end(), value, value + size); };
    bytes.insert(bytes.end(), {'E', 'F', 'K', 'M'}); integer(3); integer(0); integer(0);
    bytes.insert(bytes.end(), {'P', 'R', 'M', '_'}); auto chunkSize = bytes.size(); integer(0);
    integer(1); integer(0); integer(0); integer(4); integer(4);
    const auto textureCount = growingVines ? 5 : soulPanning ? 4 : prelitSprite ? 5 : basicSprite ? 4 : 6;
    integer(textureCount);
    for (int index = 0; index < textureCount; ++index)
    {
        const auto name = "meshTexture" + std::to_string(index);
        string(name.c_str()); string(name.c_str()); string("");
        integer(index); integer(0); integer(0); integer(1); integer(0); // value texture, repeat
    }
    const auto uniformCount = rigidDissolve ? 16 : growingVines ? 4 : soulPanning ? 8 : prelitSprite ? 7 : solid ? 15 : premultiplyAnimated ? 9 : basicSprite ? 6 : animatedUV ? 10 : gemstone ? 11 : oneShot || basicMesh ? 15 : 14;
    integer(uniformCount);
    for (int index = 0; index < uniformCount; ++index)
    {
        const auto name = index < uniformCount - 1 ? "mesh" + std::to_string(index) : std::string("meshRender");
        string(name.c_str()); string(name.c_str());
        integer(0); integer(0); integer(4); integer(0); integer(0); integer(0); integer(0);
    }
    auto size = static_cast<int32_t>(bytes.size() - chunkSize - 4); std::memcpy(bytes.data() + chunkSize, &size, 4);
    const std::string code = rigidDissolve ? RigidDissolveCode() : growingVines ? GrowingVinesCode() : soulPanning ? SoulPanningMeshCode() : prelitSprite ? PrelitSpriteCode() : solid ? SolidMeshCode() : premultiplyAnimated ? PremultiplyAnimatedCode() : basicSprite ? BasicSpriteCode() : animatedUV ? AnimatedUvSpriteCode() : gemstone ? GemstoneMeshCode() : basicMesh ? BasicMeshCode() : oneShot ? OneShotMeshCode() : R"(
        $F1$ sourceTime = customData1.x;
        $F2$ uvA = (uv1 + sourceTime * mesh8.yz) * $F2$(mesh9.w, mesh10.x);
        $F2$ uvB = (uv1 + sourceTime * $F2$(mesh8.w, mesh9.x)) * mesh10.yz;
        $F2$ uvC = (uv1 + sourceTime * mesh9.yz) * $F2$(mesh10.w, mesh11.x);
        $F4$ maskA = $TEX_P0$uvA$TEX_S0$;
        $F4$ maskB = $TEX_P1$uvB$TEX_S1$;
        $F4$ maskC = $TEX_P2$uvC$TEX_S2$;
        $F1$ mask = dot(maskA, mesh0) * mesh7.z * dot(maskB, mesh1) * mesh7.w * dot(maskC, mesh2) * mesh8.x;
        $F3$ viewDirection = normalize(cameraPosition.xyz - worldPos);
        $F1$ depthScale = mesh12.y > 0.5 ? 1 : abs(dot(viewDirection, meshRender.yzw));
        $F1$ depthFade = meshRender.x > 0.5 ? CalcDepthFade(screenUV, meshZ, max(mesh7.y * depthScale, 0.00000001)) : 1;
        mask *= lerp(1, depthFade, mesh4.z);
        $F1$ ndv = saturate(abs(dot(viewDirection, normalize(worldNormal))));
        mask *= lerp(pow(1 - ndv, mesh7.x), pow(ndv, mesh7.x), mesh6.w);
        $F2$ globalUV = (uv1 + sourceTime * $F2$(mesh4.w, mesh5.x)) * mesh5.yz;
        $F4$ globalMask = $TEX_P5$globalUV$TEX_S5$;
        mask *= globalMask.r;
        $F4$ vertexColour = lerp($F4$(1,1,1,1), vcolor, mesh11.w);
        mask *= mesh11.y * pow(abs(vertexColour.a), mesh11.z);
        mask *= lerp(1, floor(customData2.a * 1000) * 0.001, mesh12.x);
        $F2$ distortionUV = uv1 * -0.35 + sourceTime * $F2$(0, 0.1);
        $F4$ distortionTexture = $TEX_P4$distortionUV$TEX_S4$;
        $F2$ colourUV = (uv1 + mesh3.x * distortionTexture.rb * 0.1 + sourceTime * mesh3.yz) * mesh4.xy;
        $F4$ overlay = $TEX_P3$colourUV$TEX_S3$;
        $F3$ colour = overlay.rgb * (1 + overlay.a * (mesh3.w - 1));
        colour *= vertexColour.rgb * $F3$(mesh5.w, mesh6.x, mesh6.y) * mesh6.z * customData2.rgb;
        $F1$ opacity = saturate(mask);
        // Effekseer's alpha blend uses SRC_ALPHA; restore the source's ONE / INV_SRC_ALPHA result.
        $F3$ emissive = colour * max(mask, 0) / max(opacity, 0.00000001);
        $F1$ opacityMask = 1;
        $F3$ worldPositionOffset = $F3$(0,0,0);
    )";
    bytes.insert(bytes.end(), {'G', 'E', 'N', 'E'}); integer(static_cast<int32_t>(code.size() + 5)); string(code.c_str());
    return bytes;
}
