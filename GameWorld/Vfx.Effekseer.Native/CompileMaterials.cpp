#include <Effekseer/Material/Effekseer.CompiledMaterial.h>
#include <EffekseerMaterialCompilerDX11.h>
#include "../HLSLGenerator/ShaderGenerator.h"
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <array>
#include <cstring>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <string>
#include <vector>

namespace
{
#include "CoverageMaterial.h"
#include "DistortionMaterial.h"
#include "OneShotMeshMaterial.h"
#include "BasicMeshMaterial.h"
#include "GemstoneMeshMaterial.h"
#include "AnimatedUvSpriteMaterial.h"
#include "BasicSpriteMaterial.h"
#include "PremultiplyAnimatedMaterial.h"
#include "SolidMeshMaterial.h"
#include "PrelitSpriteMaterial.h"
#include "SoulPanningMeshMaterial.h"
#include "GrowingVinesMaterial.h"
#include "RigidDissolveMaterial.h"
#include "RigidBasicDitherMaterial.h"
#include "ScrollingMeshMaterial.h"
#include "TidecallMaterial.h"
#include "VertexAnimatedMeshMaterial.h"

bool IsVertexAnimated(const Effekseer::MaterialFile& material)
{
    return material.GetUniformCount() > 0 && std::strcmp(material.GetUniformName(0), "vertexAnimRender") == 0;
}

bool IsTidecall(const Effekseer::MaterialFile& material)
{
    return material.GetUniformCount() > 0 && std::strcmp(material.GetUniformName(0), "tideRender") == 0;
}

bool IsCoverage(const Effekseer::MaterialFile& material)
{
    return material.GetUniformCount() > 1 && std::strcmp(material.GetUniformName(0), "parameters") == 0
        && std::strcmp(material.GetUniformName(1), "ambientPX") == 0;
}

void ReplaceRequired(std::string& code, const std::string& from, const std::string& to)
{
    const auto position = code.find(from);
    if (position == std::string::npos) throw std::runtime_error("Pinned particle shader contract changed: " + from);
    code.replace(position, from.size(), to);
}

std::vector<uint8_t> CompileShader(const std::string& code, const char* profile)
{
    Microsoft::WRL::ComPtr<ID3DBlob> shader, errors;
    UINT flags = D3DCOMPILE_PACK_MATRIX_COLUMN_MAJOR;
#ifndef _DEBUG
    flags |= D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
    if (FAILED(D3DCompile(code.data(), code.size(), "VFX native depth and authored sampling", nullptr, nullptr,
        "main", profile, flags, 0, shader.GetAddressOf(), errors.GetAddressOf())))
        throw std::runtime_error(errors ? static_cast<const char*>(errors->GetBufferPointer()) : "VFX shader compilation failed");
    const auto data = static_cast<const uint8_t*>(shader->GetBufferPointer());
    return {data, data + shader->GetBufferSize()};
}

std::vector<uint8_t> CompileCoverageVertexShader(Effekseer::MaterialFile& material, Effekseer::MaterialShaderType type)
{
    using namespace Effekseer;
    auto generator = DirectX::ShaderGenerator(DirectX::ShaderGeneratorTarget::DirectX11);
    auto code = generator.GenerateShader(&material, type, UserUniformSlotMax, UserTextureSlotMax, 0, 40).CodeVS;
    ReplaceRequired(code, "float4 PosP : TEXCOORD6;", "float4 PosP : TEXCOORD6;\n float AecnSoftViewZ : TEXCOORD10;");
    if (type == MaterialShaderType::Standard)
    {
        ReplaceRequired(code, "Output.PosP = Output.Position;", R"(
        // Geometry XY/W remain unbiased; the soft depth varying retains perspective interpolation.
        Output.PosP = Output.Position;
        Output.AecnSoftViewZ = -cameraPos.z;
        if (softDepthInfo.w != 0)
        {
            float4 softCamera = cameraPos;
            softCamera.z += softDepthInfo.z;
            if (softDepthInfo.w > 0 && -softCamera.z <= softDepthInfo.w)
                softCamera.z = -(softDepthInfo.w + 0.001);
            float4 softClip = mul(mProj, softCamera);
            Output.Position.z = Output.Position.w * softClip.z / softClip.w;
            Output.AecnSoftViewZ = -softCamera.z;
        }
        )");
    }
    else ReplaceRequired(code, "Output.PosP = Output.Position;", "Output.PosP = Output.Position;\n Output.AecnSoftViewZ = 0;");
    return CompileShader(code, "vs_5_0");
}

std::vector<uint8_t> CompileTrilinearPixelShader(Effekseer::MaterialFile& material, Effekseer::MaterialShaderType type)
{
    using namespace Effekseer;
    auto generator = DirectX::ShaderGenerator(DirectX::ShaderGeneratorTarget::DirectX11);
    auto code = generator.GenerateShader(&material, type, UserUniformSlotMax, UserTextureSlotMax, 0, 40).CodePS;
    if (IsVertexAnimated(material))
    {
        code = "Texture2D vertexAnimDisplacementTexture : register(t8);\nSamplerState vertexAnimDisplacementSampler : register(s8);\n"
            + VertexAnimatedParameterHeader() + code;
        ReplaceRequired(code, "CalcDepthFade(screenUV, meshZ, vertexAnimSoftDistance)",
            "CalcDepthFade(Input.Position.xy, meshZ, vertexAnimSoftDistance)");
        ReplaceRequired(code, "float backgroundZ = efk_depth_texture.Sample(efk_depth_sampler, GetUVBack(screenUV)).x;",
            "uint vertexAnimDepthWidth, vertexAnimDepthHeight; efk_depth_texture.GetDimensions(vertexAnimDepthWidth, vertexAnimDepthHeight);\n"
            "int2 vertexAnimDepthPixel = clamp(int2(floor(screenUV)), int2(0,0), int2(vertexAnimDepthWidth,vertexAnimDepthHeight)-1);\n"
            "float backgroundZ = efk_depth_texture.Load(int3(vertexAnimDepthPixel,0)).x;");
    }
    if (IsTidecall(material)) code = TidecallParameterHeader()
        + "Texture2D tideExtraTextures[5] : register(t8);\nSamplerState tideMaskSampler : register(s8);\n"
        + "float4 AecnTideExtraSample(int slot, float2 uv) { return tideExtraTextures[slot].Sample(tideMaskSampler, uv); }\n" + code;
    if (IsTidecall(material))
    {
        ReplaceRequired(code, "CalcDepthFade(screenUV, meshZ, tideSoftDistance)",
            "CalcDepthFade(Input.Position.xy, meshZ, tideSoftDistance)");
        ReplaceRequired(code, "float backgroundZ = efk_depth_texture.Sample(efk_depth_sampler, GetUVBack(screenUV)).x;",
            "uint tideDepthWidth, tideDepthHeight; efk_depth_texture.GetDimensions(tideDepthWidth, tideDepthHeight);\n"
            "int2 tideDepthPixel = clamp(int2(floor(screenUV)), int2(0,0), int2(tideDepthWidth,tideDepthHeight)-1);\n"
            "float backgroundZ = efk_depth_texture.Load(int3(tideDepthPixel,0)).x;");
    }
    const bool distortion = material.GetUniformCount() > 0 && std::strcmp(material.GetUniformName(0), "distortionInfo") == 0;
    const bool coverage = IsCoverage(material);
    if (distortion)
    {
        const auto position = code.find("return ConvertToScreen(Output);");
        if (position == std::string::npos) throw std::runtime_error("Pinned distortion accumulation shader contract changed");
        code.replace(position, std::strlen("return ConvertToScreen(Output);"), "return Output;");
    }
    if (distortion || coverage)
    {
        const std::string distance = distortion ? "softDistance" : "parameters.y";
        const std::string depthCall = "CalcDepthFade(screenUV, meshZ, " + distance + ")";
        const auto depthCallPosition = code.find(depthCall);
        const std::string depthSample = "float backgroundZ = efk_depth_texture.Sample(efk_depth_sampler, GetUVBack(screenUV)).x;";
        const auto depthSamplePosition = code.find(depthSample);
        if (depthCallPosition == std::string::npos || depthSamplePosition == std::string::npos)
            throw std::runtime_error("Pinned particle depth shader contract changed");
        code.replace(depthCallPosition, depthCall.size(), "CalcDepthFade(Input.Position.xy, meshZ, " + distance
            + (coverage ? ", Input.AecnSoftViewZ)" : ")"));
        // Native samples a separate soft-depth image in absolute render-target coordinates.
        const std::string dimensions = distortion ? "distortionSampling.xy" : "softDepthInfo.xy";
        code.replace(depthSamplePosition, depthSample.size(),
            "uint depthWidth, depthHeight; efk_depth_texture.GetDimensions(depthWidth, depthHeight);\n"
            "int2 depthPixel = clamp(int2(floor(screenUV * " + dimensions + " * float2(depthWidth, depthHeight))), int2(0, 0), int2(depthWidth, depthHeight) - 1);\n"
            "float backgroundZ = efk_depth_texture.Load(int3(depthPixel, 0)).x;");
        if (coverage)
        {
            ReplaceRequired(code, "float4 PosP : TEXCOORD6;", "float4 PosP : TEXCOORD6;\n float AecnSoftViewZ : TEXCOORD10;");
            ReplaceRequired(code, "float CalcDepthFade(float2 screenUV, float meshZ, float softParticleParam)",
                "float CalcDepthFade(float2 screenUV, float meshZ, float softParticleParam, float aecnSoftViewZ)");
            ReplaceRequired(code, "depth *= dir;", "depth *= dir;\n if (softDepthInfo.w != 0) depth.y = aecnSoftViewZ;");
        }
    }
    // The pinned renderer's linear sampler selects the nearest mip. Preserve its address mode,
    // while blending the two authored levels explicitly for game material textures.
    for (int i = 0; i < material.GetTextureCount() && i < UserTextureSlotMax; ++i)
    {
        const auto name = std::string(material.GetTextureName(i)) + "_texture";
        const auto ditherSample = name + ".Sample(" + material.GetTextureName(i) + "_sampler,GetUV(rigidDitherUV))";
        const auto ditherLevel = name + ".SampleLevel(" + material.GetTextureName(i) + "_sampler,GetUV(rigidDitherUV),0)";
        size_t ditherPosition = 0;
        while ((ditherPosition = code.find(ditherSample, ditherPosition)) != std::string::npos)
        {
            code.replace(ditherPosition, ditherSample.size(), ditherLevel);
            ditherPosition += ditherLevel.size();
        }
        const auto from = name + ".Sample(";
        const auto to = "AecnTrilinear(" + name + ", ";
        size_t position = 0;
        while ((position = code.find(from, position)) != std::string::npos)
        {
            code.replace(position, from.size(), to);
            position += to.size();
        }
    }
    code = R"(
float4 AecnTrilinear(Texture2D source, SamplerState sourceSampler, float2 uv)
{
    uint width, height, levels;
    source.GetDimensions(0, width, height, levels);
    if (levels == 1) return source.SampleLevel(sourceSampler, uv, 0);
    float lod = clamp(source.CalculateLevelOfDetailUnclamped(sourceSampler, uv), 0, levels - 1);
    float lower = floor(lod);
    return lerp(source.SampleLevel(sourceSampler, uv, lower),
        source.SampleLevel(sourceSampler, uv, min(lower + 1, levels - 1)), lod - lower);
}
)" + code;
    return CompileShader(code, "ps_5_0");
}

std::vector<uint8_t> Compile(std::vector<uint8_t> source)
{
    using namespace Effekseer;
    MaterialFile material;
    if (!material.Load(source.data(), static_cast<int32_t>(source.size())))
        throw std::runtime_error("Invalid VFX material source");
    if (IsTidecall(material) || IsVertexAnimated(material))
    {
        auto vertex = [&](MaterialShaderType type) {
            auto generator = DirectX::ShaderGenerator(DirectX::ShaderGeneratorTarget::DirectX11);
            auto code = generator.GenerateShader(&material, type, UserUniformSlotMax, UserTextureSlotMax, 0, 40).CodeVS;
            if (type == MaterialShaderType::Model)
                ReplaceRequired(code, "worldPos = worldPos + worldPositionOffset;",
                    "worldPos = worldPos + worldPositionOffset;\n" + (IsVertexAnimated(material) ? VertexAnimatedVertexOffset() : TidecallVertexDisplacement()));
            if (IsVertexAnimated(material))
                return CompileShader("Texture2D vertexAnimDisplacementTexture : register(t0);\nSamplerState vertexAnimDisplacementSampler : register(s0);\n"
                    + VertexAnimatedParameterHeader() + code, "vs_5_0");
            return CompileShader(TidecallParameterHeader()
                + "Texture2D tideDisplacementTexture : register(t0);\nSamplerState tideDisplacementSampler : register(s0);\n"
                + "float4 AecnTideExtraSample(int slot, float2 uv) { return float4(0,0,0,0); }\n"
                + code, "vs_5_0");
        };
        CompiledMaterial compiled;
        compiled.UpdateData(vertex(MaterialShaderType::Standard), CompileTrilinearPixelShader(material, MaterialShaderType::Standard),
            vertex(MaterialShaderType::Model), CompileTrilinearPixelShader(material, MaterialShaderType::Model),
            {}, {}, {}, {}, CompiledMaterialPlatformType::DirectX11);
        std::vector<uint8_t> result;
        compiled.Save(result, material.GetGUID(), source);
        return result;
    }
    auto compiler = CreateUniqueReference(new MaterialCompilerDX11());
    auto binary = CreateUniqueReference(compiler->Compile(&material));
    if (!binary) throw std::runtime_error("VFX material compilation failed");
    auto shader = [&](MaterialShaderType type, bool vertex)
    {
        auto size = vertex ? binary->GetVertexShaderSize(type) : binary->GetPixelShaderSize(type);
        auto data = vertex ? binary->GetVertexShaderData(type) : binary->GetPixelShaderData(type);
        if (size == 0)
        {
            if (type == MaterialShaderType::Standard || type == MaterialShaderType::Model)
                throw std::runtime_error("VFX material has an empty shader");
            return std::vector<uint8_t>{};
        }
        if (size < 0 || data == nullptr) throw std::runtime_error("Invalid compiled VFX shader");
        if (!vertex) return CompileTrilinearPixelShader(material, type);
        if (IsCoverage(material)) return CompileCoverageVertexShader(material, type);
        return std::vector<uint8_t>(data, data + size);
    };
    CompiledMaterial compiled;
    compiled.UpdateData(shader(MaterialShaderType::Standard, true), shader(MaterialShaderType::Standard, false),
        shader(MaterialShaderType::Model, true), shader(MaterialShaderType::Model, false),
        shader(MaterialShaderType::Refraction, true), shader(MaterialShaderType::Refraction, false),
        shader(MaterialShaderType::RefractionModel, true), shader(MaterialShaderType::RefractionModel, false),
        CompiledMaterialPlatformType::DirectX11);
    std::vector<uint8_t> result;
    compiled.Save(result, material.GetGUID(), source);
    return result;
}
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("Expected generated header path");
        std::array<std::vector<uint8_t>, 18> sources = {PremultipliedMaterial(), PremultipliedMaterial(true),
            ScrollingMeshMaterial(), ScrollingMeshMaterial(true), ScrollingMeshMaterial(false, true),
            ScrollingMeshMaterial(false, false, true), ScrollingMeshMaterial(false, false, false, true),
            ScrollingMeshMaterial(false, false, false, false, true), ScrollingMeshMaterial(false, false, false, false, false, true), ScrollingMeshMaterial(false, false, false, false, false, false, true), ScrollingMeshMaterial(false, false, false, false, false, false, false, true),
            ScrollingMeshMaterial(false, false, false, false, false, false, false, false, true),
            ScrollingMeshMaterial(false, false, false, false, false, false, false, false, false, true),
            ScrollingMeshMaterial(false, false, false, false, false, false, false, false, false, false, true), DistortionMaterial(), TidecallMaterial(), VertexAnimatedMeshMaterial(), RigidBasicDitherMaterial()};
        std::array<std::vector<uint8_t>, 18> materials;
        for (size_t i = 0; i < sources.size(); ++i)
        {
            std::cout << "Compile VFX material " << i + 1 << " / " << sources.size() << std::endl;
            materials[i] = Compile(std::move(sources[i]));
        }
        std::ofstream output(argv[1], std::ios::binary | std::ios::trunc);
        output.exceptions(std::ios::badbit | std::ios::failbit);
        output << "#pragma once\n// Generated from the pinned compiler and current material sources.\n";
        for (size_t i = 0; i < materials.size(); ++i)
        {
            output << "static const uint8_t VfxMaterial" << i << "[] = {\n";
            for (size_t j = 0; j < materials[i].size(); ++j)
                output << static_cast<unsigned>(materials[i][j]) << ',' << (j % 32 == 31 ? '\n' : ' ');
            output << "\n};\n";
        }
        output << "struct CompiledVfxMaterial { const uint8_t* Data; int32_t Size; };\n"
            << "static const CompiledVfxMaterial CompiledVfxMaterials[] = {\n";
        for (size_t i = 0; i < materials.size(); ++i)
            output << "{VfxMaterial" << i << ", sizeof(VfxMaterial" << i << ")},\n";
        output << "};\n";
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << std::endl;
        return 1;
    }
    return 0;
}
