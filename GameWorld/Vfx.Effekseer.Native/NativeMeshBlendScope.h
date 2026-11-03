#pragma once

// The pinned model renderer updates blend only when its cached enum changes (distortion=false).
// Seed that public state before setting the native blend; restore both cache and D3D after the batch.
class NativeMeshBlendScope
{
    EffekseerRendererDX11::RendererImplemented* renderer_;
    Effekseer::AlphaBlendType previous_;
public:
    NativeMeshBlendScope(EffekseerRendererDX11::RendererImplemented* renderer,
        ComPtr<ID3D11BlendState>& blend, Effekseer::AlphaBlendType state, bool opaque)
        : renderer_(renderer), previous_(renderer->GetRenderState()->GetActiveState().AlphaBlend)
    {
        if (!blend)
        {
            D3D11_BLEND_DESC description{};
            auto& target = description.RenderTarget[0];
            target.RenderTargetWriteMask = D3D11_COLOR_WRITE_ENABLE_ALL;
            target.BlendEnable = !opaque;
            target.SrcBlend = D3D11_BLEND_ONE; target.DestBlend = D3D11_BLEND_INV_SRC_ALPHA;
            target.BlendOp = D3D11_BLEND_OP_ADD;
            target.SrcBlendAlpha = D3D11_BLEND_SRC_ALPHA; target.DestBlendAlpha = D3D11_BLEND_INV_SRC_ALPHA;
            target.BlendOpAlpha = D3D11_BLEND_OP_ADD;
            if (FAILED(renderer->GetDevice()->CreateBlendState(&description, blend.GetAddressOf())))
                throw std::runtime_error("Cannot prepare native mesh composition");
        }
        auto renderState = renderer_->GetRenderState();
        renderState->GetActiveState().AlphaBlend = state; renderState->Update(false);
        renderer_->GetContext()->OMSetBlendState(blend.Get(), nullptr, 0xffffffff);
    }
    ~NativeMeshBlendScope()
    {
        auto state = renderer_->GetRenderState();
        state->GetActiveState().AlphaBlend = previous_; state->Update(true);
    }
};
