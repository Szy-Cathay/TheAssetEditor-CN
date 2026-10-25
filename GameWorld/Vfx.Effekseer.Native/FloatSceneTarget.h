#pragma once

// UNORM targets clamp shader RGB before SRC_ALPHA blending. Keep thin HDR particle colour until composition finishes.
class FloatSceneTarget
{
    ComPtr<ID3D11Texture2D> sourceCopy_, colour_;
    ComPtr<ID3D11ShaderResourceView> sourceView_, colourView_;
    ComPtr<ID3D11RenderTargetView> colourTarget_;
    ComPtr<ID3D11VertexShader> vertex_;
    ComPtr<ID3D11PixelShader> single_, multisample_;
    ComPtr<ID3D11PixelShader> extract_, extractMultisample_, downsample_, blur_, upsample_;
    ComPtr<ID3D11SamplerState> linearClamp_;
    ComPtr<ID3D11Buffer> displayBuffer_;
    ComPtr<ID3D11BlendState> replace_;
    ComPtr<ID3D11DepthStencilState> noDepth_;
    ComPtr<ID3D11RasterizerState> raster_;
    D3D11_TEXTURE2D_DESC description_{};
    DXGI_FORMAT viewFormat_ = DXGI_FORMAT_UNKNOWN;
    std::array<float, 4> display_{};
    std::array<float, 4> bloom_{};
    struct Surface
    {
        ComPtr<ID3D11Texture2D> texture;
        ComPtr<ID3D11ShaderResourceView> view;
        ComPtr<ID3D11RenderTargetView> target;
        UINT width = 0, height = 0;
    };
    Surface bright_;
    std::array<Surface, 5> bloomLevels_, bloomScratch_;

    static void Check(HRESULT result)
    {
        if (FAILED(result)) throw std::runtime_error("Cannot preserve VFX colour in the floating scene target");
    }

    void Prepare(ID3D11Device* device, const D3D11_TEXTURE2D_DESC& source, DXGI_FORMAT viewFormat)
    {
        if (!vertex_)
        {
            const char* vs = "float4 main(uint i:SV_VertexID):SV_Position { float2 p=float2((i<<1)&2,i&2); return float4(p*float2(2,-2)+float2(-1,1),0,1); }";
            // Shipped hdr_to_screen transfer and separable default LUT, applied after particle blending.
            const std::string transfer = R"(
                cbuffer Display : register(b0) { float4 display; float4 mode; float4 bloom; }
                Texture2D<float4> halo : register(t2);
                SamplerState linearClamp : register(s0);
                static const float lut[32] = { 0,8,16,24,33,41,49,57,65,73,82,91,99,107,115,123,
                    132,140,148,156,164,173,182,190,198,206,214,222,231,239,247,255 };
                float3 ToSrgb(float3 c) { return lerp(12.92*c, 1.055*pow(max(c,0),1/2.4)-.055, step(.0031308,c)); }
                float3 FromSrgb(float3 c) { return lerp(c/12.92, pow((c+.055)/1.055,2.4), step(.04045,c)); }
                float WhiteReciprocal() { return display.z == 0 ? 0 : 1 / (1 / display.z - .999); }
                float Lookup(float c)
                {
                    float p = clamp(c*32-.5,0,31); uint i = (uint)p;
                    return lerp(lut[i], lut[min(i+1,31)], p-i) / 255;
                }
                float InverseLookup(float c)
                {
                    if (c <= 0 || c >= 1) return saturate(c);
                    float value = c*255;
                    [unroll] for (uint i=0;i<31;i++)
                        if (value <= lut[i+1]) return (i+.5+(value-lut[i])/(lut[i+1]-lut[i]))/32;
                    return 1;
                }
                float3 Encode(float3 rgb)
                {
                    rgb = max(rgb,0);
                    float exposure = display.y*display.y;
                    float x = dot(rgb,float3(.2126,.7152,.0722))*exposure;
                    float3 encoded = pow(saturate(rgb*exposure*(1+x*WhiteReciprocal())/(1+x)),1/2.2);
                    if (display.w != 0) encoded = (1 + display.w) * (encoded - .5) + .5;
                    return float3(Lookup(encoded.r),Lookup(encoded.g),Lookup(encoded.b));
                }
                float3 Decode(float3 encoded)
                {
                    float3 corrected = float3(InverseLookup(encoded.r),InverseLookup(encoded.g),InverseLookup(encoded.b));
                    if (display.w != 0) corrected = max((corrected - .5) / (1 + display.w) + .5, 0);
                    float3 c = pow(corrected,2.2);
                    float y = dot(c,float3(.2126,.7152,.0722)), w = WhiteReciprocal();
                    float x = w > 0 ? 2*y/max(sqrt((1-y)*(1-y)+4*w*y)+1-y,1e-8) : y/max(1-y,1e-6);
                    return c*(x/max(y,1e-8))/(display.y*display.y);
                }
                float3 BackgroundRadiance(float4 background)
                {
                    return Decode(mode.y > .5 ? ToSrgb(background.rgb) : background.rgb);
                }
                float3 Bright(float3 colour)
                {
                    colour = max(colour,0);
                    return colour * saturate(dot(colour,float3(.2126,.7152,.0722))-max(bloom.x,0)) * bloom.y;
                }
                float4 Extract(float4 value, float4 background)
                {
                    // Only the VFX increment blooms; the existing model/grid display is not a new light source.
                    float3 before = f16tof32(f32tof16(BackgroundRadiance(background)));
                    return float4(max(Bright(value.rgb)-Bright(before),0),0);
                }
                float4 Transfer(float4 value, float4 background, float2 uv)
                {
                    if (display.x < .5) return value;
                    float3 linearBackground = BackgroundRadiance(background);
                    if (mode.x < .5) return float4(linearBackground,background.a);
                    float3 glow = bloom.y > 0 ? halo.SampleLevel(linearClamp,uv,0).rgb : 0;
                    // Preserve untouched display pixels exactly, including prior PBR tonemapping and alpha.
                    if (all(glow == 0) && all(value.rgb == f16tof32(f32tof16(linearBackground))) && value.a == f16tof32(f32tof16(background.a)))
                        return background;
                    float3 encoded = Encode(value.rgb + glow);
                    return float4(mode.y > .5 ? FromSrgb(encoded) : encoded,value.a);
                }
            )";
            const auto ps = transfer + "Texture2D<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position):SV_Target { int3 q=int3(int2(p.xy),0); return Transfer(source.Load(q),background.Load(q),p.xy*bloom.zw); }";
            const auto ms = transfer + "Texture2DMS<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position,uint sample:SV_SampleIndex):SV_Target { int2 q=int2(p.xy); return Transfer(source.Load(q,sample),background.Load(q,sample),p.xy*bloom.zw); }";
            const auto extract = transfer + "Texture2D<float4> source:register(t0), background:register(t1); float4 main(float4 p:SV_Position):SV_Target { int3 q=int3(int2(p.xy),0); return Extract(source.Load(q),background.Load(q)); }";
            const auto extractMS = transfer + R"(
                Texture2DMS<float4> source:register(t0), background:register(t1);
                float4 main(float4 p:SV_Position):SV_Target {
                    uint width,height,samples; source.GetDimensions(width,height,samples);
                    float4 result=0;
                    for(uint i=0;i<samples;i++) result += Extract(source.Load(int2(p.xy),i),background.Load(int2(p.xy),i));
                    return result/samples;
                })";
            const std::string filter = R"(
                cbuffer Display : register(b0) { float4 display; float4 mode; float4 bloom; }
                Texture2D<float4> inputA:register(t0), inputB:register(t1);
                SamplerState linearClamp:register(s0);
            )";
            const auto downsample = filter + "float4 main(float4 p:SV_Position):SV_Target { return inputA.SampleLevel(linearClamp,p.xy*bloom.zw,0); }";
            const auto upsample = filter + "float4 main(float4 p:SV_Position):SV_Target { float2 uv=p.xy*bloom.zw; return inputA.SampleLevel(linearClamp,uv,0)+inputB.SampleLevel(linearClamp,uv,0); }";
            const auto blur = filter + R"(
                static const float weights[11]={.000003,.000229,.005977,.060598,.241730,.382925,.241730,.060598,.005977,.000229,.000003};
                float4 main(float4 p:SV_Position):SV_Target {
                    float4 result=0;
                    [unroll] for(int i=0;i<11;i++) result += inputA.SampleLevel(linearClamp,p.xy*bloom.zw+(i-5)*mode.zw,0)*weights[i];
                    return result;
                })";
            auto compile = [](const char* sourceCode, const char* profile)
            {
                ComPtr<ID3DBlob> code, errors;
                Check(D3DCompile(sourceCode, std::strlen(sourceCode), "AECN VFX colour transfer", nullptr, nullptr, "main", profile, 0, 0, code.GetAddressOf(), errors.GetAddressOf()));
                return code;
            };
            static auto vertexCode = compile(vs, "vs_5_0"), singleCode = compile(ps.c_str(), "ps_5_0"), multisampleCode = compile(ms.c_str(), "ps_5_0");
            Check(device->CreateVertexShader(vertexCode->GetBufferPointer(), vertexCode->GetBufferSize(), nullptr, vertex_.GetAddressOf()));
            Check(device->CreatePixelShader(singleCode->GetBufferPointer(), singleCode->GetBufferSize(), nullptr, single_.GetAddressOf()));
            Check(device->CreatePixelShader(multisampleCode->GetBufferPointer(), multisampleCode->GetBufferSize(), nullptr, multisample_.GetAddressOf()));
            static auto extractCode=compile(extract.c_str(),"ps_5_0"), extractMSCode=compile(extractMS.c_str(),"ps_5_0"),
                downsampleCode=compile(downsample.c_str(),"ps_5_0"), blurCode=compile(blur.c_str(),"ps_5_0"), upsampleCode=compile(upsample.c_str(),"ps_5_0");
            auto pixel = [&](const ComPtr<ID3DBlob>& code, ComPtr<ID3D11PixelShader>& shader) {
                Check(device->CreatePixelShader(code->GetBufferPointer(),code->GetBufferSize(),nullptr,shader.GetAddressOf()));
            };
            pixel(extractCode,extract_); pixel(extractMSCode,extractMultisample_); pixel(downsampleCode,downsample_); pixel(blurCode,blur_); pixel(upsampleCode,upsample_);
            D3D11_SAMPLER_DESC sampler{}; sampler.Filter=D3D11_FILTER_MIN_MAG_LINEAR_MIP_POINT;
            sampler.AddressU=sampler.AddressV=sampler.AddressW=D3D11_TEXTURE_ADDRESS_CLAMP; sampler.MaxLOD=D3D11_FLOAT32_MAX;
            Check(device->CreateSamplerState(&sampler,linearClamp_.GetAddressOf()));
            D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth = 48; buffer.Usage = D3D11_USAGE_DEFAULT; buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
            Check(device->CreateBuffer(&buffer, nullptr, displayBuffer_.GetAddressOf()));
            D3D11_BLEND_DESC blend{}; blend.RenderTarget[0].RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
            Check(device->CreateBlendState(&blend, replace_.GetAddressOf()));
            D3D11_DEPTH_STENCIL_DESC depth{}; Check(device->CreateDepthStencilState(&depth, noDepth_.GetAddressOf()));
            D3D11_RASTERIZER_DESC raster{}; raster.FillMode = D3D11_FILL_SOLID; raster.CullMode = D3D11_CULL_NONE; raster.DepthClipEnable = true; raster.MultisampleEnable = true;
            Check(device->CreateRasterizerState(&raster, raster_.GetAddressOf()));
        }
        if (colour_ && description_.Width == source.Width && description_.Height == source.Height && description_.Format == source.Format
            && description_.SampleDesc.Count == source.SampleDesc.Count && description_.SampleDesc.Quality == source.SampleDesc.Quality && viewFormat_ == viewFormat) return;
        sourceView_.Reset(); sourceCopy_.Reset(); colourTarget_.Reset(); colourView_.Reset(); colour_.Reset();
        bright_ = {}; bloomLevels_ = {}; bloomScratch_ = {};
        description_ = source; viewFormat_ = viewFormat;
        auto copy = source; copy.BindFlags = D3D11_BIND_SHADER_RESOURCE; copy.Usage = D3D11_USAGE_DEFAULT; copy.CPUAccessFlags = 0; copy.MiscFlags = 0;
        Check(device->CreateTexture2D(&copy, nullptr, sourceCopy_.GetAddressOf()));
        D3D11_SHADER_RESOURCE_VIEW_DESC view{}; view.Format = viewFormat;
        view.ViewDimension = source.SampleDesc.Count > 1 ? D3D11_SRV_DIMENSION_TEXTURE2DMS : D3D11_SRV_DIMENSION_TEXTURE2D;
        if (source.SampleDesc.Count == 1) view.Texture2D.MipLevels = 1;
        Check(device->CreateShaderResourceView(sourceCopy_.Get(), &view, sourceView_.GetAddressOf()));
        copy.Format = DXGI_FORMAT_R16G16B16A16_FLOAT; copy.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
        Check(device->CreateTexture2D(&copy, nullptr, colour_.GetAddressOf()));
        Check(device->CreateRenderTargetView(colour_.Get(), nullptr, colourTarget_.GetAddressOf()));
        Check(device->CreateShaderResourceView(colour_.Get(), nullptr, colourView_.GetAddressOf()));
    }

    void Constants(ID3D11DeviceContext* context, UINT width, UINT height, bool finish, float dx = 0, float dy = 0)
    {
        float values[12]{display_[0],display_[1],display_[2],display_[3],finish ? 1.0f : 0.0f,
            viewFormat_ == DXGI_FORMAT_R8G8B8A8_UNORM_SRGB || viewFormat_ == DXGI_FORMAT_B8G8R8A8_UNORM_SRGB ? 1.0f : 0.0f,
            dx,dy,bloom_[0],bloom_[1],1.0f/width,1.0f/height};
        context->UpdateSubresource(displayBuffer_.Get(),0,nullptr,values,0,0);
        auto buffer=displayBuffer_.Get(); context->PSSetConstantBuffers(0,1,&buffer);
    }

    ID3D11ShaderResourceView* Bloom(ID3D11DeviceContext* context)
    {
        if (display_[0] < .5f || bloom_[1] <= 0) return nullptr;
        if (!bright_.texture)
        {
            ComPtr<ID3D11Device> device; context->GetDevice(device.GetAddressOf());
            auto create=[&](Surface& surface, UINT width, UINT height) {
                surface.width=width; surface.height=height;
                D3D11_TEXTURE2D_DESC d{}; d.Width=width; d.Height=height; d.MipLevels=1; d.ArraySize=1;
                d.Format=DXGI_FORMAT_R16G16B16A16_FLOAT; d.SampleDesc.Count=1; d.BindFlags=D3D11_BIND_RENDER_TARGET|D3D11_BIND_SHADER_RESOURCE;
                Check(device->CreateTexture2D(&d,nullptr,surface.texture.GetAddressOf()));
                Check(device->CreateRenderTargetView(surface.texture.Get(),nullptr,surface.target.GetAddressOf()));
                Check(device->CreateShaderResourceView(surface.texture.Get(),nullptr,surface.view.GetAddressOf()));
            };
            create(bright_,description_.Width,description_.Height);
            for(size_t i=0;i<bloomLevels_.size();++i) {
                const auto divisor=1u<<(i+1);
                create(bloomLevels_[i],std::max(1u,description_.Width/divisor),std::max(1u,description_.Height/divisor));
                create(bloomScratch_[i],bloomLevels_[i].width,bloomLevels_[i].height);
            }
        }
        auto pass=[&](Surface& destination, ID3D11PixelShader* shader, ID3D11ShaderResourceView* first, ID3D11ShaderResourceView* second=nullptr, float dx=0, float dy=0) {
            ID3D11ShaderResourceView* empty[3]{}; context->PSSetShaderResources(0,3,empty);
            auto target=destination.target.Get(); context->OMSetRenderTargets(1,&target,nullptr);
            D3D11_VIEWPORT viewport{0,0,static_cast<float>(destination.width),static_cast<float>(destination.height),0,1};
            context->RSSetViewports(1,&viewport); context->PSSetShader(shader,nullptr,0);
            Constants(context,destination.width,destination.height,true,dx,dy);
            ID3D11ShaderResourceView* inputs[]{first,second}; context->PSSetShaderResources(0,2,inputs); context->Draw(3,0);
        };
        pass(bright_,description_.SampleDesc.Count>1 ? extractMultisample_.Get() : extract_.Get(),colourView_.Get(),sourceView_.Get());
        auto source=bright_.view.Get();
        // Native five-stage half-resolution pyramid, with an eleven-tap separable blur at each stage.
        for(size_t i=0;i<bloomLevels_.size();++i) {
            auto& level=bloomLevels_[i]; auto& scratch=bloomScratch_[i];
            pass(level,downsample_.Get(),source);
            pass(scratch,blur_.Get(),level.view.Get(),nullptr,1.0f/level.width,0);
            pass(level,blur_.Get(),scratch.view.Get(),nullptr,0,1.0f/level.height);
            source=level.view.Get();
        }
        for(int i=static_cast<int>(bloomLevels_.size())-2;i>=0;--i) {
            pass(bloomScratch_[i],upsample_.Get(),source,bloomLevels_[i].view.Get()); source=bloomScratch_[i].view.Get();
        }
        ID3D11ShaderResourceView* empty[3]{}; context->PSSetShaderResources(0,3,empty);
        return source;
    }

    void Copy(ID3D11DeviceContext* context, ID3D11RenderTargetView* target, ID3D11ShaderResourceView* source, bool finish)
    {
        ComPtr<ID3D11VertexShader> vs; ComPtr<ID3D11PixelShader> ps; ComPtr<ID3D11GeometryShader> gs;
        ID3D11ClassInstance* vsClasses[D3D11_SHADER_MAX_INTERFACES]{}; UINT vsCount = D3D11_SHADER_MAX_INTERFACES;
        ID3D11ClassInstance* psClasses[D3D11_SHADER_MAX_INTERFACES]{}; UINT psCount = D3D11_SHADER_MAX_INTERFACES;
        ID3D11ClassInstance* gsClasses[D3D11_SHADER_MAX_INTERFACES]{}; UINT gsCount = D3D11_SHADER_MAX_INTERFACES;
        context->VSGetShader(vs.GetAddressOf(), vsClasses, &vsCount); context->PSGetShader(ps.GetAddressOf(), psClasses, &psCount); context->GSGetShader(gs.GetAddressOf(), gsClasses, &gsCount);
        ComPtr<ID3D11BlendState> blend; FLOAT factors[4]{}; UINT mask = 0; context->OMGetBlendState(blend.GetAddressOf(), factors, &mask);
        ComPtr<ID3D11DepthStencilState> depth; UINT stencil = 0; context->OMGetDepthStencilState(depth.GetAddressOf(), &stencil);
        ComPtr<ID3D11RasterizerState> raster; context->RSGetState(raster.GetAddressOf());
        ComPtr<ID3D11InputLayout> layout; D3D11_PRIMITIVE_TOPOLOGY topology; context->IAGetInputLayout(layout.GetAddressOf()); context->IAGetPrimitiveTopology(&topology);
        ComPtr<ID3D11ShaderResourceView> texture, background, halo; context->PSGetShaderResources(0, 1, texture.GetAddressOf()); context->PSGetShaderResources(1, 1, background.GetAddressOf()); context->PSGetShaderResources(2,1,halo.GetAddressOf());
        ComPtr<ID3D11SamplerState> sampler; context->PSGetSamplers(0,1,sampler.GetAddressOf());
        ComPtr<ID3D11Buffer> constants; context->PSGetConstantBuffers(0, 1, constants.GetAddressOf());
        ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{}; ComPtr<ID3D11DepthStencilView> depthTarget;
        context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depthTarget.GetAddressOf());
        D3D11_VIEWPORT viewports[D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE]{}; UINT viewportCount = D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;
        context->RSGetViewports(&viewportCount, viewports);
        D3D11_VIEWPORT viewport{0,0,static_cast<float>(description_.Width),static_cast<float>(description_.Height),0,1};
        context->OMSetRenderTargets(1, &target, nullptr); context->RSSetViewports(1, &viewport);
        context->OMSetBlendState(replace_.Get(), nullptr, 0xFFFFFFFF); context->OMSetDepthStencilState(noDepth_.Get(), 0); context->RSSetState(raster_.Get());
        context->IASetInputLayout(nullptr); context->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->VSSetShader(vertex_.Get(), nullptr, 0); context->GSSetShader(nullptr, nullptr, 0);
        auto linearSampler=linearClamp_.Get(); context->PSSetSamplers(0,1,&linearSampler);
        auto glow=finish ? Bloom(context) : nullptr;
        context->OMSetRenderTargets(1,&target,nullptr); context->RSSetViewports(1,&viewport);
        context->PSSetShader(description_.SampleDesc.Count > 1 ? multisample_.Get() : single_.Get(), nullptr, 0);
        Constants(context,description_.Width,description_.Height,finish);
        ID3D11ShaderResourceView* inputs[]{source,sourceView_.Get(),glow}; context->PSSetShaderResources(0, 3, inputs); context->Draw(3, 0);
        ID3D11ShaderResourceView* empty[3]{}; context->PSSetShaderResources(0, 3, empty);
        context->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depthTarget.Get());
        for (auto targetView : targets) if (targetView) targetView->Release();
        ID3D11ShaderResourceView* previousTextures[]{texture.Get(),background.Get(),halo.Get()}; context->PSSetShaderResources(0, 3, previousTextures);
        auto previousSampler=sampler.Get(); context->PSSetSamplers(0,1,&previousSampler);
        auto previousConstants = constants.Get(); context->PSSetConstantBuffers(0, 1, &previousConstants);
        context->VSSetShader(vs.Get(), vsClasses, vsCount); context->PSSetShader(ps.Get(), psClasses, psCount); context->GSSetShader(gs.Get(), gsClasses, gsCount);
        for (UINT i = 0; i < vsCount; ++i) if (vsClasses[i]) vsClasses[i]->Release();
        for (UINT i = 0; i < psCount; ++i) if (psClasses[i]) psClasses[i]->Release();
        for (UINT i = 0; i < gsCount; ++i) if (gsClasses[i]) gsClasses[i]->Release();
        context->IASetInputLayout(layout.Get()); context->IASetPrimitiveTopology(topology);
        context->OMSetBlendState(blend.Get(), factors, mask); context->OMSetDepthStencilState(depth.Get(), stencil); context->RSSetState(raster.Get()); context->RSSetViewports(viewportCount, viewports);
    }

public:
    class Frame
    {
        FloatSceneTarget& owner_;
        ID3D11DeviceContext* context_;
        ComPtr<ID3D11RenderTargetView> target_;
        std::array<ComPtr<ID3D11RenderTargetView>, D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT - 1> otherTargets_;
        ComPtr<ID3D11DepthStencilView> depth_;

        void Restore()
        {
            ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{target_.Get()};
            for (size_t index = 0; index < otherTargets_.size(); ++index) targets[index + 1] = otherTargets_[index].Get();
            context_->OMSetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, depth_.Get()); target_.Reset();
        }

    public:
        Frame(FloatSceneTarget& owner, ID3D11Device* device, ID3D11DeviceContext* context, const float* display, const float* bloom) : owner_(owner), context_(context)
        {
            ComPtr<ID3D11RenderTargetView> target; ComPtr<ID3D11DepthStencilView> depth;
            context->OMGetRenderTargets(1, target.GetAddressOf(), depth.GetAddressOf());
            if (!target) return;
            D3D11_RENDER_TARGET_VIEW_DESC view{}; target->GetDesc(&view);
            if (view.Format != DXGI_FORMAT_R8G8B8A8_UNORM && view.Format != DXGI_FORMAT_R8G8B8A8_UNORM_SRGB
                && view.Format != DXGI_FORMAT_B8G8R8A8_UNORM && view.Format != DXGI_FORMAT_B8G8R8A8_UNORM_SRGB) return;
            ComPtr<ID3D11Resource> resource; target->GetResource(resource.GetAddressOf()); ComPtr<ID3D11Texture2D> texture; Check(resource.As(&texture));
            D3D11_TEXTURE2D_DESC description{}; texture->GetDesc(&description);
            if (description.ArraySize != 1 || description.MipLevels != 1) throw std::runtime_error("Unsupported VFX scene target layout");
            owner_.Prepare(device, description, view.Format);
            for (size_t i = 0; i < owner_.display_.size(); ++i) owner_.display_[i] = display ? display[i] : 0;
            for (size_t i = 0; i < owner_.bloom_.size(); ++i) owner_.bloom_[i] = bloom ? bloom[i] : 0;
            ID3D11RenderTargetView* targets[D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT]{};
            context->OMGetRenderTargets(D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT, targets, nullptr);
            if (targets[0]) targets[0]->Release();
            for (size_t index = 0; index < otherTargets_.size(); ++index) otherTargets_[index].Attach(targets[index + 1]);
            context->CopyResource(owner_.sourceCopy_.Get(), texture.Get());
            owner_.Copy(context, owner_.colourTarget_.Get(), owner_.sourceView_.Get(), false);
            target_ = target; depth_ = depth;
            auto temporary = owner_.colourTarget_.Get(); context->OMSetRenderTargets(1, &temporary, depth.Get());
        }
        Frame(const Frame&) = delete;
        ~Frame() { if (target_) Restore(); }
        void Finish()
        {
            if (!target_) return;
            owner_.Copy(context_, target_.Get(), owner_.colourView_.Get(), true); Restore();
        }
    };
};
