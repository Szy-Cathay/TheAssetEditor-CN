#pragma once
#include <d3d11.h>
#include <wrl/client.h>
#include <DirectXPackedVector.h>
#include <DirectXMath.h>
#include <EffekseerRendererDX11.RendererImplemented.h>
#include <array>
#include <vector>
#include <cmath>
#include <algorithm>
#include <cstring>
#include <cstdint>
#include <limits>
#include <stdexcept>
#include "Bridge.h"
#include "CompiledVfxMaterials.h"

// Ordinary HALFRES particles share moments before any colour reconstruction.
class HalfResolutionScene
{
    template<class T> using Ptr = Microsoft::WRL::ComPtr<T>;
public:
    struct Draw
    {
        const AecnLayer* Layer = nullptr;
        const AecnHalfLayerConfig* Config = nullptr;
        const AecnParticle* Particles = nullptr;
        int Count = 0;
        const float* View = nullptr;
        const float* Projection = nullptr;
        const AecnLighting* Lighting = nullptr;
        std::array<ID3D11ShaderResourceView*, 3> Textures{};
        ID3D11ShaderResourceView* SoftDepth = nullptr;
        bool HalfResolution = true;
    };
private:
    struct Surface
    {
        Ptr<ID3D11Texture2D> Texture;
        Ptr<ID3D11ShaderResourceView> View;
        Ptr<ID3D11RenderTargetView> Target;
        Ptr<ID3D11DepthStencilView> Depth, ReadOnlyDepth;
    } b0_, b3456_, b12_, colour_, halfDepth_, quarterDepth_;
    struct FullSet
    {
        Surface B0, B3456, B12, Colour, ResolvedB0, ResolvedB3456, ResolvedB12;
        UINT Width = 0, Height = 0, Samples = 0;
    } full_[2];
    FullSet* activeFull_ = nullptr;
    struct Vertex
    {
        float Position[3], Colour[4];
        uint32_t Normal, Tangent;
        float UV[2], UV2[2], Custom1[4], Custom2[4], Dynamic[4][4];
        uint32_t Tag, Padding[2];
    };
    static_assert(sizeof(Vertex) == 160, "Half-resolution vertex layout changed");
    struct Prepared { const Draw* Source; size_t FirstVertex; UINT Count; std::vector<const Draw*> Runs; };
    std::vector<Vertex> vertices_;
    std::vector<Prepared> prepared_;
    size_t preparedCount_ = 0;
    Ptr<ID3D11VertexShader> vertex_, fullscreen_;
    Ptr<ID3D11PixelShader> moments_, colourShader_, merge_, compose_, downsample_, moments4_, colour4_, compose4_;
    Ptr<ID3D11InputLayout> layout_;
    Ptr<ID3D11Buffer> vertexBuffer_, indexBuffer_, vertexConstants_, pixelConstants_, halfConstants_;
    Ptr<ID3D11SamplerState> sampler_;
    Ptr<ID3D11RasterizerState> raster_;
    Ptr<ID3D11BlendState> additive_, composite_, replace_;
    Ptr<ID3D11DepthStencilState> readDepth_, noDepth_, writeDepth_;
    Ptr<ID3D11DepthStencilView> originalDepth_, fullReadOnlyDepth_;
    UINT width_ = 0, height_ = 0;
    size_t vertexCapacity_ = 0, indexCapacity_ = 0;
    unsigned drawCalls_ = 0;
    static void Check(HRESULT result)
    { if (FAILED(result)) throw std::runtime_error("Native half-resolution resource operation failed"); }
    static bool Describe(ID3D11ShaderResourceView* view, D3D11_TEXTURE2D_DESC& desc)
    {
        if (!view) return false;
        D3D11_SHADER_RESOURCE_VIEW_DESC srv{}; view->GetDesc(&srv);
        if (srv.ViewDimension != D3D11_SRV_DIMENSION_TEXTURE2D || srv.Texture2D.MostDetailedMip != 0) return false;
        Ptr<ID3D11Resource> resource; view->GetResource(resource.GetAddressOf());
        Ptr<ID3D11Texture2D> texture;
        if (FAILED(resource.As(&texture))) return false;
        texture->GetDesc(&desc);
        return desc.SampleDesc.Count == 1 && desc.ArraySize == 1;
    }
    static bool Target(ID3D11DeviceContext* context, D3D11_TEXTURE2D_DESC& desc, D3D11_VIEWPORT& viewport,
        bool& hasDepth)
    {
        if (!context) return false;
        ID3D11RenderTargetView* targets[8]{}; Ptr<ID3D11DepthStencilView> depth;
        context->OMGetRenderTargets(8, targets, depth.GetAddressOf());
        Ptr<ID3D11RenderTargetView> target; target.Attach(targets[0]);
        bool one = target != nullptr;
        for (int i = 1; i < 8; ++i) if (targets[i]) { one = false; targets[i]->Release(); }
        if (!one) return false;
        D3D11_RENDER_TARGET_VIEW_DESC rtv{}; target->GetDesc(&rtv);
        if (rtv.Format != DXGI_FORMAT_R16G16B16A16_FLOAT) return false;
        Ptr<ID3D11Resource> resource; target->GetResource(resource.GetAddressOf());
        Ptr<ID3D11Texture2D> texture;
        if (FAILED(resource.As(&texture))) return false;
        texture->GetDesc(&desc);
        UINT count = 1; context->RSGetViewports(&count, &viewport);
        hasDepth = depth != nullptr;
        return count == 1 && viewport.TopLeftX == 0 && viewport.TopLeftY == 0
            && viewport.Width >= 2 && viewport.Height >= 2 && viewport.MinDepth == 0 && viewport.MaxDepth == 1
            && viewport.Width == std::floor(viewport.Width) && viewport.Height == std::floor(viewport.Height)
            && viewport.Width <= desc.Width && viewport.Height <= desc.Height
            && desc.Width >= 4 && desc.Height >= 4 && desc.MipLevels == 1 && desc.ArraySize == 1
            && desc.SampleDesc.Quality == 0 && (desc.SampleDesc.Count == 1 || desc.SampleDesc.Count == 4);
    }
    struct Restore
    {
        ID3D11Device* Device;
        ID3D11DeviceContext* Context;
        EffekseerRendererDX11::OriginalState Pipeline;
        std::array<Ptr<ID3D11RenderTargetView>, 8> Targets;
        std::array<Ptr<ID3D11UnorderedAccessView>, D3D11_PS_CS_UAV_REGISTER_COUNT> Unordered;
        Ptr<ID3D11DepthStencilView> Depth;
        Ptr<ID3D11Buffer> VertexExtra, PixelExtra;
        std::array<D3D11_VIEWPORT, D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE> Viewports;
        UINT ViewportCount = static_cast<UINT>(Viewports.size()), TargetCount = 0;
        Restore(ID3D11Device* device, ID3D11DeviceContext* context) : Device(device), Context(context)
        {
            Pipeline.SaveState(device, context);
            ID3D11RenderTargetView* targets[8]{}; context->OMGetRenderTargets(8, targets, Depth.GetAddressOf());
            for (UINT i = 0; i < Targets.size(); ++i) { Targets[i].Attach(targets[i]); if (targets[i]) TargetCount = i + 1; }
            ID3D11UnorderedAccessView* unordered[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
            context->OMGetRenderTargetsAndUnorderedAccessViews(0, nullptr, nullptr, 0, ARRAYSIZE(unordered), unordered);
            for (UINT i = 0; i < Unordered.size(); ++i) Unordered[i].Attach(unordered[i]);
            context->VSGetConstantBuffers(1, 1, VertexExtra.GetAddressOf());
            context->PSGetConstantBuffers(1, 1, PixelExtra.GetAddressOf());
            context->RSGetViewports(&ViewportCount, Viewports.data());
        }
        ~Restore()
        {
            ID3D11ShaderResourceView* empty[8]{}; Context->PSSetShaderResources(0, 8, empty);
            Pipeline.LoadState(Device, Context);
            auto v = VertexExtra.Get(), p = PixelExtra.Get();
            Context->VSSetConstantBuffers(1, 1, &v); Context->PSSetConstantBuffers(1, 1, &p);
            ID3D11RenderTargetView* targets[8]{};
            for (UINT i = 0; i < Targets.size(); ++i) targets[i] = Targets[i].Get();
            ID3D11UnorderedAccessView* unordered[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
            for (UINT i = 0; i < Unordered.size(); ++i) unordered[i] = Unordered[i].Get();
            Context->OMSetRenderTargetsAndUnorderedAccessViews(TargetCount, targets, Depth.Get(), TargetCount,
                static_cast<UINT>(Unordered.size()) - TargetCount, unordered + TargetCount, nullptr);
            Context->RSSetViewports(ViewportCount, Viewports.data());
        }
    };
    static Surface MakeSurface(ID3D11Device* device, UINT width, UINT height, DXGI_FORMAT format, bool depth = false, UINT samples = 1)
    {
        Surface result;
        D3D11_TEXTURE2D_DESC d{}; d.Width = width; d.Height = height; d.MipLevels = d.ArraySize = 1;
        d.Format = format; d.SampleDesc.Count = samples; d.Usage = D3D11_USAGE_DEFAULT;
        d.BindFlags = D3D11_BIND_SHADER_RESOURCE | (depth ? D3D11_BIND_DEPTH_STENCIL : D3D11_BIND_RENDER_TARGET);
        Check(device->CreateTexture2D(&d, nullptr, result.Texture.GetAddressOf()));
        D3D11_SHADER_RESOURCE_VIEW_DESC srv{}; srv.Format = depth ? DXGI_FORMAT_R24_UNORM_X8_TYPELESS : format;
        srv.ViewDimension = samples > 1 ? D3D11_SRV_DIMENSION_TEXTURE2DMS : D3D11_SRV_DIMENSION_TEXTURE2D;
        if (samples == 1) srv.Texture2D.MipLevels = 1;
        Check(device->CreateShaderResourceView(result.Texture.Get(), &srv, result.View.GetAddressOf()));
        if (depth)
        {
            D3D11_DEPTH_STENCIL_VIEW_DESC view{}; view.Format = DXGI_FORMAT_D24_UNORM_S8_UINT;
            view.ViewDimension = D3D11_DSV_DIMENSION_TEXTURE2D;
            Check(device->CreateDepthStencilView(result.Texture.Get(), &view, result.Depth.GetAddressOf()));
            view.Flags = D3D11_DSV_READ_ONLY_DEPTH | D3D11_DSV_READ_ONLY_STENCIL;
            Check(device->CreateDepthStencilView(result.Texture.Get(), &view, result.ReadOnlyDepth.GetAddressOf()));
        }
        else Check(device->CreateRenderTargetView(result.Texture.Get(), nullptr, result.Target.GetAddressOf()));
        return result;
    }
    void Prepare(ID3D11Device* device, UINT width, UINT height, UINT samples)
    {
        if (!vertex_)
        {
            const auto& v = CompiledHalfSceneShaders[0]; const auto& f = CompiledHalfSceneShaders[3];
            Check(device->CreateVertexShader(v.Data, v.Size, nullptr, vertex_.GetAddressOf()));
            Check(device->CreateVertexShader(f.Data, f.Size, nullptr, fullscreen_.GetAddressOf()));
            auto pixel = [&](size_t i, Ptr<ID3D11PixelShader>& shader) {
                const auto& code = CompiledHalfSceneShaders[i];
                Check(device->CreatePixelShader(code.Data, code.Size, nullptr, shader.GetAddressOf()));
            };
            pixel(1, moments_); pixel(2, colourShader_); pixel(4, merge_); pixel(5, compose_); pixel(6, downsample_);
            pixel(7, moments4_); pixel(8, colour4_); pixel(9, compose4_);
            const D3D11_INPUT_ELEMENT_DESC input[] = {
                {"POSITION",0,DXGI_FORMAT_R32G32B32_FLOAT,0,0,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"NORMAL",0,DXGI_FORMAT_R32G32B32A32_FLOAT,0,12,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"NORMAL",1,DXGI_FORMAT_R8G8B8A8_UNORM,0,28,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"NORMAL",2,DXGI_FORMAT_R8G8B8A8_UNORM,0,32,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",0,DXGI_FORMAT_R32G32_FLOAT,0,36,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",1,DXGI_FORMAT_R32G32_FLOAT,0,44,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",2,DXGI_FORMAT_R32G32B32A32_FLOAT,0,52,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",3,DXGI_FORMAT_R32G32B32A32_FLOAT,0,68,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",12,DXGI_FORMAT_R32G32B32A32_FLOAT,0,84,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",13,DXGI_FORMAT_R32G32B32A32_FLOAT,0,100,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",14,DXGI_FORMAT_R32G32B32A32_FLOAT,0,116,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",15,DXGI_FORMAT_R32G32B32A32_FLOAT,0,132,D3D11_INPUT_PER_VERTEX_DATA,0},
                {"TEXCOORD",16,DXGI_FORMAT_R32_UINT,0,148,D3D11_INPUT_PER_VERTEX_DATA,0}};
            Check(device->CreateInputLayout(input, ARRAYSIZE(input), v.Data, v.Size, layout_.GetAddressOf()));
            auto buffer = [&](UINT bytes, Ptr<ID3D11Buffer>& target) {
                D3D11_BUFFER_DESC desc{}; desc.ByteWidth = bytes; desc.Usage = D3D11_USAGE_DEFAULT;
                desc.BindFlags = D3D11_BIND_CONSTANT_BUFFER; Check(device->CreateBuffer(&desc, nullptr, target.GetAddressOf()));
            };
            buffer(416, vertexConstants_); buffer(368, pixelConstants_); buffer(64, halfConstants_);
            D3D11_SAMPLER_DESC sampler{}; sampler.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
            sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP;
            sampler.MaxLOD = D3D11_FLOAT32_MAX; Check(device->CreateSamplerState(&sampler, sampler_.GetAddressOf()));
            D3D11_RASTERIZER_DESC raster{}; raster.FillMode = D3D11_FILL_SOLID; raster.CullMode = D3D11_CULL_NONE;
            raster.DepthClipEnable = true; raster.MultisampleEnable = true;
            Check(device->CreateRasterizerState(&raster, raster_.GetAddressOf()));
            D3D11_BLEND_DESC blend{}; auto& rt = blend.RenderTarget[0]; rt.RenderTargetWriteMask = 15;
            Check(device->CreateBlendState(&blend, replace_.GetAddressOf()));
            rt.BlendEnable = true; rt.SrcBlend = rt.DestBlend = rt.SrcBlendAlpha = rt.DestBlendAlpha = D3D11_BLEND_ONE;
            rt.BlendOp = rt.BlendOpAlpha = D3D11_BLEND_OP_ADD;
            Check(device->CreateBlendState(&blend, additive_.GetAddressOf()));
            rt.DestBlend = rt.DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
            rt.BlendOpAlpha = D3D11_BLEND_OP_MAX;
            Check(device->CreateBlendState(&blend, composite_.GetAddressOf()));
            D3D11_DEPTH_STENCIL_DESC depth{}; Check(device->CreateDepthStencilState(&depth, noDepth_.GetAddressOf()));
            depth.DepthEnable = true; depth.DepthFunc = D3D11_COMPARISON_LESS_EQUAL;
            Check(device->CreateDepthStencilState(&depth, readDepth_.GetAddressOf()));
            depth.DepthFunc = D3D11_COMPARISON_ALWAYS; depth.DepthWriteMask = D3D11_DEPTH_WRITE_MASK_ALL;
            Check(device->CreateDepthStencilState(&depth, writeDepth_.GetAddressOf()));
        }
        if (width_ != width || height_ != height)
        {
        const auto halfWidth = width / 2, halfHeight = height / 2;
        auto b0 = MakeSurface(device, halfWidth, halfHeight, DXGI_FORMAT_R32_FLOAT);
        auto b3456 = MakeSurface(device, halfWidth, halfHeight, DXGI_FORMAT_R32G32B32A32_FLOAT);
        auto b12 = MakeSurface(device, halfWidth, halfHeight, DXGI_FORMAT_R32G32_FLOAT);
        auto colour = MakeSurface(device, halfWidth, halfHeight, DXGI_FORMAT_R16G16B16A16_FLOAT);
        auto halfDepth = MakeSurface(device, halfWidth, halfHeight, DXGI_FORMAT_R24G8_TYPELESS, true);
        auto quarterDepth = MakeSurface(device, halfWidth / 2, halfHeight / 2, DXGI_FORMAT_R24G8_TYPELESS, true);
        b0_ = std::move(b0); b3456_ = std::move(b3456); b12_ = std::move(b12); colour_ = std::move(colour);
        halfDepth_ = std::move(halfDepth); quarterDepth_ = std::move(quarterDepth);
        width_ = width; height_ = height;
        full_[0] = {}; full_[1] = {};
        }
        auto& full = full_[samples == 4 ? 1 : 0];
        if (full.Width != width || full.Height != height || full.Samples != samples)
        {
            FullSet next;
            next.B0 = MakeSurface(device,width,height,DXGI_FORMAT_R32_FLOAT,false,samples);
            next.B3456 = MakeSurface(device,width,height,DXGI_FORMAT_R32G32B32A32_FLOAT,false,samples);
            next.B12 = MakeSurface(device,width,height,DXGI_FORMAT_R32G32_FLOAT,false,samples);
            next.Colour = MakeSurface(device,width,height,DXGI_FORMAT_R16G16B16A16_FLOAT,false,samples);
            if (samples > 1)
            {
                next.ResolvedB0 = MakeSurface(device,width,height,DXGI_FORMAT_R32_FLOAT);
                next.ResolvedB3456 = MakeSurface(device,width,height,DXGI_FORMAT_R32G32B32A32_FLOAT);
                next.ResolvedB12 = MakeSurface(device,width,height,DXGI_FORMAT_R32G32_FLOAT);
            }
            next.Width=width; next.Height=height; next.Samples=samples;
            full=std::move(next);
        }
        activeFull_ = &full;
    }
    static uint32_t PackedDirection(const float* value)
    {
        const auto length = std::sqrt(value[0]*value[0] + value[1]*value[1] + value[2]*value[2]);
        uint32_t result = 0xff000000;
        for (int c = 0; c < 3; ++c)
        {
            const auto component = length > 0 ? value[c] / length : (c == 2 ? 1.0f : 0.0f);
            const auto byte = static_cast<uint32_t>(std::max(0.0f, std::min(255.0f, (component * .5f + .5f) * 255)));
            result |= byte << (c * 8);
        }
        return result;
    }
    static void Planes(const Draw& draw, float& nearPlane, float& farPlane)
    {
        const auto* p = draw.Projection;
        nearPlane = std::abs(p[14] / p[10]);
        farPlane = std::abs((p[15] - p[14]) / (p[10] - p[11]));
    }
    static bool Compatible(const Draw& a, const Draw& b)
    {
        return a.HalfResolution==b.HalfResolution && a.Textures==b.Textures && a.SoftDepth==b.SoftDepth
            && !std::memcmp(a.Layer,b.Layer,sizeof(AecnLayer))
            && a.Config->Premultiplied==b.Config->Premultiplied
            && !std::memcmp(&a.Config->CoveredColourMultiplier,&b.Config->CoveredColourMultiplier,8)
            && !std::memcmp(a.View,b.View,64) && !std::memcmp(a.Projection,b.Projection,64)
            && (a.Lighting==b.Lighting || (a.Lighting && b.Lighting && !std::memcmp(a.Lighting,b.Lighting,sizeof(AecnLighting))));
    }
    void PrepareGeometry(ID3D11Device* device, ID3D11DeviceContext* context, const std::vector<Draw>& draws)
    {
        vertices_.clear(); preparedCount_=0; size_t count = 0;
        for (const auto& draw : draws) count += static_cast<size_t>(draw.Count);
        vertices_.reserve(count * 4); prepared_.reserve(draws.size());
        for (const auto& draw : draws)
        {
            size_t group=0;
            while(group<preparedCount_ && !Compatible(*prepared_[group].Source,draw))++group;
            if(group==preparedCount_)
            {
                if(group==prepared_.size())prepared_.push_back({});
                auto& entry=prepared_[group]; entry.Source=&draw; entry.Count=0; entry.Runs.clear(); ++preparedCount_;
            }
            prepared_[group].Runs.push_back(&draw); prepared_[group].Count+=static_cast<UINT>(draw.Count);
        }
        for(size_t group=0;group<preparedCount_;++group)
        {
            auto& entry=prepared_[group]; entry.FirstVertex=vertices_.size();
            for(const auto* source:entry.Runs)
            {
            const auto& draw=*source;
            const auto& layer = *draw.Layer;
            float nearPlane, farPlane; Planes(draw, nearPlane, farPlane);
            std::array<std::array<float, 2>, 4> positions = {{{-layer.Pivot[0],-layer.Pivot[1]},
                {1-layer.Pivot[0],-layer.Pivot[1]}, {-layer.Pivot[0],1-layer.Pivot[1]}, {1-layer.Pivot[0],1-layer.Pivot[1]}}};
            const auto original = positions;
            if (layer.UvRotation == 1) positions = {original[1],original[3],original[0],original[2]};
            else if (layer.UvRotation == 2) positions = {original[2],original[0],original[3],original[1]};
            if (layer.CameraFacing && draw.Projection[0]*draw.Projection[5]-draw.Projection[1]*draw.Projection[4] < 0)
                for (auto& p : positions) p[0] = -p[0];
            for (int i = 0; i < draw.Count; ++i)
            {
                const auto& particle = draw.Particles[i]; float softBias = 0;
                if (layer.NativeSoftDepth)
                {
                    using namespace DirectX::PackedVector;
                    const auto factor = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.SoftDistance));
                    const auto strength = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.DistortionSoftScaleStrength));
                    softBias = factor * (1 + strength * (std::max(0.0f, std::min(1.0f, particle.SoftParticleScale)) - 1));
                }
                const auto viewZ = particle.Transform[12]*draw.View[2] + particle.Transform[13]*draw.View[6]
                    + particle.Transform[14]*draw.View[10] + draw.View[14];
                float nextX = 0, nextY = 0, blend = 0;
                if (layer.AtlasBlend)
                {
                    const int columns = std::max(1, layer.AtlasColumns), rows = std::max(1, layer.AtlasRows);
                    const auto current = static_cast<int>(std::floor(particle.Flipbook));
                    const auto next = layer.AtlasLoop ? (current + 1) % (columns * rows) : std::min(current + 1, columns * rows - 1);
                    nextX = (next % columns - current % columns) * std::abs(particle.UV[2]);
                    nextY = (next / columns - current / columns) * std::abs(particle.UV[3]);
                    blend = particle.Flipbook - current;
                }
                for (int corner = 0; corner < 4; ++corner)
                {
                    Vertex vertex{}; const auto& xy = positions[corner];
                    for (int c = 0; c < 3; ++c) vertex.Position[c] = xy[0]*particle.Transform[c]
                        + xy[1]*particle.Transform[4+c] + particle.Transform[12+c];
                    std::copy_n(particle.Colour, 4, vertex.Colour);
                    vertex.Normal = PackedDirection(particle.Transform+8); vertex.Tangent = PackedDirection(particle.Transform);
                    vertex.UV[0] = particle.UV[0] + (corner % 2) * particle.UV[2];
                    vertex.UV[1] = particle.UV[1] + (corner < 2 ? particle.UV[3] : 0);
                    std::copy_n(vertex.UV, 2, vertex.UV2);
                    vertex.Custom1[0]=nextX; vertex.Custom1[1]=nextY; vertex.Custom1[2]=blend; vertex.Custom1[3]=particle.ParticleSize;
                    std::copy_n(particle.NormalRotation, 4, vertex.Custom2);
                    vertex.Dynamic[0][0]=1; vertex.Dynamic[0][1]=layer.NativeSoftDepth ? softBias+.001f : layer.SoftDistance;
                    vertex.Dynamic[0][2]=static_cast<float>(draw.Config->Premultiplied); vertex.Dynamic[0][3]=layer.NormalIndex >= 0 ? 1.0f : 0.0f;
                    vertex.Dynamic[1][0]=1; vertex.Dynamic[1][1]=layer.EmissionShapeWeight;
                    vertex.Dynamic[1][2]=particle.AlphaCrush[0]; vertex.Dynamic[1][3]=particle.AlphaCrush[1];
                    vertex.Dynamic[2][0]=particle.UV[2] < 0 ? -1.0f : 1.0f; vertex.Dynamic[2][1]=particle.UV[3] < 0 ? -1.0f : 1.0f;
                    vertex.Dynamic[3][0]=1.0f/(draw.HalfResolution?width_/2:width_);
                    vertex.Dynamic[3][1]=1.0f/(draw.HalfResolution?height_/2:height_);
                    vertex.Dynamic[3][2]=softBias; vertex.Dynamic[3][3]=layer.NativeSoftDepth ? (viewZ < 0 ? nearPlane : -nearPlane) : 0;
                    vertices_.push_back(vertex);
                }
            }
            }
        }
        const size_t bytes = vertices_.size() * sizeof(Vertex);
        if (bytes > vertexCapacity_)
        {
            D3D11_BUFFER_DESC d{}; d.ByteWidth=static_cast<UINT>(bytes); d.Usage=D3D11_USAGE_DYNAMIC;
            d.BindFlags=D3D11_BIND_VERTEX_BUFFER; d.CPUAccessFlags=D3D11_CPU_ACCESS_WRITE;
            Ptr<ID3D11Buffer> buffer; Check(device->CreateBuffer(&d,nullptr,buffer.GetAddressOf()));
            vertexBuffer_=buffer; vertexCapacity_=bytes;
        }
        const auto maxCount = std::max_element(prepared_.begin(),prepared_.begin()+preparedCount_,
            [](const Prepared& a,const Prepared& b){return a.Count<b.Count;})->Count;
        if (static_cast<size_t>(maxCount) > indexCapacity_)
        {
            std::vector<uint32_t> indices(static_cast<size_t>(maxCount)*6);
            const uint32_t order[6]={0,1,2,2,1,3};
            for(UINT i=0;i<maxCount;++i) for(int k=0;k<6;++k) indices[static_cast<size_t>(i)*6+k]=i*4+order[k];
            D3D11_BUFFER_DESC d{}; d.ByteWidth=static_cast<UINT>(indices.size()*4); d.Usage=D3D11_USAGE_IMMUTABLE; d.BindFlags=D3D11_BIND_INDEX_BUFFER;
            D3D11_SUBRESOURCE_DATA data{}; data.pSysMem=indices.data();
            Ptr<ID3D11Buffer> buffer; Check(device->CreateBuffer(&d,&data,buffer.GetAddressOf()));
            indexBuffer_=buffer; indexCapacity_=maxCount;
        }
        D3D11_MAPPED_SUBRESOURCE mapped{}; Check(context->Map(vertexBuffer_.Get(),0,D3D11_MAP_WRITE_DISCARD,0,&mapped));
        std::memcpy(mapped.pData,vertices_.data(),bytes); context->Unmap(vertexBuffer_.Get(),0);
    }
    void Constants(ID3D11DeviceContext* context, const Draw& draw)
    {
        std::array<float,104> vs{}; std::array<float,92> ps{};
        std::memcpy(vs.data(),draw.View,64); std::memcpy(vs.data()+16,draw.Projection,64);
        vs[33]=vs[35]=1; ps[1]=ps[3]=1; vs[37]=ps[5]=1;
        DirectX::XMFLOAT4X4 view; std::memcpy(&view,draw.View,64);
        DirectX::XMFLOAT4X4 inverse; DirectX::XMStoreFloat4x4(&inverse,DirectX::XMMatrixInverse(nullptr,DirectX::XMLoadFloat4x4(&view)));
        vs[40]=ps[8]=inverse._41; vs[41]=ps[9]=inverse._42; vs[42]=ps[10]=inverse._43;
        ps[12]=1; ps[16]=draw.Projection[10]; ps[17]=draw.Projection[14]; ps[18]=draw.Projection[11]; ps[19]=draw.Projection[15];
        float uniforms[15][4]{}; const AecnLighting empty{}; const auto& lighting=draw.Lighting ? *draw.Lighting : empty;
        const auto& layer=*draw.Layer;
        const float* faces[]={lighting.PositiveX,lighting.NegativeX,lighting.PositiveY,lighting.NegativeY,lighting.PositiveZ,lighting.NegativeZ,lighting.SunDirection};
        for(int i=0;i<7;++i) std::copy_n(faces[i],4,uniforms[i+1]);
        std::copy_n(lighting.SunColour,3,uniforms[8]);
        uniforms[8][3]=-(draw.View[2]*lighting.SunDirection[0]+draw.View[6]*lighting.SunDirection[1]+draw.View[10]*lighting.SunDirection[2]);
        uniforms[9][0]=static_cast<float>(layer.LightingMode); uniforms[9][1]=layer.Emissive;
        uniforms[9][2]=layer.SmallParticles; uniforms[9][3]=layer.LargeParticles;
        uniforms[10][0]=layer.Directionality; uniforms[10][1]=layer.AverageSize;
        uniforms[10][2]=lighting.Parameters[0]; uniforms[10][3]=lighting.Parameters[1];
        uniforms[11][0]=layer.GradientIndex>=0 ? 1.0f : 0.0f; uniforms[11][1]=static_cast<float>(layer.GradientAlphaMapping); uniforms[11][2]=layer.GradientScale;
        std::memcpy(vs.data()+44,uniforms,sizeof(uniforms)); std::memcpy(ps.data()+32,uniforms,sizeof(uniforms));
        context->UpdateSubresource(vertexConstants_.Get(),0,nullptr,vs.data(),0,0);
        context->UpdateSubresource(pixelConstants_.Get(),0,nullptr,ps.data(),0,0);
        float nearPlane,farPlane; Planes(draw,nearPlane,farPlane);
        const float half[16]={nearPlane,farPlane,.0006000000284984708f,.25f,
            static_cast<float>(draw.Config->Premultiplied),draw.Config->CoveredColourMultiplier,draw.Config->EmissionMultiplier,.0010004499927163124f,
            static_cast<float>(width_/2),static_cast<float>(height_/2),static_cast<float>(width_),static_cast<float>(height_),
            draw.HalfResolution?2.0f:1.0f,0,1.0f/width_,1.0f/height_};
        context->UpdateSubresource(halfConstants_.Get(),0,nullptr,half,0,0);
        auto v=vertexConstants_.Get(),p=pixelConstants_.Get(),h=halfConstants_.Get();
        context->VSSetConstantBuffers(0,1,&v); context->PSSetConstantBuffers(0,1,&p);
        context->VSSetConstantBuffers(1,1,&h); context->PSSetConstantBuffers(1,1,&h);
    }
    void Fullscreen(ID3D11DeviceContext* context, ID3D11PixelShader* shader)
    {
        context->VSSetShader(fullscreen_.Get(),nullptr,0); context->PSSetShader(shader,nullptr,0);
        context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->Draw(3,0); ++drawCalls_;
    }
    void DepthPass(ID3D11DeviceContext* context, Surface& destination, ID3D11ShaderResourceView* source, UINT width, UINT height)
    {
        ID3D11ShaderResourceView* empty[8]{}; context->PSSetShaderResources(0,8,empty);
        context->OMSetRenderTargets(0,nullptr,destination.Depth.Get());
        D3D11_VIEWPORT viewport{0,0,static_cast<float>(width),static_cast<float>(height),0,1}; context->RSSetViewports(1,&viewport);
        context->OMSetDepthStencilState(writeDepth_.Get(),0); context->PSSetShaderResources(0,1,&source);
        Fullscreen(context,downsample_.Get());
    }
public:
    static bool CanRender(ID3D11DeviceContext* context, const std::vector<Draw>& draws, ID3D11ShaderResourceView* fullDepth = nullptr)
    {
        if (draws.empty()) return false;
        D3D11_TEXTURE2D_DESC target{}; D3D11_VIEWPORT viewport{}; bool hasDepth=false;
        if (!Target(context,target,viewport,hasDepth)) return false;
        Ptr<ID3D11Device> device; context->GetDevice(device.GetAddressOf()); if(device->GetFeatureLevel()<D3D_FEATURE_LEVEL_11_0)return false;
        UINT instances=0;Ptr<ID3D11VertexShader> currentVertex;Ptr<ID3D11PixelShader> currentPixel;
        context->VSGetShader(currentVertex.GetAddressOf(),nullptr,&instances);if(instances)return false;
        context->PSGetShader(currentPixel.GetAddressOf(),nullptr,&instances);if(instances)return false;
        const bool hasFull=std::any_of(draws.begin(),draws.end(),[](const Draw& draw){return !draw.HalfResolution;});
        if(hasFull && target.SampleDesc.Count==4)
            for(auto format:{DXGI_FORMAT_R32_FLOAT,DXGI_FORMAT_R32G32_FLOAT,DXGI_FORMAT_R32G32B32A32_FLOAT,DXGI_FORMAT_R16G16B16A16_FLOAT})
            {
                UINT levels=0,support=0;
                if(FAILED(device->CheckMultisampleQualityLevels(format,4,&levels)) || levels==0
                    || FAILED(device->CheckFormatSupport(format,&support)) || !(support&D3D11_FORMAT_SUPPORT_MULTISAMPLE_LOAD)
                    || (format!=DXGI_FORMAT_R16G16B16A16_FLOAT && !(support&D3D11_FORMAT_SUPPORT_MULTISAMPLE_RESOLVE)))return false;
            }
        Ptr<ID3D11GeometryShader> gs; Ptr<ID3D11HullShader> hs; Ptr<ID3D11DomainShader> ds;
        context->GSGetShader(gs.GetAddressOf(),nullptr,nullptr); context->HSGetShader(hs.GetAddressOf(),nullptr,nullptr); context->DSGetShader(ds.GetAddressOf(),nullptr,nullptr);
        if(gs || hs || ds)return false;
        D3D11_TEXTURE2D_DESC depth{};
        if(fullDepth && (!Describe(fullDepth,depth) || depth.Width!=target.Width || depth.Height!=target.Height))return false;
        if(hasDepth && !fullDepth)return false;
        size_t count=0;
        for(const auto& draw:draws)
        {
            if(!draw.Layer || !draw.Config || !draw.Particles || draw.Count<=0 || !draw.View || !draw.Projection || !draw.Textures[0])return false;
            const auto& layer=*draw.Layer; const auto& config=*draw.Config;
            if(layer.Kind!=0 || layer.Distortion!=0 || layer.ModelIndex>=0 || layer.OpaqueModel || layer.MeshMaterialIndex>=0
                || layer.GradientIndex>=0 || layer.TextureIndex<0 || layer.Blend!=1 || (config.Premultiplied!=0 && config.Premultiplied!=1)
                || layer.MaterialMode!=config.Premultiplied || layer.LightingMode<0 || layer.LightingMode>2
                || !std::isfinite(config.CoveredColourMultiplier) || config.CoveredColourMultiplier<0
                || !std::isfinite(config.EmissionMultiplier) || config.EmissionMultiplier<0)return false;
            if(!std::isfinite(layer.SoftDistance) || layer.SoftDistance<0 || layer.SoftDistance>65504
                || !std::isfinite(layer.DistortionSoftScaleStrength) || layer.DistortionSoftScaleStrength<0 || layer.DistortionSoftScaleStrength>1)return false;
            if(std::memcmp(draw.View,draws[0].View,64) || std::memcmp(draw.Projection,draws[0].Projection,64))return false;
            if(draw.Projection[15]!=0 || std::abs(draw.Projection[11]+1)>1e-6f)return false;
            float nearPlane,farPlane; Planes(draw,nearPlane,farPlane);
            if(!std::isfinite(nearPlane) || !std::isfinite(farPlane) || nearPlane<=0 || farPlane<=nearPlane)return false;
            if(draw.SoftDepth && (!Describe(draw.SoftDepth,depth) || depth.Width!=target.Width/4 || depth.Height!=target.Height/4))return false;
            for(int i=0;i<draw.Count;++i)
            {
                const auto& p=draw.Particles[i];
                if(p.Layer!=config.LayerIndex || !std::isfinite(p.SoftParticleScale) || !std::isfinite(p.Flipbook)
                    || !std::isfinite(p.AlphaCrush[0]) || !std::isfinite(p.AlphaCrush[1]) || p.AlphaCrush[1]<0 || p.AlphaCrush[1]>2)return false;
                for(float value:p.Transform)if(!std::isfinite(value))return false;
                for(float value:p.Colour)if(!std::isfinite(value))return false;
                for(float value:p.UV)if(!std::isfinite(value))return false;
                for(float value:p.NormalRotation)if(!std::isfinite(value))return false;
            }
            count+=static_cast<size_t>(draw.Count);
        }
        return count>0 && count<=std::numeric_limits<UINT>::max()/(4*sizeof(Vertex));
    }
    void Render(ID3D11Device* device, ID3D11DeviceContext* context, const std::vector<Draw>& draws, ID3D11ShaderResourceView* fullDepth = nullptr)
    {
        if(!CanRender(context,draws,fullDepth))throw std::runtime_error("Unsupported native full/half-resolution scene packet");
        D3D11_TEXTURE2D_DESC target{}; D3D11_VIEWPORT viewport{}; bool hasDepth=false; Target(context,target,viewport,hasDepth);
        const bool hasFull=std::any_of(draws.begin(),draws.end(),[](const Draw& draw){return !draw.HalfResolution;});
        const bool hasHalf=std::any_of(draws.begin(),draws.end(),[](const Draw& draw){return draw.HalfResolution;});
        const bool needsSoft=std::any_of(draws.begin(),draws.end(),[](const Draw& draw){return draw.Layer->NativeSoftDepth || draw.Layer->SoftDistance>0;});
        Prepare(device,target.Width,target.Height,hasFull?target.SampleDesc.Count:1);
        PrepareGeometry(device,context,draws);
        struct ClearInputs
        {
            std::vector<Prepared>& Entries;
            ~ClearInputs(){for(auto& entry:Entries){entry.Source=nullptr;entry.Runs.clear();entry.Count=0;}}
        } clearInputs{prepared_};
        Restore restore(device,context); drawCalls_=0;
        if(restore.Depth && restore.Depth.Get()!=originalDepth_.Get())
        {
            D3D11_DEPTH_STENCIL_VIEW_DESC view{}; restore.Depth->GetDesc(&view);
            view.Flags=D3D11_DSV_READ_ONLY_DEPTH;
            if(view.Format==DXGI_FORMAT_D24_UNORM_S8_UINT || view.Format==DXGI_FORMAT_D32_FLOAT_S8X24_UINT)view.Flags|=D3D11_DSV_READ_ONLY_STENCIL;
            Ptr<ID3D11Resource> resource; restore.Depth->GetResource(resource.GetAddressOf());
            Ptr<ID3D11DepthStencilView> readOnly; Check(device->CreateDepthStencilView(resource.Get(),&view,readOnly.GetAddressOf()));
            originalDepth_=restore.Depth; fullReadOnlyDepth_=readOnly;
        }
        ID3D11UnorderedAccessView* unordered[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
        context->OMSetRenderTargetsAndUnorderedAccessViews(0,nullptr,nullptr,0,ARRAYSIZE(unordered),unordered,nullptr);
        context->RSSetState(raster_.Get());
        std::array<ID3D11SamplerState*,8> samplers{}; samplers.fill(sampler_.Get()); context->PSSetSamplers(0,8,samplers.data());
        const float zero[4]{};
        for(auto* surface:{&b0_,&b3456_,&b12_,&colour_,&activeFull_->B0,&activeFull_->B3456,&activeFull_->B12,&activeFull_->Colour})
            context->ClearRenderTargetView(surface->Target.Get(),zero);
        context->ClearDepthStencilView(halfDepth_.Depth.Get(),D3D11_CLEAR_DEPTH|D3D11_CLEAR_STENCIL,1,0);
        context->ClearDepthStencilView(quarterDepth_.Depth.Get(),D3D11_CLEAR_DEPTH|D3D11_CLEAR_STENCIL,1,0);
        if(fullDepth && (hasHalf || needsSoft))
        {
            DepthPass(context,halfDepth_,fullDepth,width_/2,height_/2);
            if(needsSoft)DepthPass(context,quarterDepth_,halfDepth_.View.Get(),width_/4,height_/4);
        }
        const D3D11_VIEWPORT halfViewport{0,0,std::floor(viewport.Width/2),std::floor(viewport.Height/2),0,1};
        ID3D11ShaderResourceView* empty[8]{};
        for(int phase=0;phase<2;++phase)
        {
            for(size_t group=0;group<preparedCount_;++group)
            {
                const auto& prepared=prepared_[group]; const auto& draw=*prepared.Source;
                const bool half=draw.HalfResolution, multisample=!half && activeFull_->Samples>1;
                context->PSSetShaderResources(0,8,empty);
                auto& sourceB0=half?b0_:activeFull_->B0;
                auto& sourceB3456=half?b3456_:activeFull_->B3456;
                auto& sourceB12=half?b12_:activeFull_->B12;
                ID3D11RenderTargetView* targets[]={sourceB0.Target.Get(),sourceB3456.Target.Get(),sourceB12.Target.Get()};
                if(phase==1)targets[0]=half?colour_.Target.Get():activeFull_->Colour.Target.Get();
                auto depth=half?halfDepth_.ReadOnlyDepth.Get():(restore.Depth?fullReadOnlyDepth_.Get():nullptr);
                context->OMSetRenderTargets(phase==0?3:1,targets,depth);
                context->RSSetViewports(1,half?&halfViewport:&viewport);
                context->OMSetBlendState(additive_.Get(),nullptr,0xffffffff);
                context->OMSetDepthStencilState(draw.Layer->ZTest?readDepth_.Get():noDepth_.Get(),0);
                context->VSSetShader(vertex_.Get(),nullptr,0);
                context->PSSetShader(phase==0?(multisample?moments4_.Get():moments_.Get()):(multisample?colour4_.Get():colourShader_.Get()),nullptr,0);
                context->IASetInputLayout(layout_.Get()); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
                context->IASetIndexBuffer(indexBuffer_.Get(),DXGI_FORMAT_R32_UINT,0);
                Constants(context,draw);
                std::array<ID3D11ShaderResourceView*,5> textures={draw.Textures[0],draw.Textures[1]?draw.Textures[1]:draw.Textures[0],
                    draw.Textures[2]?draw.Textures[2]:draw.Textures[0],nullptr,draw.SoftDepth?draw.SoftDepth:quarterDepth_.View.Get()};
                context->PSSetShaderResources(0,5,textures.data());
                if(phase==1)
                {
                    const bool resolved=half && activeFull_->Samples>1;
                    ID3D11ShaderResourceView* moments[]={resolved?activeFull_->ResolvedB0.View.Get():activeFull_->B0.View.Get(),
                        resolved?activeFull_->ResolvedB3456.View.Get():activeFull_->B3456.View.Get(),
                        resolved?activeFull_->ResolvedB12.View.Get():activeFull_->B12.View.Get()};
                    context->PSSetShaderResources(5,3,moments);
                }
                UINT stride=sizeof(Vertex),offset=static_cast<UINT>(prepared.FirstVertex*sizeof(Vertex));auto buffer=vertexBuffer_.Get();
                context->IASetVertexBuffers(0,1,&buffer,&stride,&offset);
                context->DrawIndexed(prepared.Count*6,0,0);++drawCalls_;
            }
            if(phase==0)
            {
                // All seven HALF moments join the FULL pool before either colour resolution reads them.
                context->PSSetShaderResources(0,8,empty);
                if(hasHalf)
                {
                    ID3D11RenderTargetView* targets[]={activeFull_->B0.Target.Get(),activeFull_->B3456.Target.Get(),activeFull_->B12.Target.Get()};
                    context->OMSetRenderTargets(3,targets,nullptr);context->RSSetViewports(1,&viewport);
                    context->OMSetDepthStencilState(noDepth_.Get(),0);context->OMSetBlendState(additive_.Get(),nullptr,0xffffffff);
                    ID3D11ShaderResourceView* sources[]={b0_.View.Get(),b3456_.View.Get(),b12_.View.Get()};context->PSSetShaderResources(0,3,sources);
                    Fullscreen(context,merge_.Get());
                }
                context->PSSetShaderResources(0,8,empty);context->OMSetRenderTargets(0,nullptr,nullptr);
                if(activeFull_->Samples>1)
                {
                    // AECN MSAA compatibility: half colour queries the sample-average seven-moment field.
                    context->ResolveSubresource(activeFull_->ResolvedB0.Texture.Get(),0,activeFull_->B0.Texture.Get(),0,DXGI_FORMAT_R32_FLOAT);
                    context->ResolveSubresource(activeFull_->ResolvedB3456.Texture.Get(),0,activeFull_->B3456.Texture.Get(),0,DXGI_FORMAT_R32G32B32A32_FLOAT);
                    context->ResolveSubresource(activeFull_->ResolvedB12.Texture.Get(),0,activeFull_->B12.Texture.Get(),0,DXGI_FORMAT_R32G32_FLOAT);
                }
            }
        }
        context->PSSetShaderResources(0,8,empty);
        ID3D11RenderTargetView* destination=restore.Targets[0].Get();context->OMSetRenderTargets(1,&destination,nullptr);context->RSSetViewports(1,&viewport);
        context->OMSetDepthStencilState(noDepth_.Get(),0);context->OMSetBlendState(composite_.Get(),nullptr,0xffffffff);
        Constants(context,draws.front());
        ID3D11ShaderResourceView* inputs[]={activeFull_->B0.View.Get(),activeFull_->Colour.View.Get(),colour_.View.Get()};context->PSSetShaderResources(0,3,inputs);
        Fullscreen(context,activeFull_->Samples>1?compose4_.Get():compose_.Get());
    }
    unsigned DrawCallCount() const { return drawCalls_; }
};
