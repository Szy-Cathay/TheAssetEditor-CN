#pragma once
#include <d3dcompiler.h>
#include <d3d11shader.h>
#include <wrl/client.h>
#include <algorithm>
#include <cstring>
#include <memory>
#include <vector>
#include <map>
#include <EffekseerRendererDX11.h>
#include <EffekseerRendererDX11.RendererImplemented.h>
#include <EffekseerRendererDX11.Shader.h>
#include <EffekseerRendererDX11.VertexBuffer.h>
#include <EffekseerRendererDX11.IndexBuffer.h>
#include <EffekseerRenderer.Renderer_Impl.h>
#include <EffekseerRenderer.StandardRenderer.h>
#include <EffekseerRenderer.SpriteRendererBase.h>
#include "BulkSpriteQueue.h"

namespace AecnSpriteUpload
{
struct ByteRange { size_t Offset, Size; };

class VertexBinding
{
    const void* bound_ = nullptr;
public:
    bool NeedsBind(const void* buffer) const { return !bound_ || bound_ != buffer; }
    void Bound(const void* buffer) { bound_ = buffer; }
    void Invalidate() { bound_ = nullptr; }
};

inline std::vector<ByteRange> ReflectVertexRanges(const void* code, size_t size, size_t expectedSize)
{
    Microsoft::WRL::ComPtr<ID3D11ShaderReflection> reflection;
    if (!code || !size || FAILED(D3DReflect(code, size, __uuidof(ID3D11ShaderReflection),
        reinterpret_cast<void**>(reflection.GetAddressOf())))) return {};
    D3D11_SHADER_DESC shader{};
    if (FAILED(reflection->GetDesc(&shader)) || D3D11_SHVER_GET_TYPE(shader.Version) != D3D11_SHVER_VERTEX_SHADER
        || shader.ConstantBuffers != 1) return {};
    auto buffer = reflection->GetConstantBufferByIndex(0);
    D3D11_SHADER_BUFFER_DESC description{};
    D3D11_SHADER_INPUT_BIND_DESC binding{};
    if (FAILED(buffer->GetDesc(&description)) || description.Size != expectedSize
        || FAILED(reflection->GetResourceBindingDescByName(description.Name, &binding))
        || binding.Type != D3D_SIT_CBUFFER || binding.BindPoint != 0 || binding.BindCount != 1) return {};
    std::vector<ByteRange> ranges;
    for (UINT i = 0; i < description.Variables; ++i)
    {
        D3D11_SHADER_VARIABLE_DESC variable{};
        if (FAILED(buffer->GetVariableByIndex(i)->GetDesc(&variable))) return {};
        if (!(variable.uFlags & D3D_SVF_USED)) continue;
        if (variable.Size == 0 || variable.StartOffset > expectedSize
            || variable.Size > expectedSize - variable.StartOffset) return {};
        ranges.push_back({variable.StartOffset, variable.Size});
    }
    std::sort(ranges.begin(), ranges.end(), [](const ByteRange& a, const ByteRange& b) { return a.Offset < b.Offset; });
    std::vector<ByteRange> merged;
    for (const auto& range : ranges)
    {
        if (!merged.empty() && range.Offset <= merged.back().Offset + merged.back().Size)
            merged.back().Size = std::max(merged.back().Offset + merged.back().Size, range.Offset + range.Size) - merged.back().Offset;
        else merged.push_back(range);
    }
    return merged;
}

class UsedBytes
{
    std::vector<ByteRange> ranges_;
    std::vector<uint8_t> previous_;
    bool valid_ = false;
public:
    UsedBytes(size_t size, std::vector<ByteRange> ranges) : ranges_(std::move(ranges)), previous_(size) {}
    bool Changed(const void* data) const
    {
        if (!valid_ || ranges_.empty()) return true;
        const auto bytes = static_cast<const uint8_t*>(data);
        for (const auto& range : ranges_)
            if (std::memcmp(bytes + range.Offset, previous_.data() + range.Offset, range.Size) != 0) return true;
        return false;
    }
    void Uploaded(const void* data) { std::memcpy(previous_.data(), data, previous_.size()); valid_ = true; }
    void Invalidate() { valid_ = false; }
};

class Shader
{
    EffekseerRendererDX11::Shader* source_;
    ID3D11DeviceContext* context_ = nullptr;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> expectedVertex_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> expectedPixel_;
    Microsoft::WRL::ComPtr<ID3D11InputLayout> expectedLayout_;
    Microsoft::WRL::ComPtr<ID3D11Buffer> vertex_, pixel_;
    int vertexSize_ = 0, pixelSize_ = 0;
    std::unique_ptr<UsedBytes> used_;
    VertexBinding* binding_ = nullptr;
    bool fallback_ = false;
public:
    explicit Shader(EffekseerRendererDX11::Shader* source, VertexBinding* binding = nullptr) : source_(source), binding_(binding) {}
    EffekseerRendererDX11::Shader* Source() const { return source_; }
    bool Initialize(ID3D11Device* device, ID3D11DeviceContext* context, const void* code, size_t size)
    {
        vertexSize_ = source_->GetVertexConstantBufferSize(); pixelSize_ = source_->GetPixelConstantBufferSize();
        if (vertexSize_ <= 0 || pixelSize_ <= 0 || vertexSize_ % 16 || pixelSize_ % 16) return false;
        auto ranges = ReflectVertexRanges(code, size, vertexSize_);
        if (ranges.empty()) return false;
        D3D11_BUFFER_DESC description{}; description.Usage = D3D11_USAGE_DEFAULT;
        description.BindFlags = D3D11_BIND_CONSTANT_BUFFER; description.StructureByteStride = sizeof(float);
        description.ByteWidth = vertexSize_;
        if (FAILED(device->CreateBuffer(&description, nullptr, vertex_.GetAddressOf()))) return false;
        description.ByteWidth = pixelSize_;
        if (FAILED(device->CreateBuffer(&description, nullptr, pixel_.GetAddressOf()))) return false;
        used_ = std::make_unique<UsedBytes>(vertexSize_, std::move(ranges)); context_ = context;
        expectedVertex_ = source_->GetVertexShader(); expectedPixel_ = source_->GetPixelShader();
        expectedLayout_ = source_->GetLayoutInterface();
        return expectedVertex_ && expectedPixel_ && expectedLayout_;
    }
    bool UsedFallback() const { return fallback_; }
    void SetConstantBuffer()
    {
        fallback_ = false;
        if (!used_ || !vertex_ || !pixel_ || !context_
            || source_->GetVertexConstantBufferSize() != vertexSize_ || source_->GetPixelConstantBufferSize() != pixelSize_
            || source_->GetVertexShader() != expectedVertex_.Get() || source_->GetPixelShader() != expectedPixel_.Get()
            || source_->GetLayoutInterface() != expectedLayout_.Get()
            || !source_->GetVertexConstantBuffer() || !source_->GetPixelConstantBuffer())
        {
            fallback_ = true;
            if (used_) used_->Invalidate();
            if (binding_) binding_->Invalidate();
            source_->SetConstantBuffer(); return;
        }
        const auto vertexData = source_->GetVertexConstantBuffer();
        if (used_->Changed(vertexData))
        {
            context_->UpdateSubresource(vertex_.Get(), 0, nullptr, vertexData, 0, 0);
            used_->Uploaded(vertexData);
        }
        context_->UpdateSubresource(pixel_.Get(), 0, nullptr, source_->GetPixelConstantBuffer(), 0, 0);
        auto vertex = vertex_.Get(), pixel = pixel_.Get();
        if (!binding_ || binding_->NeedsBind(vertex))
        {
            context_->VSSetConstantBuffers(0, 1, &vertex);
            if (binding_) binding_->Bound(vertex);
        }
        context_->PSSetConstantBuffers(0, 1, &pixel);
    }
};

template<class RENDERER>
class ScopedSprites : public EffekseerRenderer::SpriteRendererBase<RENDERER, false>
{
    using Base = EffekseerRenderer::SpriteRendererBase<RENDERER, false>;
    using State = EffekseerRenderer::StandardRendererState;
    using Node = Effekseer::SpriteRenderer::NodeParameter;
    struct Entry
    {
        State Value;
        Effekseer::Effect* Effect = nullptr;
        Effekseer::MaterialRenderData* RenderData = nullptr;
        std::array<Effekseer::MaterialTextureParameter, 3> Textures;
        std::array<Effekseer::TextureWrapType, 3> Wraps;
        int Custom1 = 0, Custom2 = 0;
        void* Shader = nullptr;
        bool SoftEnabled = false;
    };
    std::vector<std::unique_ptr<Entry>> entries_;
    size_t selected_ = size_t(-1);
    bool active_ = false;
    bool invalidate_ = false;
    bool BasicEligible(const Node& node, int count) const
    {
        auto basic = node.BasicParameterPtr;
        return active_ && selected_ != size_t(-1) && count > 0 && node.EffectPointer && basic
            && node.Billboard == Effekseer::BillboardType::Fixed && node.ZSort == Effekseer::ZSortType::None
            && basic->MaterialType == Effekseer::RendererMaterialType::File && basic->MaterialRenderDataPtr
            && basic->MaterialRenderDataPtr->MaterialIndex == 0
            && basic->MaterialRenderDataPtr->MaterialTextures.size() == 3
            && basic->MaterialRenderDataPtr->MaterialUniforms.size() == 15
            && basic->MaterialRenderDataPtr->MaterialGradients.empty()
            && this->m_renderer->GetRenderMode() == Effekseer::RenderMode::Normal
            && !this->m_renderer->GetExternalShaderSettings();
    }
    bool Matches(const Entry& entry, const Node& node) const
    {
        const auto data = node.BasicParameterPtr->MaterialRenderDataPtr;
        const auto material = entry.Value.Collector.MaterialDataPtr.Get();
        if (entry.Effect != node.EffectPointer || entry.RenderData != data || !material
            || material->IsSimpleVertex || material->IsRefractionRequired
            || material->CustomData1 != entry.Custom1 || material->CustomData2 != entry.Custom2
            || material->TextureCount != 3 || material->UniformCount != 15
            || material->UserPtr != entry.Shader
            || this->m_renderer->GetImpl()->isSoftParticleEnabled != entry.SoftEnabled) return false;
        for (size_t i = 0; i < 3; ++i)
            if (entry.Textures[i].Index != data->MaterialTextures[i].Index
                || entry.Textures[i].Type != data->MaterialTextures[i].Type
                || entry.Wraps[i] != material->TextureWrapTypes[i]) return false;
        return true;
    }
    static void Refresh(State& state, const Node& node, void* userData)
    {
        const auto basic = node.BasicParameterPtr;
        state.CullingType = Effekseer::CullingType::Double;
        state.DepthTest = node.ZTest; state.DepthWrite = node.ZWrite;
        state.Flipbook = EffekseerRenderer::ToState(basic->Flipbook);
        state.UVDistortionIntensity = basic->UVDistortionIntensity;
        state.TextureBlendType = basic->TextureBlendType;
        state.BlendUVDistortionIntensity = basic->BlendUVDistortionIntensity;
        state.EmissiveScaling = basic->EmissiveScaling;
        state.EdgeThreshold = basic->EdgeThreshold;
        for (size_t i = 0; i < 4; ++i) state.EdgeColor[i] = basic->EdgeColor[i];
        state.EdgeColorScaling = basic->EdgeColorScaling;
        state.IsAlphaCuttoffEnabled = basic->IsAlphaCutoffEnabled;
        state.Maginification = node.Maginification;
        state.Distortion = basic->MaterialType == Effekseer::RendererMaterialType::BackDistortion;
        state.DistortionIntensity = basic->DistortionIntensity;
        state.MaterialType = basic->MaterialType;
        state.RenderingUserData = node.UserData; state.HandleUserData = userData;
        state.LocalTime = node.LocalTime;
        state.AlphaBlend = basic->AlphaBlend;
        state.SoftParticleDistanceFar = basic->SoftParticleDistanceFar;
        state.SoftParticleDistanceNear = basic->SoftParticleDistanceNear;
        state.SoftParticleDistanceNearOffset = basic->SoftParticleDistanceNearOffset;
        for (size_t i = 0; i < 15; ++i) state.MaterialUniforms[i] = basic->MaterialRenderDataPtr->MaterialUniforms[i];
    }
public:
    explicit ScopedSprites(RENDERER* renderer) : Base(renderer) {}
    void BeginScope() { entries_.clear(); selected_ = size_t(-1); invalidate_ = false; active_ = true; }
    void EndScope() { entries_.clear(); selected_ = size_t(-1); invalidate_ = false; active_ = false; }
    // A shader fallback can occur while the standard queue still reads the current state.
    void InvalidateOnNextUse() { invalidate_ = true; }
    void SelectLayer(size_t index)
    {
        selected_ = index;
        if (active_ && entries_.size() <= index) entries_.resize(index + 1);
    }
private:
    void BeginCore(const Node& node, int32_t count, void* userData)
    {
        if (!BasicEligible(node, count))
        {
            entries_.clear(); selected_ = size_t(-1);
            Base::BeginRendering(node, count, userData); return;
        }
        if (invalidate_)
        {
            entries_.clear(); entries_.resize(selected_ + 1); invalidate_ = false;
        }
        auto& entry = entries_[selected_];
        if (entry && Matches(*entry, node))
        {
            auto& state = entry->Value;
            Refresh(state, node, userData);
            this->customData1Count_ = state.CustomData1Count; this->customData2Count_ = state.CustomData2Count;
            this->instanceMaxCount_ = (std::min)(count, this->m_renderer->GetSquareMaxCount());
            this->vertexCount_ = this->instanceMaxCount_ * 4;
            this->m_renderer->GetStandardRenderer()->BeginRenderingAndRenderingIfRequired(state,
                this->vertexCount_, this->stride_, reinterpret_cast<void*&>(this->m_ringBufferData));
            this->m_spriteCount = 0; this->instances.clear(); return;
        }
        entry.reset();
        Base::BeginRendering(node, count, userData);
        if (!this->m_ringBufferData) return;
        const auto& state = this->m_renderer->GetStandardRenderer()->GetState();
        const auto material = state.Collector.MaterialDataPtr.Get();
        if (state.Collector.ShaderType != EffekseerRenderer::RendererShaderType::Material || !material
            || material->IsSimpleVertex || material->IsRefractionRequired || material->TextureCount != 3
            || material->UniformCount != 15 || state.MaterialUniformCount != 15 || state.MaterialGradientCount != 0)
        { entries_.clear(); selected_ = size_t(-1); return; }
        entry = std::make_unique<Entry>(); entry->Value = state;
        entry->Effect = node.EffectPointer; entry->RenderData = node.BasicParameterPtr->MaterialRenderDataPtr;
        entry->Custom1 = material->CustomData1; entry->Custom2 = material->CustomData2;
        entry->Shader = material->UserPtr;
        entry->SoftEnabled = this->m_renderer->GetImpl()->isSoftParticleEnabled;
        for (size_t i = 0; i < 3; ++i)
        {
            entry->Textures[i] = entry->RenderData->MaterialTextures[i];
            entry->Wraps[i] = material->TextureWrapTypes[i];
        }
    }
public:
    void BeginRendering(const Node& node, int32_t count, void* userData) override
    {
        try { BeginCore(node, count, userData); }
        catch (...) { EndScope(); throw; }
    }
};

class Renderer : public EffekseerRendererDX11::RendererImplemented
{
    using Base = EffekseerRendererDX11::RendererImplemented;
    std::unique_ptr<SpriteDispatchQueue<Renderer, Shader>> sprites_;
    std::map<EffekseerRendererDX11::Shader*, std::unique_ptr<Shader>> fallbackShaders_;
    VertexBinding vertexBinding_;
    ScopedSprites<Renderer>* preparedSprites_ = nullptr;
public:
    explicit Renderer(int count) : Base(count) {}
    void InitializeSpriteQueue() { sprites_ = std::make_unique<SpriteDispatchQueue<Renderer, Shader>>(this); }
    SpriteDispatchQueue<Renderer, Shader>* GetStandardRenderer() { return sprites_.get(); }
    bool InitializeBulkSprites(const void* vs, size_t vsSize, const void* colour, size_t colourSize, const void* alpha, size_t alphaSize)
    { return sprites_ && sprites_->Initialize(vs, vsSize, colour, colourSize, alpha, alphaSize); }
    void FlushOriginalSprites() { Base::GetStandardRenderer()->Rendering(); }
    void AfterBulkSpriteDraw()
    {
        Base::EndShader(nullptr); vertexBinding_.Invalidate();
        if (preparedSprites_) preparedSprites_->InvalidateOnNextUse();
    }
    Effekseer::SpriteRendererRef CreateCachedSpriteRenderer()
    {
        auto sprites = Effekseer::MakeRefPtr<ScopedSprites<Renderer>>(this);
        preparedSprites_ = sprites.Get();
        return sprites;
    }
    VertexBinding* VertexBindingState() { return &vertexBinding_; }
    void InvalidateCachedVertexBinding()
    {
        vertexBinding_.Invalidate();
        if (sprites_) sprites_->Cancel();
        if (preparedSprites_) preparedSprites_->EndScope();
        preparedSprites_ = nullptr;
    }
    void BeginCachedSprites()
    {
        vertexBinding_.Invalidate();
        if (sprites_) sprites_->BeginScope();
        if (preparedSprites_) preparedSprites_->BeginScope();
    }
    void SelectCachedSpriteLayer(size_t layer) { if (preparedSprites_) preparedSprites_->SelectLayer(layer); }
    void FlushCachedSprites()
    {
        struct ResetOnExit
        {
            VertexBinding& State; ScopedSprites<Renderer>* Sprites; SpriteDispatchQueue<Renderer, Shader>* Queue;
            ~ResetOnExit() { State.Invalidate(); if (Queue) Queue->EndScope(); if (Sprites) Sprites->EndScope(); }
        } reset{vertexBinding_, preparedSprites_, sprites_.get()};
        sprites_->Rendering();
    }
    Shader* GetShader(EffekseerRenderer::RendererShaderType type)
    {
        auto original = Base::GetShader(type);
        if (!original) return nullptr;
        auto& shader = fallbackShaders_[original];
        if (!shader) shader = std::make_unique<Shader>(original, &vertexBinding_);
        return shader.get();
    }
    void BeginShader(Shader* shader) { Base::BeginShader(shader->Source()); }
    void EndShader(Shader* shader)
    {
        Base::EndShader(shader->Source());
        if (shader->UsedFallback() && preparedSprites_) preparedSprites_->InvalidateOnNextUse();
    }
    void SetLayout(Shader* shader) { Base::SetLayout(shader->Source()); }
    void SetTextures(Shader* shader, Effekseer::Backend::TextureRef* textures, int32_t count)
    {
        Base::SetTextures(shader->Source(), textures, count);
    }
};
}
