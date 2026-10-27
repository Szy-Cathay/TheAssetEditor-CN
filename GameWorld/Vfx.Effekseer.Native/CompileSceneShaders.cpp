#include <windows.h>
#include <bcrypt.h>
#include <d3dcompiler.h>
#include <wrl/client.h>
#include <fstream>
#include <iostream>
#include <stdexcept>
#include <vector>
#include "FloatSceneShaderSources.h"
#include "SceneUtilityShaderSources.h"

namespace
{
std::string SourceHash(const std::string& source)
{
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    if (BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_SHA256_ALGORITHM, nullptr, 0) < 0)
        throw std::runtime_error("Cannot open VFX shader provenance hash");
    unsigned char hash[32]{};
    const auto result = BCryptHash(algorithm, nullptr, 0,
        reinterpret_cast<PUCHAR>(const_cast<char*>(source.data())), static_cast<ULONG>(source.size()), hash, sizeof(hash));
    BCryptCloseAlgorithmProvider(algorithm, 0);
    if (result < 0) throw std::runtime_error("Cannot hash VFX shader source");
    const char* digits = "0123456789abcdef";
    std::string text;
    for (auto value : hash) { text += digits[value >> 4]; text += digits[value & 15]; }
    return text;
}
}

int main(int argc, char** argv)
{
    try
    {
        if (argc != 2) throw std::runtime_error("Expected generated scene shader header path");
        const auto sources = FloatSceneShaderSources();
        std::vector<Microsoft::WRL::ComPtr<ID3DBlob>> shaders;
        for (const auto& source : sources)
        {
            Microsoft::WRL::ComPtr<ID3DBlob> code, errors;
            // Preserve the original flags even when the generator itself is built in Release.
            const auto result = D3DCompile(source.Source.data(), source.Source.size(), FloatSceneShaderSourceName,
                nullptr, nullptr, "main", source.Profile, FloatSceneShaderCompileFlags, 0, code.GetAddressOf(), errors.GetAddressOf());
            if (FAILED(result))
            {
                if (errors) std::cerr.write(static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize());
                throw std::runtime_error("VFX scene shader compilation failed");
            }
            if (!code || code->GetBufferSize() == 0) throw std::runtime_error("Empty VFX scene shader bytecode");
            shaders.push_back(std::move(code));
        }
        const auto utilitySources = SceneUtilityShaderSources();
        std::vector<Microsoft::WRL::ComPtr<ID3DBlob>> utilityShaders;
        for (const auto& source : utilitySources)
        {
            Microsoft::WRL::ComPtr<ID3DBlob> code, errors;
            const auto result = D3DCompile(source.Source.data(), source.Source.size(), source.SourceName,
                nullptr, nullptr, "main", source.Profile, SceneUtilityShaderCompileFlags, 0, code.GetAddressOf(), errors.GetAddressOf());
            if (FAILED(result))
            {
                if (errors) std::cerr.write(static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize());
                throw std::runtime_error("VFX utility shader compilation failed");
            }
            if (!code || code->GetBufferSize() == 0) throw std::runtime_error("Empty VFX utility shader bytecode");
            utilityShaders.push_back(std::move(code));
        }
        // Do not publish a partial header if any shader fails.
        std::ofstream output(argv[1], std::ios::binary | std::ios::trunc);
        output.exceptions(std::ios::badbit | std::ios::failbit);
        output << "#pragma once\n// Generated from FloatSceneShaderSources.h and SceneUtilityShaderSources.h; do not edit or compile at runtime.\n";
        for (size_t index = 0; index < shaders.size(); ++index)
        {
            output << "static const unsigned char FloatScene" << sources[index].Name << "Bytes[] = {\n";
            const auto bytes = static_cast<const unsigned char*>(shaders[index]->GetBufferPointer());
            for (size_t offset = 0; offset < shaders[index]->GetBufferSize(); ++offset)
                output << static_cast<unsigned>(bytes[offset]) << ',' << (offset % 32 == 31 ? '\n' : ' ');
            output << "\n};\n";
        }
        output << "struct CompiledFloatSceneShader { const unsigned char* Data; size_t Size; const char* SourceSha256; const char* Profile; unsigned Flags; };\n"
            << "static const CompiledFloatSceneShader CompiledFloatSceneShaders[] = {\n";
        for (const auto& source : sources)
            output << "{FloatScene" << source.Name << "Bytes, sizeof(FloatScene" << source.Name << "Bytes), \""
                << SourceHash(source.Source) << "\", \"" << source.Profile << "\", " << FloatSceneShaderCompileFlags << "},\n";
        output << "};\n";
        for (size_t index = 0; index < utilityShaders.size(); ++index)
        {
            output << "static const unsigned char SceneUtility" << utilitySources[index].Name << "Bytes[] = {\n";
            const auto bytes = static_cast<const unsigned char*>(utilityShaders[index]->GetBufferPointer());
            for (size_t offset = 0; offset < utilityShaders[index]->GetBufferSize(); ++offset)
                output << static_cast<unsigned>(bytes[offset]) << ',' << (offset % 32 == 31 ? '\n' : ' ');
            output << "\n};\n";
        }
        output << "struct CompiledSceneUtilityShader { const unsigned char* Data; size_t Size; const char* SourceSha256; const char* Profile; unsigned Flags; };\n"
            << "static const CompiledSceneUtilityShader CompiledSceneUtilityShaders[] = {\n";
        for (const auto& source : utilitySources)
            output << "{SceneUtility" << source.Name << "Bytes, sizeof(SceneUtility" << source.Name << "Bytes), \""
                << SourceHash(source.Source) << "\", \"" << source.Profile << "\", " << SceneUtilityShaderCompileFlags << "},\n";
        output << "};\n";
    }
    catch (const std::exception& error)
    {
        std::cerr << error.what() << std::endl;
        return 1;
    }
    return 0;
}
