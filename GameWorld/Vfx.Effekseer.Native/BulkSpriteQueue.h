#pragma once
#include <EffekseerRendererDX11.RenderState.h>
#include <EffekseerRendererDX11.IndexBuffer.h>
#include <GraphicsDevice.h>
#include <cstdint>
#include <cstring>
#include <memory>
#include <vector>
#include <array>
#include <stdexcept>

namespace AecnSpriteUpload
{
class BulkSpriteGpu
{
    using State = EffekseerRenderer::StandardRendererState;
    using ComDevice = Microsoft::WRL::ComPtr<ID3D11Device>;
    ComDevice device_;
    Microsoft::WRL::ComPtr<ID3D11DeviceContext> context_;
    Microsoft::WRL::ComPtr<ID3D11VertexShader> vertex_;
    Microsoft::WRL::ComPtr<ID3D11PixelShader> colour_, alpha_;
    Microsoft::WRL::ComPtr<ID3D11InputLayout> layout_;
    Microsoft::WRL::ComPtr<ID3D11Buffer> vertices_, vertexConstants_, pixelConstants_;
    Microsoft::WRL::ComPtr<ID3D11BlendState> colourBlend_, alphaBlend_;
    std::unique_ptr<EffekseerRendererDX11::RenderState> states_;
    size_t vertexCapacity_ = 0;
public:
    static constexpr int OriginalStride = 72;
    static constexpr int Stride = 144;
    bool Initialize(EffekseerRendererDX11::RendererImplemented* renderer, const void* vs, size_t vsSize,
        const void* colour, size_t colourSize, const void* alpha, size_t alphaSize)
    {
        if (vertex_) return true;
        auto device = renderer->GetDevice(); auto context = renderer->GetContext();
        if (!device || !context || device->GetFeatureLevel() < D3D_FEATURE_LEVEL_11_0) return false;
        Microsoft::WRL::ComPtr<ID3D11VertexShader> vertex;
        Microsoft::WRL::ComPtr<ID3D11PixelShader> colourShader, alphaShader;
        Microsoft::WRL::ComPtr<ID3D11InputLayout> layout;
        Microsoft::WRL::ComPtr<ID3D11Buffer> vcb, pcb;
        Microsoft::WRL::ComPtr<ID3D11BlendState> colourBlend, alphaBlend;
        if (FAILED(device->CreateVertexShader(vs, vsSize, nullptr, vertex.GetAddressOf()))
            || FAILED(device->CreatePixelShader(colour, colourSize, nullptr, colourShader.GetAddressOf()))
            || FAILED(device->CreatePixelShader(alpha, alphaSize, nullptr, alphaShader.GetAddressOf()))) return false;
        const D3D11_INPUT_ELEMENT_DESC input[] = {
            {"POSITION",0,DXGI_FORMAT_R32G32B32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"NORMAL",0,DXGI_FORMAT_R8G8B8A8_UNORM,0,12,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"NORMAL",1,DXGI_FORMAT_R8G8B8A8_UNORM,0,16,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"NORMAL",2,DXGI_FORMAT_R8G8B8A8_UNORM,0,20,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,24,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",1,DXGI_FORMAT_R32G32_FLOAT,0,32,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",2,DXGI_FORMAT_R32G32B32A32_FLOAT,0,40,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",3,DXGI_FORMAT_R32G32B32A32_FLOAT,0,56,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",12,DXGI_FORMAT_R32G32B32A32_FLOAT,0,72,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",13,DXGI_FORMAT_R32G32B32A32_FLOAT,0,88,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",14,DXGI_FORMAT_R32G32B32A32_FLOAT,0,104,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",15,DXGI_FORMAT_R32G32B32A32_FLOAT,0,120,D3D11_INPUT_PER_VERTEX_DATA,0},
            {"TEXCOORD",16,DXGI_FORMAT_R32_UINT,0,136,D3D11_INPUT_PER_VERTEX_DATA,0}};
        if (FAILED(device->CreateInputLayout(input, ARRAYSIZE(input), vs, vsSize, layout.GetAddressOf()))) return false;
        D3D11_BUFFER_DESC buffer{}; buffer.Usage = D3D11_USAGE_DEFAULT; buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        buffer.ByteWidth = 416;
        if (FAILED(device->CreateBuffer(&buffer, nullptr, vcb.GetAddressOf()))) return false;
        buffer.ByteWidth = 368;
        if (FAILED(device->CreateBuffer(&buffer, nullptr, pcb.GetAddressOf()))) return false;
        D3D11_BLEND_DESC blend{}; auto& target = blend.RenderTarget[0]; target.BlendEnable = true;
        target.SrcBlend = D3D11_BLEND_SRC_ALPHA; target.DestBlend = D3D11_BLEND_INV_SRC1_ALPHA; target.BlendOp = D3D11_BLEND_OP_ADD;
        target.SrcBlendAlpha = D3D11_BLEND_ONE; target.DestBlendAlpha = D3D11_BLEND_ONE; target.BlendOpAlpha = D3D11_BLEND_OP_ADD;
        target.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_RED | D3D11_COLOR_WRITE_ENABLE_GREEN | D3D11_COLOR_WRITE_ENABLE_BLUE;
        if (FAILED(device->CreateBlendState(&blend, colourBlend.GetAddressOf()))) return false;
        target.SrcBlend = D3D11_BLEND_ONE; target.DestBlend = D3D11_BLEND_ZERO;
        target.BlendOpAlpha = D3D11_BLEND_OP_MAX; target.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALPHA;
        if (FAILED(device->CreateBlendState(&blend, alphaBlend.GetAddressOf()))) return false;
        auto states = std::make_unique<EffekseerRendererDX11::RenderState>(renderer, D3D11_COMPARISON_LESS_EQUAL, false);
        device_ = device; context_ = context; vertex_ = vertex; colour_ = colourShader; alpha_ = alphaShader;
        layout_ = layout; vertexConstants_ = vcb; pixelConstants_ = pcb;
        colourBlend_ = colourBlend; alphaBlend_ = alphaBlend; states_ = std::move(states);
        return true;
    }
    bool TargetAllowed(EffekseerRendererDX11::RendererImplemented* renderer) const
    {
        if (!vertex_ || renderer->GetDevice() != device_.Get() || renderer->GetContext() != context_.Get()) return false;
        Microsoft::WRL::ComPtr<ID3D11GeometryShader> geometry; context_->GSGetShader(geometry.GetAddressOf(), nullptr, nullptr);
        Microsoft::WRL::ComPtr<ID3D11HullShader> hull; context_->HSGetShader(hull.GetAddressOf(), nullptr, nullptr);
        Microsoft::WRL::ComPtr<ID3D11DomainShader> domain; context_->DSGetShader(domain.GetAddressOf(), nullptr, nullptr);
        if (geometry || hull || domain) return false;
        ID3D11RenderTargetView* views[8]{}; context_->OMGetRenderTargets(8, views, nullptr);
        bool one = views[0] != nullptr;
        for (int i = 1; i < 8; ++i) one = one && views[i] == nullptr;
        Microsoft::WRL::ComPtr<ID3D11Resource> resource;
        if (one) views[0]->GetResource(resource.GetAddressOf());
        for (auto view : views) if (view) view->Release();
        Microsoft::WRL::ComPtr<ID3D11Texture2D> texture;
        if (!one || FAILED(resource.As(&texture))) return false;
        D3D11_TEXTURE2D_DESC description{}; texture->GetDesc(&description);
        return description.Format == DXGI_FORMAT_R16G16B16A16_FLOAT;
    }
    void Draw(EffekseerRendererDX11::RendererImplemented* renderer, const State& source, const uint8_t* data, size_t bytes)
    {
        if (!bytes || bytes % (Stride * 4)) throw std::runtime_error("Invalid bulk sprite vertices");
        if (bytes > vertexCapacity_)
        {
            D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth = static_cast<UINT>(bytes);
            buffer.Usage = D3D11_USAGE_DYNAMIC; buffer.BindFlags = D3D11_BIND_VERTEX_BUFFER; buffer.CPUAccessFlags = D3D11_CPU_ACCESS_WRITE;
            Microsoft::WRL::ComPtr<ID3D11Buffer> vertices;
            if (FAILED(device_->CreateBuffer(&buffer, nullptr, vertices.GetAddressOf()))) throw std::runtime_error("Cannot allocate bulk sprite buffer");
            vertices_ = vertices; vertexCapacity_ = bytes;
        }
        D3D11_MAPPED_SUBRESOURCE mapped{};
        if (FAILED(context_->Map(vertices_.Get(), 0, D3D11_MAP_WRITE_DISCARD, 0, &mapped))) throw std::runtime_error("Cannot map bulk sprite vertices");
        std::memcpy(mapped.pData, data, bytes); context_->Unmap(vertices_.Get(), 0);
        std::array<float, 104> vs{}; std::array<float, 92> ps{};
        const auto camera = renderer->GetCameraMatrix(), projection = renderer->GetProjectionMatrix();
        std::memcpy(vs.data(), camera.Values, 64); std::memcpy(vs.data() + 16, projection.Values, 64);
        const bool flip = renderer->GetTextureUVStyle() == EffekseerRenderer::UVStyle::VerticalFlipped;
        const bool backFlip = renderer->GetBackgroundTextureUVStyle() == EffekseerRenderer::UVStyle::VerticalFlipped;
        const std::array<float, 4> uv = {flip ? 1.0f : 0.0f, flip ? -1.0f : 1.0f, backFlip ? 1.0f : 0.0f, backFlip ? -1.0f : 1.0f};
        const std::array<float, 4> predefined = {renderer->GetTime(), source.Maginification,
            renderer->GetImpl()->MaintainGammaColorInLinearColorSpace ? 1.0f : 0.0f, source.LocalTime};
        const auto position = renderer->GetCameraPosition();
        std::memcpy(vs.data() + 32, uv.data(), 16); std::memcpy(vs.data() + 36, predefined.data(), 16);
        vs[40] = position.X; vs[41] = position.Y; vs[42] = position.Z;
        std::memcpy(ps.data(), uv.data(), 16); std::memcpy(ps.data() + 4, predefined.data(), 16);
        ps[8] = position.X; ps[9] = position.Y; ps[10] = position.Z;
        Effekseer::Backend::TextureRef depth; EffekseerRenderer::DepthReconstructionParameter reconstruction;
        renderer->GetDepth(depth, reconstruction);
        ps[12] = reconstruction.DepthBufferScale; ps[13] = reconstruction.DepthBufferOffset;
        ps[16] = reconstruction.ProjectionMatrix33; ps[17] = reconstruction.ProjectionMatrix34;
        ps[18] = reconstruction.ProjectionMatrix43; ps[19] = reconstruction.ProjectionMatrix44;
        for (int i = 0; i < 15; ++i)
        {
            std::memcpy(vs.data() + 44 + i * 4, source.MaterialUniforms[i].data(), 16);
            std::memcpy(ps.data() + 32 + i * 4, source.MaterialUniforms[i].data(), 16);
        }
        context_->UpdateSubresource(vertexConstants_.Get(), 0, nullptr, vs.data(), 0, 0);
        context_->UpdateSubresource(pixelConstants_.Get(), 0, nullptr, ps.data(), 0, 0);
        struct Restore
        {
            EffekseerRendererDX11::OriginalState State;
            ID3D11Device* Device; ID3D11DeviceContext* Context;
            Restore(ID3D11Device* device, ID3D11DeviceContext* context) : Device(device), Context(context) { State.SaveState(device, context); }
            ~Restore() { State.LoadState(Device, Context); }
        } restore(device_.Get(), context_.Get());
        auto& state = states_->GetActiveState();
        state.DepthTest = source.DepthTest; state.DepthWrite = false; state.CullingType = source.CullingType;
        state.AlphaBlend = Effekseer::AlphaBlendType::Blend;
        state.TextureFilterTypes = source.Collector.TextureFilterTypes; state.TextureWrapTypes = source.Collector.TextureWrapTypes;
        states_->Update(true);
        auto textures = source.Collector.Textures;
        if (source.Collector.IsDepthRequired)
        {
            if (depth == nullptr) depth = renderer->GetImpl()->GetProxyTexture(EffekseerRenderer::ProxyTextureType::White);
            textures[source.Collector.DepthIndex] = depth;
        }
        std::array<ID3D11ShaderResourceView*, Effekseer::TextureSlotMax> views{};
        for (int i = 0; i < source.Collector.TextureCount; ++i)
            if (textures[i] != nullptr) views[i] = static_cast<EffekseerRendererDX11::Backend::Texture*>(textures[i].Get())->GetSRV();
        context_->PSSetShaderResources(0, source.Collector.TextureCount, views.data());
        context_->VSSetShader(vertex_.Get(), nullptr, 0); context_->IASetInputLayout(layout_.Get());
        auto vcb = vertexConstants_.Get(), pcb = pixelConstants_.Get();
        context_->VSSetConstantBuffers(0, 1, &vcb); context_->PSSetConstantBuffers(0, 1, &pcb);
        const UINT stride = Stride, offset = 0; auto vertices = vertices_.Get();
        context_->IASetVertexBuffers(0, 1, &vertices, &stride, &offset);
        context_->IASetIndexBuffer(renderer->GetIndexBuffer()->GetInterface(), DXGI_FORMAT_R16_UINT, 0);
        context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        const int quads = static_cast<int>(bytes / (Stride * 4));
        context_->OMSetBlendState(colourBlend_.Get(), nullptr, 0xffffffff); context_->PSSetShader(colour_.Get(), nullptr, 0);
        renderer->DrawSprites(quads, 0);
        context_->OMSetBlendState(alphaBlend_.Get(), nullptr, 0xffffffff); context_->PSSetShader(alpha_.Get(), nullptr, 0);
        renderer->DrawSprites(quads, 0);
    }
};

template<class RENDERER, class SHADER>
class SpriteDispatchQueue
{
    using State = EffekseerRenderer::StandardRendererState;
    using Legacy = EffekseerRenderer::StandardRenderer<RENDERER, SHADER>;
    struct ShortBatch { State Value; size_t Offset; int Vertices; };
    RENDERER* renderer_;
    std::unique_ptr<Legacy> legacy_;
    BulkSpriteGpu gpu_;
    std::unique_ptr<State> current_, run_;
    std::vector<uint8_t> vertices_;
    std::vector<ShortBatch> short_;
    bool allowed_ = false, direct_ = false, useBulk_ = false, legacyPending_ = false;
    static bool Eligible(const State& state)
    {
        return state.MaterialType == Effekseer::RendererMaterialType::File
            && state.Collector.ShaderType == EffekseerRenderer::RendererShaderType::Material
            && state.Collector.MaterialRenderDataPtr && state.Collector.MaterialRenderDataPtr->MaterialIndex == 0
            && state.Collector.MaterialDataPtr != nullptr && !state.Collector.MaterialDataPtr->IsRefractionRequired
            && state.MaterialUniformCount == 15 && state.MaterialGradientCount == 0
            && state.CustomData1Count == 4 && state.CustomData2Count == 4
            && !state.DepthWrite && state.CullingType == Effekseer::CullingType::Double
            && state.RenderingUserData == nullptr && state.HandleUserData == nullptr
            && (state.AlphaBlend == Effekseer::AlphaBlendType::Blend || state.AlphaBlend == Effekseer::AlphaBlendType::Add)
            && state.Collector.TextureCount <= Effekseer::TextureSlotMax;
    }
    static bool Compatible(const State& a, const State& b)
    {
        if (a.DepthTest != b.DepthTest || a.CullingType != b.CullingType
            || std::memcmp(&a.Maginification, &b.Maginification, 4) || std::memcmp(&a.LocalTime, &b.LocalTime, 4)
            || a.Collector.MaterialDataPtr != b.Collector.MaterialDataPtr || a.Collector != b.Collector) return false;
        for (int i = 1; i < 12; ++i)
            if (std::memcmp(a.MaterialUniforms[i].data(), b.MaterialUniforms[i].data(), 16)) return false;
        return true;
    }
    void ClearRun() { vertices_.clear(); short_.clear(); run_.reset(); useBulk_ = false; }
    void FlushRun()
    {
        if (!run_) return;
        try
        {
            if (useBulk_)
            {
                if (legacyPending_) { legacy_->Rendering(); legacyPending_ = false; }
                gpu_.Draw(renderer_, *run_, vertices_.data(), vertices_.size());
                renderer_->AfterBulkSpriteDraw();
            }
            else
            {
                for (const auto& batch : short_)
                {
                    int stride = 0; void* data = nullptr;
                    legacy_->BeginRenderingAndRenderingIfRequired(batch.Value, batch.Vertices, stride, data);
                    legacyPending_ = true;
                    if (!data || stride != BulkSpriteGpu::OriginalStride) throw std::runtime_error("Invalid sprite replay buffer");
                    for (int i = 0; i < batch.Vertices; ++i)
                        std::memcpy(static_cast<uint8_t*>(data) + i * stride, vertices_.data() + batch.Offset + i * BulkSpriteGpu::Stride, stride);
                }
                legacy_->Rendering(); legacyPending_ = false;
            }
        }
        catch (...) { ClearRun(); renderer_->AfterBulkSpriteDraw(); throw; }
        ClearRun();
    }
public:
    explicit SpriteDispatchQueue(RENDERER* renderer) : renderer_(renderer), legacy_(std::make_unique<Legacy>(renderer)) {}
    bool Initialize(const void* vs, size_t vsSize, const void* colour, size_t colourSize, const void* alpha, size_t alphaSize)
    { return gpu_.Initialize(renderer_, vs, vsSize, colour, colourSize, alpha, alphaSize); }
    void BeginScope() { EndScope(); allowed_ = gpu_.TargetAllowed(renderer_); }
    void EndScope() { ClearRun(); current_.reset(); allowed_ = false; direct_ = false; }
    void Cancel()
    {
        EndScope();
        if (legacyPending_) { legacy_ = std::make_unique<Legacy>(renderer_); legacyPending_ = false; }
    }
    State& GetState() { return direct_ ? *current_ : legacy_->GetState(); }
    void BeginRenderingAndRenderingIfRequired(const State& state, int count, int& stride, void*& data)
    {
        if (!allowed_ || !Eligible(state) || Legacy::CalculateCurrentStride(state) != BulkSpriteGpu::OriginalStride)
        {
            FlushRun(); direct_ = false;
            legacy_->BeginRenderingAndRenderingIfRequired(state, count, stride, data); legacyPending_ = true; return;
        }
        if (legacyPending_) { legacy_->Rendering(); legacyPending_ = false; }
        const size_t bytes = static_cast<size_t>(count) * BulkSpriteGpu::Stride;
        if (run_ && (!Compatible(*run_, state) || vertices_.size() + bytes > static_cast<size_t>(renderer_->GetSquareMaxCount()) * 4 * BulkSpriteGpu::Stride)) FlushRun();
        if (!run_) run_ = std::make_unique<State>(state);
        const auto offset = vertices_.size();
        if (!useBulk_)
        {
            if (!short_.empty() && short_.back().Value == state) short_.back().Vertices += count;
            else short_.push_back({state, offset, count});
            // Two real passes must replace at least four legacy state/draw runs.
            if (short_.size() >= 4) { useBulk_ = true; short_.clear(); }
        }
        if (!current_) current_ = std::make_unique<State>();
        *current_ = state;
        vertices_.resize(offset + bytes);
        const uint32_t tag = state.AlphaBlend == Effekseer::AlphaBlendType::Blend ? 1 : 2;
        for (int i = 0; i < count; ++i)
        {
            auto vertex = vertices_.data() + offset + i * BulkSpriteGpu::Stride;
            std::memcpy(vertex + 72, state.MaterialUniforms[0].data(), 16);
            std::memcpy(vertex + 88, state.MaterialUniforms[12].data(), 48);
            std::memcpy(vertex + 136, &tag, 4);
        }
        stride = BulkSpriteGpu::Stride; data = vertices_.data() + offset; direct_ = true;
    }
    void Rendering() { FlushRun(); if (legacyPending_) { legacy_->Rendering(); legacyPending_ = false; } }
};
}
