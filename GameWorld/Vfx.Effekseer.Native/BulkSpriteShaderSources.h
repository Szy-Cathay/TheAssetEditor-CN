#pragma once
#include <string>
#include <stdexcept>

inline void BulkShaderReplace(std::string& text, const std::string& from, const std::string& to)
{
    const auto at = text.find(from);
    if (at == std::string::npos || text.find(from, at + from.size()) != std::string::npos)
        throw std::runtime_error("Pinned bulk sprite shader contract changed: " + from);
    text.replace(at, from.size(), to);
}
inline void BulkShaderFields(std::string& text, const char* name, bool flat)
{
    const auto start = text.find(std::string("struct ") + name + "\n{");
    const auto end = text.find("\n};", start);
    if (start == std::string::npos || end == std::string::npos) throw std::runtime_error("Missing bulk sprite structure");
    const std::string qualifier = flat ? "nointerpolation " : "";
    std::string fields;
    for (int i = 0; i < 4; ++i)
        fields += "\n    " + qualifier + "float4 AecnDynamic" + std::to_string(i) + " : TEXCOORD" + std::to_string(12 + i) + ";";
    fields += "\n    " + qualifier + "uint AecnBlendTag : TEXCOORD16;";
    text.insert(end, fields);
}
inline void BulkShaderGlobals(std::string& text, bool vertex)
{
    const char* names[] = {"parameters", "particleInfo", "normalUv", "softDepthInfo"};
    const int registers[] = {vertex ? 11 : 8, vertex ? 23 : 20, vertex ? 24 : 21, vertex ? 25 : 22};
    for (int i = 0; i < 4; ++i)
        BulkShaderReplace(text, std::string("float4 ") + names[i] + " : register(c" + std::to_string(registers[i]) + ");",
            std::string("float4 AecnUnused_") + names[i] + " : register(c" + std::to_string(registers[i]) + ");");
    const auto buffer = text.find("cbuffer ");
    const auto end = text.find("\n};", buffer);
    if (end == std::string::npos) throw std::runtime_error("Missing bulk sprite constant buffer");
    std::string globals;
    for (auto name : names) globals += std::string("\nstatic float4 ") + name + ";";
    text.insert(end + 3, globals);
}
inline std::string BulkShaderAssignments()
{
    const char* names[] = {"parameters", "particleInfo", "normalUv", "softDepthInfo"};
    std::string result;
    for (int i = 0; i < 4; ++i) result += std::string("\n    ") + names[i] + " = Input.AecnDynamic" + std::to_string(i) + ";";
    return result;
}
inline std::string BulkSpriteVertexSource(std::string text)
{
    BulkShaderFields(text, "VS_Input", false); BulkShaderFields(text, "VS_Output", true); BulkShaderGlobals(text, true);
    BulkShaderReplace(text, "VS_Output main( const VS_Input Input )\n{", "VS_Output main( const VS_Input Input )\n{" + BulkShaderAssignments());
    std::string assignments;
    for (int i = 0; i < 4; ++i) assignments += "\n    Output.AecnDynamic" + std::to_string(i) + " = Input.AecnDynamic" + std::to_string(i) + ";";
    assignments += "\n    Output.AecnBlendTag = Input.AecnBlendTag;";
    BulkShaderReplace(text, "VS_Output Output = (VS_Output)0;", "VS_Output Output = (VS_Output)0;" + assignments);
    return text;
}
inline std::string BulkSpritePixelSource(std::string text, bool alphaOnly)
{
    BulkShaderFields(text, "PS_Input", true); BulkShaderGlobals(text, false);
    BulkShaderReplace(text, "float4 main( const PS_Input Input ) : SV_Target\n{", "float4 AecnOriginalMain( const PS_Input Input )\n{" + BulkShaderAssignments());
    if (alphaOnly) return text + R"(
float4 main(const PS_Input Input) : SV_Target0
{
    float4 value=AecnOriginalMain(Input);
    return float4(0,0,0,value.a);
}
)";
    return text + R"(
struct AecnDualOutput { float4 Colour : SV_Target0; float4 Attenuation : SV_Target1; };
AecnDualOutput main(const PS_Input Input)
{
    float4 value=AecnOriginalMain(Input);
    AecnDualOutput result; result.Colour=value;
    result.Attenuation=float4(0,0,0,Input.AecnBlendTag == 1 ? value.a : 0);
    return result;
}
)";
}
