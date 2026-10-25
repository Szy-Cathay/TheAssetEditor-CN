#pragma once

// Consecutive distortion layers share native weighted vectors and transmittance before resolving once.
class WeightedDistortionTarget
{
    struct Surface
    {
        ComPtr<ID3D11Texture2D> texture, resolved;
        ComPtr<ID3D11RenderTargetView> target;
        ComPtr<ID3D11ShaderResourceView> view;
    } vectors_, transmittance_;
    ComPtr<ID3D11Texture2D> background_;
    ComPtr<ID3D11ShaderResourceView> backgroundView_;
    ComPtr<ID3D11VertexShader> vertex_;
    ComPtr<ID3D11PixelShader> resolve_;
    ComPtr<ID3D11SamplerState> linearClamp_;
    ComPtr<ID3D11BlendState> replaceRgb_;
    ComPtr<ID3D11DepthStencilState> noDepth_;
    ComPtr<ID3D11RasterizerState> raster_;
    D3D11_TEXTURE2D_DESC description_{};
    DXGI_FORMAT viewFormat_ = DXGI_FORMAT_UNKNOWN;

    static void Check(HRESULT result)
    {
        if (FAILED(result)) throw std::runtime_error("Cannot prepare weighted VFX distortion targets");
    }

    void Prepare(ID3D11Device* device, const D3D11_TEXTURE2D_DESC& source, DXGI_FORMAT viewFormat)
    {
        if (!vertex_)
        {
            const char* vs = "float4 main(uint i:SV_VertexID):SV_Position { float2 p=float2((i<<1)&2,i&2); return float4(p*float2(2,-2)+float2(-1,1),0,1); }";
            const char* ps = R"(
                Texture2D<float4> vectors : register(t0), transmittance : register(t1), background : register(t2);
                SamplerState linearClamp : register(s0);
                float4 main(float4 position : SV_Position) : SV_Target
                {
                    uint width, height; vectors.GetDimensions(width, height);
                    float2 inverseSize = 1.0 / float2(width, height);
                    float2 uv = position.xy * inverseSize;
                    // Captured native resolve samples the distortion buffer half a texel before the background.
                    float2 bufferUV = uv - 0.5 * inverseSize;
                    float3 accumulated = vectors.SampleLevel(linearClamp, bufferUV, 0).rgb;
                    // Native mode 1 squares 1-T even when floating transmittance leaves [0,1].
                    float visibility = 1 - transmittance.SampleLevel(linearClamp, bufferUV, 0).r;
                    if (visibility == 0 || accumulated.z <= 0) discard;
                    float2 offset = 0.02 * accumulated.xy / (accumulated.z < 0.001 ? 1 : accumulated.z)
                        * visibility * visibility;
                    if (all(offset == 0)) discard;
                    return float4(background.SampleLevel(linearClamp, uv + offset, 0).rgb, 0);
                }
            )";
            auto compile = [](const char* sourceCode, const char* profile)
            {
                ComPtr<ID3DBlob> code, errors;
                if (FAILED(D3DCompile(sourceCode, std::strlen(sourceCode), "AECN weighted distortion resolve", nullptr, nullptr,
                    "main", profile, 0, 0, code.GetAddressOf(), errors.GetAddressOf())))
                    throw std::runtime_error(errors ? static_cast<const char*>(errors->GetBufferPointer()) : "Cannot compile VFX distortion resolve");
                return code;
            };
            const auto vertexCode = compile(vs, "vs_5_0"), pixelCode = compile(ps, "ps_5_0");
            Check(device->CreateVertexShader(vertexCode->GetBufferPointer(), vertexCode->GetBufferSize(), nullptr, vertex_.GetAddressOf()));
            Check(device->CreatePixelShader(pixelCode->GetBufferPointer(), pixelCode->GetBufferSize(), nullptr, resolve_.GetAddressOf()));
            D3D11_SAMPLER_DESC sampler{}; sampler.Filter = D3D11_FILTER_MIN_MAG_LINEAR_MIP_POINT;
            sampler.AddressU = sampler.AddressV = sampler.AddressW = D3D11_TEXTURE_ADDRESS_CLAMP; sampler.MaxLOD = D3D11_FLOAT32_MAX;
            Check(device->CreateSamplerState(&sampler, linearClamp_.GetAddressOf()));
            D3D11_BLEND_DESC blend{};
            blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_RED | D3D11_COLOR_WRITE_ENABLE_GREEN | D3D11_COLOR_WRITE_ENABLE_BLUE;
            Check(device->CreateBlendState(&blend, replaceRgb_.GetAddressOf()));
            D3D11_DEPTH_STENCIL_DESC depth{}; Check(device->CreateDepthStencilState(&depth, noDepth_.GetAddressOf()));
            D3D11_RASTERIZER_DESC raster{}; raster.FillMode = D3D11_FILL_SOLID; raster.CullMode = D3D11_CULL_NONE;
            raster.DepthClipEnable = true; raster.MultisampleEnable = true;
            Check(device->CreateRasterizerState(&raster, raster_.GetAddressOf()));
        }
        if (background_ && vectors_.view && transmittance_.view && description_.Width == source.Width && description_.Height == source.Height && description_.Format == source.Format
            && description_.SampleDesc.Count == source.SampleDesc.Count && description_.SampleDesc.Quality == source.SampleDesc.Quality && viewFormat_ == viewFormat) return;
        vectors_ = {}; transmittance_ = {}; backgroundView_.Reset(); background_.Reset();
        description_ = source; viewFormat_ = viewFormat;
        auto background = source; background.SampleDesc = {1, 0}; background.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        background.Usage = D3D11_USAGE_DEFAULT; background.CPUAccessFlags = 0; background.MiscFlags = 0;
        Check(device->CreateTexture2D(&background, nullptr, background_.GetAddressOf()));
        D3D11_SHADER_RESOURCE_VIEW_DESC view{}; view.Format = viewFormat; view.ViewDimension = D3D11_SRV_DIMENSION_TEXTURE2D; view.Texture2D.MipLevels = 1;
        Check(device->CreateShaderResourceView(background_.Get(), &view, backgroundView_.GetAddressOf()));
        auto create = [&](Surface& surface)
        {
            auto description = source; description.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
            description.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            description.Usage = D3D11_USAGE_DEFAULT; description.CPUAccessFlags = 0; description.MiscFlags = 0;
            Check(device->CreateTexture2D(&description, nullptr, surface.texture.GetAddressOf()));
            Check(device->CreateRenderTargetView(surface.texture.Get(), nullptr, surface.target.GetAddressOf()));
            if (description.SampleDesc.Count > 1)
            {
                description.SampleDesc = {1, 0}; description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
                Check(device->CreateTexture2D(&description, nullptr, surface.resolved.GetAddressOf()));
            }
            Check(device->CreateShaderResourceView(surface.resolved ? surface.resolved.Get() : surface.texture.Get(), nullptr, surface.view.GetAddressOf()));
        };
        create(vectors_); create(transmittance_);
    }

    class PipelineState
    {
        ID3D11DeviceContext* context_;
        ComPtr<ID3D11VertexShader> vertex_;
        ComPtr<ID3D11PixelShader> pixel_;
        ComPtr<ID3D11GeometryShader> geometry_;
        ComPtr<ID3D11HullShader> hull_;
        ComPtr<ID3D11DomainShader> domain_;
        std::array<std::array<ID3D11ClassInstance*, 256>, 5> classes_{};
        std::array<UINT, 5> counts_{{256, 256, 256, 256, 256}};
        ComPtr<ID3D11InputLayout> layout_;
        D3D11_PRIMITIVE_TOPOLOGY topology_;
        ComPtr<ID3D11BlendState> blend_;
        FLOAT factors_[4]; UINT mask_;
        ComPtr<ID3D11DepthStencilState> depth_; UINT stencil_;
        ComPtr<ID3D11RasterizerState> raster_;
        std::array<ComPtr<ID3D11ShaderResourceView>, 3> textures_;
        ComPtr<ID3D11SamplerState> sampler_;
    public:
        explicit PipelineState(ID3D11DeviceContext* context) : context_(context)
        {
            context->VSGetShader(vertex_.GetAddressOf(), classes_[0].data(), &counts_[0]);
            context->PSGetShader(pixel_.GetAddressOf(), classes_[1].data(), &counts_[1]);
            context->GSGetShader(geometry_.GetAddressOf(), classes_[2].data(), &counts_[2]);
            context->HSGetShader(hull_.GetAddressOf(), classes_[3].data(), &counts_[3]);
            context->DSGetShader(domain_.GetAddressOf(), classes_[4].data(), &counts_[4]);
            context->IAGetInputLayout(layout_.GetAddressOf()); context->IAGetPrimitiveTopology(&topology_);
            context->OMGetBlendState(blend_.GetAddressOf(), factors_, &mask_);
            context->OMGetDepthStencilState(depth_.GetAddressOf(), &stencil_); context->RSGetState(raster_.GetAddressOf());
            for (UINT index = 0; index < textures_.size(); ++index) context->PSGetShaderResources(index, 1, textures_[index].GetAddressOf());
            context->PSGetSamplers(0, 1, sampler_.GetAddressOf());
        }
        ~PipelineState()
        {
            ID3D11ShaderResourceView* empty[3]{}; context_->PSSetShaderResources(0, 3, empty);
            context_->VSSetShader(vertex_.Get(), classes_[0].data(), counts_[0]); context_->PSSetShader(pixel_.Get(), classes_[1].data(), counts_[1]);
            context_->GSSetShader(geometry_.Get(), classes_[2].data(), counts_[2]); context_->HSSetShader(hull_.Get(), classes_[3].data(), counts_[3]);
            context_->DSSetShader(domain_.Get(), classes_[4].data(), counts_[4]);
            for (size_t stage = 0; stage < classes_.size(); ++stage)
                for (UINT index = 0; index < counts_[stage]; ++index) if (classes_[stage][index]) classes_[stage][index]->Release();
            context_->IASetInputLayout(layout_.Get()); context_->IASetPrimitiveTopology(topology_);
            context_->OMSetBlendState(blend_.Get(), factors_, mask_); context_->OMSetDepthStencilState(depth_.Get(), stencil_); context_->RSSetState(raster_.Get());
            ID3D11ShaderResourceView* previous[]{textures_[0].Get(), textures_[1].Get(), textures_[2].Get()}; context_->PSSetShaderResources(0, 3, previous);
            auto sampler = sampler_.Get(); context_->PSSetSamplers(0, 1, &sampler);
        }
    };

public:
    class Frame
    {
        WeightedDistortionTarget& owner_;
        ID3D11DeviceContext* context_;
        std::array<ComPtr<ID3D11RenderTargetView>, D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT> targets_;
        std::array<ComPtr<ID3D11UnorderedAccessView>, D3D11_PS_CS_UAV_REGISTER_COUNT> unordered_;
        ComPtr<ID3D11DepthStencilView> depth_;
        UINT targetCount_ = 1;
        bool active_ = false;
        void RestoreTargets()
        {
            ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
            for (size_t index = 0; index < targets_.size(); ++index) targets[index] = targets_[index].Get();
            ID3D11UnorderedAccessView* unordered[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
            UINT counters[D3D11_PS_CS_UAV_REGISTER_COUNT]; std::fill_n(counters, D3D11_PS_CS_UAV_REGISTER_COUNT, static_cast<UINT>(-1));
            for (size_t index = 0; index < unordered_.size(); ++index) unordered[index] = unordered_[index].Get();
            if (targetCount_ == D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT)
                context_->OMSetRenderTargets(targetCount_, targets, depth_.Get());
            else
                context_->OMSetRenderTargetsAndUnorderedAccessViews(targetCount_, targets, depth_.Get(), targetCount_,
                    D3D11_PS_CS_UAV_REGISTER_COUNT - targetCount_, unordered + targetCount_, counters + targetCount_);
        }
    public:
        Frame(WeightedDistortionTarget& owner, ID3D11Device* device, ID3D11DeviceContext* context) : owner_(owner), context_(context)
        {
            ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
            context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depth_.GetAddressOf());
            for (size_t index = 0; index < targets_.size(); ++index) targets_[index].Attach(targets[index]);
            for (UINT index = 0; index < targets_.size(); ++index) if (targets_[index]) targetCount_ = index + 1;
            ID3D11UnorderedAccessView* unordered[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
            context->OMGetRenderTargetsAndUnorderedAccessViews(0, nullptr, nullptr, 0, D3D11_PS_CS_UAV_REGISTER_COUNT, unordered);
            for (size_t index = 0; index < unordered_.size(); ++index) unordered_[index].Attach(unordered[index]);
            if (!targets_[0]) throw std::runtime_error("Weighted distortion requires a scene render target");
            D3D11_RENDER_TARGET_VIEW_DESC view{}; targets_[0]->GetDesc(&view);
            ComPtr<ID3D11Resource> resource; targets_[0]->GetResource(resource.GetAddressOf());
            ComPtr<ID3D11Texture2D> source; Check(resource.As(&source));
            D3D11_TEXTURE2D_DESC description{}; source->GetDesc(&description);
            if (description.ArraySize != 1 || description.MipLevels != 1) throw std::runtime_error("Unsupported weighted distortion scene layout");
            owner.Prepare(device, description, view.Format);
            if (description.SampleDesc.Count > 1) context->ResolveSubresource(owner.background_.Get(), 0, source.Get(), 0, view.Format);
            else context->CopyResource(owner.background_.Get(), source.Get());
            const float zero[4]{}, one[4]{1, 1, 1, 1};
            context->ClearRenderTargetView(owner.vectors_.target.Get(), zero); context->ClearRenderTargetView(owner.transmittance_.target.Get(), one);
            auto target = owner.vectors_.target.Get();
            ID3D11UnorderedAccessView* empty[D3D11_PS_CS_UAV_REGISTER_COUNT]{};
            context->OMSetRenderTargetsAndUnorderedAccessViews(1, &target, depth_.Get(), 1,
                D3D11_PS_CS_UAV_REGISTER_COUNT - 1, empty + 1, nullptr);
            active_ = true;
        }
        Frame(const Frame&) = delete;
        ~Frame() { if (active_) RestoreTargets(); }
        void BindTransmittance()
        {
            auto target = owner_.transmittance_.target.Get(); context_->OMSetRenderTargets(1, &target, depth_.Get());
        }
        void Finish()
        {
            context_->OMSetRenderTargets(0, nullptr, nullptr);
            if (owner_.description_.SampleDesc.Count > 1)
                for (auto surface : {&owner_.vectors_, &owner_.transmittance_})
                    context_->ResolveSubresource(surface->resolved.Get(), 0, surface->texture.Get(), 0, DXGI_FORMAT_R16G16B16A16_FLOAT);
            PipelineState saved(context_);
            auto target = targets_[0].Get(); context_->OMSetRenderTargets(1, &target, nullptr);
            context_->OMSetBlendState(owner_.replaceRgb_.Get(), nullptr, 0xFFFFFFFF); context_->OMSetDepthStencilState(owner_.noDepth_.Get(), 0);
            context_->RSSetState(owner_.raster_.Get()); context_->IASetInputLayout(nullptr); context_->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
            context_->VSSetShader(owner_.vertex_.Get(), nullptr, 0); context_->PSSetShader(owner_.resolve_.Get(), nullptr, 0);
            context_->GSSetShader(nullptr, nullptr, 0); context_->HSSetShader(nullptr, nullptr, 0); context_->DSSetShader(nullptr, nullptr, 0);
            auto sampler = owner_.linearClamp_.Get(); context_->PSSetSamplers(0, 1, &sampler);
            ID3D11ShaderResourceView* inputs[]{owner_.vectors_.view.Get(), owner_.transmittance_.view.Get(), owner_.backgroundView_.Get()};
            context_->PSSetShaderResources(0, 3, inputs); context_->Draw(3, 0);
            ID3D11ShaderResourceView* empty[3]{}; context_->PSSetShaderResources(0, 3, empty);
            RestoreTargets(); active_ = false;
        }
    };
};
