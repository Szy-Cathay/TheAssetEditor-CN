#define NOMINMAX
#include "Bridge.h"
#include <Effekseer/Effekseer.EffectImplemented.h>
#include <Effekseer/Effekseer.RectF.h>
#include <Effekseer/Effekseer.MaterialLoader.h>
#include <Effekseer/Renderer/Effekseer.SpriteRenderer.h>
#include <Effekseer/Renderer/Effekseer.ModelRenderer.h>
#include <Effekseer/Material/Effekseer.CompiledMaterial.h>
#include <EffekseerRendererDX11.h>
#include <wrl/client.h>
#include <d3dcompiler.h>
#include <DirectXPackedVector.h>
#include <algorithm>
#include <cmath>
#include <cstring>
#include <stdexcept>
#include <string>
#include <cstdio>
#include <cstdlib>
#include <array>
#include <memory>
#include <atomic>
#include "SpriteUploadCache.h"
#ifdef _DEBUG
#include <crtdbg.h>
#endif

using namespace Effekseer;
using Microsoft::WRL::ComPtr;
#define API extern "C" __declspec(dllexport)

namespace
{
thread_local std::string lastError;
#include "CompiledVfxMaterials.h"
#include "FloatSceneTarget.h"
#include "WeightedDistortionTarget.h"
#include "HalfResolutionScene.h"

// Only the pinned resource container uses an internal API; particles use the renderer's public parameters.
class ResourceEffect final : public EffectImplemented
{
public:
    ResourceEffect(const ManagerRef& manager, const ResourceEffect& source, const MaterialRef& material)
        : EffectImplemented(manager, nullptr, 0)
    {
        m_version = source.m_version;
        m_pImages = source.m_pImages; m_distortionImages = source.m_distortionImages; m_normalImages = source.m_normalImages;
        models_ = source.models_; materials_ = source.materials_;
        materials_[0] = material;
    }

    ResourceEffect(const ManagerRef& manager, const std::vector<TextureRef>& textures, const std::vector<ModelRef>& models,
        const MaterialRef& material, const MaterialRef& decalMaterial, const MaterialRef& scrollingMaterial, const MaterialRef& oneShotMaterial,
        const MaterialRef& basicMeshMaterial, const MaterialRef& gemstoneMaterial, const MaterialRef& animatedUVMaterial, const MaterialRef& basicSpriteMaterial,
        const MaterialRef& premultiplyAnimatedMaterial, const MaterialRef& solidMaterial, const MaterialRef& prelitSpriteMaterial, const MaterialRef& soulPanningMaterial,
        const MaterialRef& growingVinesMaterial, const MaterialRef& rigidDissolveMaterial, const MaterialRef& distortionMaterial,
        const MaterialRef& tidecallMaterial, const MaterialRef& vertexAnimatedMaterial, const MaterialRef& rigidBasicDitherMaterial)
        : EffectImplemented(manager, nullptr, 0)
    {
        m_version = Version17;
        m_pImages.assign(textures.begin(), textures.end());
        m_distortionImages.assign(textures.begin(), textures.end());
        m_normalImages.assign(textures.begin(), textures.end());
        models_.assign(models.begin(), models.end());
        materials_.push_back(material);
        materials_.push_back(decalMaterial);
        materials_.push_back(scrollingMaterial);
        materials_.push_back(oneShotMaterial);
        materials_.push_back(basicMeshMaterial);
        materials_.push_back(gemstoneMaterial);
        materials_.push_back(animatedUVMaterial);
        materials_.push_back(basicSpriteMaterial);
        materials_.push_back(premultiplyAnimatedMaterial);
        materials_.push_back(solidMaterial);
        materials_.push_back(prelitSpriteMaterial);
        materials_.push_back(soulPanningMaterial);
        materials_.push_back(growingVinesMaterial);
        materials_.push_back(rigidDissolveMaterial);
        materials_.push_back(distortionMaterial);
        materials_.push_back(tidecallMaterial);
        materials_.push_back(vertexAnimatedMaterial);
        materials_.push_back(rigidBasicDitherMaterial);
    }
};

struct Preview;
struct SceneDraw
{
    std::shared_ptr<Preview> preview;
    std::array<float, 16> view{}, projection{};
    std::vector<AecnParticle> opaque, colour, distortion, fallback;
    AecnLighting lighting{};
    ComPtr<ID3D11ShaderResourceView> softDepth;
};

struct Preview
{
    std::atomic<int> references{1};
    void Retain() { references.fetch_add(1, std::memory_order_relaxed); }
    void Release() { if (references.fetch_sub(1, std::memory_order_acq_rel) == 1) delete this; }

    EffekseerRendererDX11::RendererRef renderer;
    ManagerRef manager;
    RefPtr<SpriteRenderer> sprites;
    RefPtr<ModelRenderer> meshes;
    std::vector<TextureRef> textures;
    std::vector<ModelRef> models;
    std::vector<float> modelAnimationCycles;
    int64_t animatedVertices = 0, animatedIndices = 0;
    std::vector<AecnLayer> layers;
    std::vector<AecnHalfLayerConfig> halfLayers;
    std::vector<AecnHalfLayerConfig> fullLayers;
    struct NativeAlphaLayer { const AecnHalfLayerConfig* config = nullptr; bool halfResolution = false; };
    std::vector<NativeAlphaLayer> nativeAlphaLayers;
    EffectRef effect;
    MaterialLoaderRef materialLoader;
    MaterialRef material, decalMaterial, scrollingMaterial, oneShotMaterial, basicMeshMaterial, gemstoneMaterial, animatedUVMaterial, basicSpriteMaterial, premultiplyAnimatedMaterial, solidMaterial, prelitSpriteMaterial, soulPanningMaterial, growingVinesMaterial, rigidDissolveMaterial, distortionMaterial, tidecallMaterial, vertexAnimatedMaterial, rigidBasicDitherMaterial;
    AecnSpriteUpload::Renderer* uploadRenderer = nullptr;
    std::unique_ptr<AecnSpriteUpload::Shader> uploadShader;
    MaterialRef uploadMaterial;
    EffectRef uploadEffect;
    SpriteRendererRef uploadSprites;
    struct MeshMaterial { std::vector<int32_t> textures; std::array<float, 104> parameters{}; ComPtr<ID3D11Buffer> extendedParameters; bool oneShot = false, basicMesh = false, gemstone = false, animatedUV = false, basicSprite = false, premultiplyAnimated = false, solid = false, prelitSprite = false, soulPanning = false, scrollingMesh = false, growingVines = false, rigidDissolve = false, tidecall = false, vertexAnimated = false, rigidBasicDither = false; };
    std::vector<MeshMaterial> meshMaterials;
    FloatSceneTarget floatingScene;
    std::unique_ptr<FloatSceneTarget::Frame> sceneFrame;
    std::vector<SceneDraw> sceneDraws;
    std::vector<SceneDraw> recycledSceneDraws;
    WeightedDistortionTarget weightedDistortion;
    HalfResolutionScene halfScene;
    int halfDrawCalls = -1;
    ComPtr<ID3D11Texture2D> background;
    ComPtr<ID3D11ShaderResourceView> backgroundView;
    ComPtr<ID3D11Texture2D> depthCopy, resolvedDepth;
    ComPtr<ID3D11ShaderResourceView> depthView, resolvedDepthView;
    ComPtr<ID3D11UnorderedAccessView> resolvedDepthWrite;
    ComPtr<ID3D11ComputeShader> depthResolveShader;
    bool depthAvailable = false;

    void ResetSpriteUploads()
    {
        if (uploadRenderer) uploadRenderer->InvalidateCachedVertexBinding();
        uploadSprites.Reset(); uploadEffect.Reset(); uploadMaterial.Reset(); uploadShader.reset();
    }

    void PrepareSpriteUploads()
    {
        ResetSpriteUploads();
        if (!uploadRenderer || effect == nullptr || material == nullptr || !material->UserPtr || material->IsRefractionRequired) return;
        // The copied resource container changes only the shader callback type, never geometry or material data.
        CompiledMaterial compiled;
        const auto& data = CompiledVfxMaterials[0];
        if (!compiled.Load(data.Data, static_cast<int32_t>(data.Size))) return;
        auto binary = compiled.GetBinary(CompiledMaterialPlatformType::DirectX11);
        if (!binary) return;
        auto shader = std::make_unique<AecnSpriteUpload::Shader>(static_cast<EffekseerRendererDX11::Shader*>(material->UserPtr), uploadRenderer->VertexBindingState());
        if (!shader->Initialize(renderer->GetDevice(), renderer->GetContext(),
            binary->GetVertexShaderData(MaterialShaderType::Standard), binary->GetVertexShaderSize(MaterialShaderType::Standard))) return;
        auto shadow = MakeRefPtr<Material>();
        shadow->ShadingModel = material->ShadingModel; shadow->IsSimpleVertex = material->IsSimpleVertex;
        shadow->IsRefractionRequired = material->IsRefractionRequired; shadow->CustomData1 = material->CustomData1;
        shadow->CustomData2 = material->CustomData2; shadow->TextureCount = material->TextureCount;
        shadow->UniformCount = material->UniformCount; shadow->TextureWrapTypes = material->TextureWrapTypes;
        shadow->UserPtr = shader.get(); shadow->ModelUserPtr = material->ModelUserPtr;
        shadow->RefractionUserPtr = material->RefractionUserPtr; shadow->RefractionModelUserPtr = material->RefractionModelUserPtr;
        auto shadowEffect = MakeRefPtr<ResourceEffect>(manager, *static_cast<ResourceEffect*>(effect.Get()), shadow);
        auto sprites = uploadRenderer->CreateCachedSpriteRenderer();
        uploadShader = std::move(shader); uploadMaterial = shadow; uploadEffect = shadowEffect; uploadSprites = sprites;
    }

    ~Preview()
    {
        sceneFrame.reset();
        ResetSpriteUploads();
        if (materialLoader == nullptr) return;
        if (material != nullptr) materialLoader->Unload(material);
        if (decalMaterial != nullptr) materialLoader->Unload(decalMaterial);
        if (scrollingMaterial != nullptr) materialLoader->Unload(scrollingMaterial);
        if (oneShotMaterial != nullptr) materialLoader->Unload(oneShotMaterial);
        if (basicMeshMaterial != nullptr) materialLoader->Unload(basicMeshMaterial);
        if (gemstoneMaterial != nullptr) materialLoader->Unload(gemstoneMaterial);
        if (animatedUVMaterial != nullptr) materialLoader->Unload(animatedUVMaterial);
        if (basicSpriteMaterial != nullptr) materialLoader->Unload(basicSpriteMaterial);
        if (premultiplyAnimatedMaterial != nullptr) materialLoader->Unload(premultiplyAnimatedMaterial);
        if (solidMaterial != nullptr) materialLoader->Unload(solidMaterial);
        if (prelitSpriteMaterial != nullptr) materialLoader->Unload(prelitSpriteMaterial);
        if (soulPanningMaterial != nullptr) materialLoader->Unload(soulPanningMaterial);
        if (growingVinesMaterial != nullptr) materialLoader->Unload(growingVinesMaterial);
        if (rigidDissolveMaterial != nullptr) materialLoader->Unload(rigidDissolveMaterial);
        if (distortionMaterial != nullptr) materialLoader->Unload(distortionMaterial);
        if (tidecallMaterial != nullptr) materialLoader->Unload(tidecallMaterial);
        if (vertexAnimatedMaterial != nullptr) materialLoader->Unload(vertexAnimatedMaterial);
        if (rigidBasicDitherMaterial != nullptr) materialLoader->Unload(rigidBasicDitherMaterial);
    }

    void CaptureDepth(const float* projection)
    {
        auto context = renderer->GetContext();
        auto device = renderer->GetDevice();
        ComPtr<ID3D11DepthStencilView> target;
        context->OMGetRenderTargets(0, nullptr, target.GetAddressOf());
        depthAvailable = target != nullptr;
        EffekseerRenderer::DepthReconstructionParameter reconstruction;
        reconstruction.ProjectionMatrix33 = projection[10]; reconstruction.ProjectionMatrix34 = projection[14];
        reconstruction.ProjectionMatrix43 = projection[11]; reconstruction.ProjectionMatrix44 = projection[15];
        if (!target) { renderer->SetDepth(nullptr, reconstruction); return; }
        ComPtr<ID3D11Resource> resource; target->GetResource(resource.GetAddressOf());
        ComPtr<ID3D11Texture2D> original;
        if (FAILED(resource.As(&original))) throw std::runtime_error("Invalid scene depth target");
        D3D11_TEXTURE2D_DESC description; original->GetDesc(&description);
        DXGI_FORMAT viewFormat;
        switch (description.Format)
        {
        case DXGI_FORMAT_R24G8_TYPELESS: case DXGI_FORMAT_D24_UNORM_S8_UINT:
            description.Format = DXGI_FORMAT_R24G8_TYPELESS; viewFormat = DXGI_FORMAT_R24_UNORM_X8_TYPELESS; break;
        case DXGI_FORMAT_R32_TYPELESS: case DXGI_FORMAT_D32_FLOAT:
            description.Format = DXGI_FORMAT_R32_TYPELESS; viewFormat = DXGI_FORMAT_R32_FLOAT; break;
        case DXGI_FORMAT_R16_TYPELESS: case DXGI_FORMAT_D16_UNORM:
            description.Format = DXGI_FORMAT_R16_TYPELESS; viewFormat = DXGI_FORMAT_R16_UNORM; break;
        default: throw std::runtime_error("Unsupported scene depth format");
        }
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE; description.Usage = D3D11_USAGE_DEFAULT;
        description.CPUAccessFlags = 0; description.MiscFlags = 0;
        D3D11_TEXTURE2D_DESC previous{}; if (depthCopy) depthCopy->GetDesc(&previous);
        if (!depthCopy || previous.Width != description.Width || previous.Height != description.Height ||
            previous.Format != description.Format || previous.SampleDesc.Count != description.SampleDesc.Count)
        {
            depthView.Reset(); depthCopy.Reset(); resolvedDepth.Reset(); resolvedDepthView.Reset(); resolvedDepthWrite.Reset();
            D3D11_SHADER_RESOURCE_VIEW_DESC view{}; view.Format = viewFormat;
            view.ViewDimension = description.SampleDesc.Count > 1 ? D3D11_SRV_DIMENSION_TEXTURE2DMS : D3D11_SRV_DIMENSION_TEXTURE2D;
            if (description.SampleDesc.Count == 1) view.Texture2D.MipLevels = 1;
            if (FAILED(device->CreateTexture2D(&description, nullptr, depthCopy.GetAddressOf())) ||
                FAILED(device->CreateShaderResourceView(depthCopy.Get(), &view, depthView.GetAddressOf())))
                throw std::runtime_error("Cannot copy scene depth");
        }
        context->CopyResource(depthCopy.Get(), original.Get());
        auto sampleView = depthView.Get();
        if (description.SampleDesc.Count > 1)
        {
            if (!resolvedDepth)
            {
                auto resolved = description;
                resolved.Format = DXGI_FORMAT_R32_FLOAT; resolved.SampleDesc = {1, 0};
                resolved.BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_UNORDERED_ACCESS;
                if (FAILED(device->CreateTexture2D(&resolved, nullptr, resolvedDepth.GetAddressOf())) ||
                    FAILED(device->CreateShaderResourceView(resolvedDepth.Get(), nullptr, resolvedDepthView.GetAddressOf())) ||
                    FAILED(device->CreateUnorderedAccessView(resolvedDepth.Get(), nullptr, resolvedDepthWrite.GetAddressOf())))
                    throw std::runtime_error("Cannot create resolved scene depth");
            }
            if (!depthResolveShader)
            {
                const auto& code = CompiledSceneUtilityShaders[0];
                if (FAILED(device->CreateComputeShader(code.Data, code.Size, nullptr, depthResolveShader.GetAddressOf())))
                    throw std::runtime_error("Cannot create scene depth resolve");
            }
            ComPtr<ID3D11ComputeShader> savedShader;
            ComPtr<ID3D11ShaderResourceView> savedResource;
            ComPtr<ID3D11UnorderedAccessView> savedWrite;
            ID3D11ClassInstance* classes[D3D11_SHADER_MAX_INTERFACES]{}; UINT classCount = D3D11_SHADER_MAX_INTERFACES;
            context->CSGetShader(savedShader.GetAddressOf(), classes, &classCount);
            context->CSGetShaderResources(0, 1, savedResource.GetAddressOf());
            context->CSGetUnorderedAccessViews(0, 1, savedWrite.GetAddressOf());
            auto read = depthView.Get(); auto write = resolvedDepthWrite.Get();
            context->CSSetShader(depthResolveShader.Get(), nullptr, 0);
            context->CSSetShaderResources(0, 1, &read); context->CSSetUnorderedAccessViews(0, 1, &write, nullptr);
            context->Dispatch((description.Width + 15) / 16, (description.Height + 15) / 16, 1);
            read = nullptr; write = nullptr;
            context->CSSetShaderResources(0, 1, &read); context->CSSetUnorderedAccessViews(0, 1, &write, nullptr);
            read = savedResource.Get(); write = savedWrite.Get();
            context->CSSetShaderResources(0, 1, &read); context->CSSetUnorderedAccessViews(0, 1, &write, nullptr);
            context->CSSetShader(savedShader.Get(), classes, classCount);
            for (UINT i = 0; i < classCount; ++i) if (classes[i]) classes[i]->Release();
            sampleView = resolvedDepthView.Get();
        }
        renderer->SetDepth(EffekseerRendererDX11::CreateTexture(renderer->GetGraphicsDevice(), sampleView, nullptr, nullptr), reconstruction);
    }

    void CaptureBackground()
    {
        auto context = renderer->GetContext();
        ComPtr<ID3D11RenderTargetView> target;
        context->OMGetRenderTargets(1, target.GetAddressOf(), nullptr);
        if (!target) throw std::runtime_error("VFX requires a bound scene render target");
        ComPtr<ID3D11Resource> resource;
        target->GetResource(resource.GetAddressOf());
        ComPtr<ID3D11Texture2D> texture;
        if (FAILED(resource.As(&texture))) throw std::runtime_error("Invalid scene render target");
        D3D11_TEXTURE2D_DESC description;
        texture->GetDesc(&description);
        auto samples = description.SampleDesc.Count;
        description.SampleDesc = {1, 0};
        description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        description.MiscFlags = 0;
        D3D11_TEXTURE2D_DESC previous{};
        if (background) background->GetDesc(&previous);
        if (!background || previous.Width != description.Width || previous.Height != description.Height || previous.Format != description.Format)
        {
            backgroundView.Reset(); background.Reset();
            if (FAILED(renderer->GetDevice()->CreateTexture2D(&description, nullptr, background.GetAddressOf())) ||
                FAILED(renderer->GetDevice()->CreateShaderResourceView(background.Get(), nullptr, backgroundView.GetAddressOf())))
                throw std::runtime_error("Cannot copy scene colour for distortion");
        }
        if (samples > 1) context->ResolveSubresource(background.Get(), 0, texture.Get(), 0, description.Format);
        else context->CopyResource(background.Get(), texture.Get());
        renderer->EffekseerRenderer::Renderer::SetBackground(EffekseerRendererDX11::CreateTexture(renderer->GetGraphicsDevice(), backgroundView.Get(), nullptr, nullptr));
    }
};

class ExtendedMeshStageState
{
    ID3D11DeviceContext* context_;
    ComPtr<ID3D11Buffer> vertexConstants_, pixelConstants_;
    ComPtr<ID3D11ShaderResourceView> vertexTexture_;
    ComPtr<ID3D11SamplerState> vertexSampler_, pixelSampler_;
    ID3D11ShaderResourceView* pixelTextures_[5]{};
public:
    explicit ExtendedMeshStageState(ID3D11DeviceContext* context) : context_(context)
    {
        context->VSGetConstantBuffers(2, 1, vertexConstants_.GetAddressOf());
        context->PSGetConstantBuffers(2, 1, pixelConstants_.GetAddressOf());
        context->VSGetShaderResources(0, 1, vertexTexture_.GetAddressOf());
        context->VSGetSamplers(0, 1, vertexSampler_.GetAddressOf());
        context->PSGetShaderResources(8, 5, pixelTextures_);
        context->PSGetSamplers(8, 1, pixelSampler_.GetAddressOf());
    }
    ~ExtendedMeshStageState()
    {
        auto vertex = vertexConstants_.Get(), pixel = pixelConstants_.Get();
        auto texture = vertexTexture_.Get(); auto sampler = vertexSampler_.Get();
        context_->VSSetConstantBuffers(2, 1, &vertex); context_->PSSetConstantBuffers(2, 1, &pixel);
        context_->VSSetShaderResources(0, 1, &texture); context_->VSSetSamplers(0, 1, &sampler);
        sampler = pixelSampler_.Get();
        context_->PSSetShaderResources(8, 5, pixelTextures_); context_->PSSetSamplers(8, 1, &sampler);
        for (auto resource : pixelTextures_) if (resource) resource->Release();
    }
};

class CaptureDistortionBackground final : public EffekseerRenderer::DistortingCallback
{
    Preview* preview_;
public:
    explicit CaptureDistortionBackground(Preview* preview) : preview_(preview) {}
    bool OnDistorting(EffekseerRenderer::Renderer*) override
    {
        // The renderer has submitted preceding particles before invoking this callback.
        preview_->CaptureBackground();
        return true;
    }
};

template<typename F> int Guard(F operation)
{
    try { lastError.clear(); operation(); return 1; }
    catch (const std::exception& error) { lastError = error.what(); return 0; }
    catch (...) { lastError = "Unknown Effekseer adapter failure"; return 0; }
}

SIMD::Mat43f Transform(const AecnParticle& particle)
{
    const auto* m = particle.Transform;
    for (int i = 0; i < 16; ++i) if (!std::isfinite(m[i])) throw std::runtime_error("Invalid particle transform");
    return SIMD::Mat43f(m[0], m[1], m[2], m[4], m[5], m[6], m[8], m[9], m[10], m[12], m[13], m[14]);
}

bool SpriteOriginInsideXY(const AecnParticle& particle, const Matrix44& viewProjection)
{
    const auto* m = particle.Transform;
    const auto centre = [&](int c) { return m[12] * viewProjection.Values[0][c] + m[13] * viewProjection.Values[1][c]
        + m[14] * viewProjection.Values[2][c] + viewProjection.Values[3][c]; };
    const auto w = centre(3);
    return w > 0 && std::abs(centre(0)) <= w && std::abs(centre(1)) <= w;
}

bool SpriteOutsideXY(const AecnLayer& layer, const AecnParticle& particle, const float* view, const float* projection,
    const Matrix44& viewProjection)
{
    if (SpriteOriginInsideXY(particle, viewProjection)) return false;
    const auto transform = Transform(particle); // Preserve validation even for a rejected batch.
    const bool reflect = layer.CameraFacing && projection[0] * projection[5] - projection[1] * projection[4] < 0;
    unsigned outside = 15;
    for (int corner = 0; corner < 4; ++corner)
    {
        float x = (corner & 1 ? 1.0f : 0.0f) - layer.Pivot[0];
        const float y = (corner & 2 ? 1.0f : 0.0f) - layer.Pivot[1];
        if (reflect) x = -x;
        // Use the same fixed-billboard CPU transform as the pinned sprite renderer.
        const auto position = SIMD::Vec3f::Transform(SIMD::Vec3f(x, y, 0), transform);
        const double world[] = {position.GetX(), position.GetY(), position.GetZ(), 1};
        double camera[4] = {0, 0, 0, 1}, cameraError[4]{};
        for (int c = 0; c < 3; ++c)
            for (int r = 0; r < 4; ++r)
            {
                const auto term = world[r] * view[r * 4 + c];
                camera[c] += term; cameraError[c] += std::abs(term) * 0.00001;
            }
        double clip[4]{}, error[4]{};
        for (int c = 0; c < 4; ++c)
            for (int r = 0; r < 4; ++r)
            {
                const auto term = camera[r] * projection[r * 4 + c];
                clip[c] += term;
                error[c] += std::abs(term) * 0.00001 + cameraError[r] * std::abs(projection[r * 4 + c]);
            }
        for (int c = 0; c < 4; ++c)
            if (!std::isfinite(clip[c]) || !std::isfinite(error[c]) || std::abs(clip[c]) + error[c] > 1e30) return false;
        // Near-plane crossings and uncertain floating-point results always keep the original draw.
        if (clip[3] <= 0.01 + error[3] || clip[2] <= error[2]) return false;
        const auto guard = std::abs(clip[3]) * 0.01 + 0.001 + error[3];
        unsigned sides = 0;
        if (clip[0] < -clip[3] - guard - error[0]) sides |= 1;
        if (clip[0] > clip[3] + guard + error[0]) sides |= 2;
        if (clip[1] < -clip[3] - guard - error[1]) sides |= 4;
        if (clip[1] > clip[3] + guard + error[1]) sides |= 8;
        outside &= sides;
        if (!outside) return false;
    }
    return outside != 0;
}

Color Colour(const AecnParticle& particle, float brightness, float alphaScale = 1)
{
    auto channel = [](float value) { return static_cast<uint8_t>(std::lround(std::max(0.0f, std::min(1.0f, value)) * 255)); };
    return Color(channel(particle.Colour[0] / brightness), channel(particle.Colour[1] / brightness),
        channel(particle.Colour[2] / brightness), channel(particle.Colour[3] / alphaScale));
}

int AlphaExponent(float alpha)
{
    if (!std::isfinite(alpha)) throw std::runtime_error("Invalid particle colour");
    if (alpha <= 0 || (alpha >= 0.5f && alpha <= 1)) return 0;
    int exponent;
    std::frexp(alpha, &exponent);
    return exponent;
}

float ParticleBrightness(const AecnParticle& particle)
{
    const auto value = std::max(particle.Colour[0], std::max(particle.Colour[1], particle.Colour[2]));
    return value > 0 ? value : 1.0f;
}

NodeRendererBasicParameter BuildMaterial(const AecnLayer& layer, float brightness)
{
    NodeRendererBasicParameter basic;
    basic.MaterialType = layer.Distortion != 0 ? RendererMaterialType::BackDistortion : RendererMaterialType::Default;
    basic.AlphaBlend = static_cast<AlphaBlendType>(layer.Blend);
    basic.TextureIndexes[0] = layer.TextureIndex;
    basic.TextureFilters.fill(TextureFilterType::Linear);
    basic.TextureWraps.fill(TextureWrapType::Clamp);
    basic.EmissiveScaling = brightness;
    basic.DistortionIntensity = layer.Distortion;
    basic.SoftParticleDistanceFar = layer.SoftDistance;
    basic.Flipbook.EnableInterpolation = layer.AtlasBlend != 0;
    basic.Flipbook.InterpolationType = 1;
    basic.Flipbook.UVLoopType = layer.AtlasLoop;
    basic.Flipbook.FlipbookDivideX = std::max(1, layer.AtlasColumns);
    basic.Flipbook.FlipbookDivideY = std::max(1, layer.AtlasRows);
    return basic;
}

std::array<float, 4> CustomData(const AecnLayer& layer, const AecnParticle& particle)
{
    if (!layer.AtlasBlend) return {0, 0, 0, particle.ParticleSize};
    auto columns = std::max(1, layer.AtlasColumns), rows = std::max(1, layer.AtlasRows);
    auto current = static_cast<int>(std::floor(particle.Flipbook));
    auto next = layer.AtlasLoop ? (current + 1) % (columns * rows) : std::min(current + 1, columns * rows - 1);
    return { (next % columns - current % columns) * std::abs(particle.UV[2]),
        (next / columns - current / columns) * std::abs(particle.UV[3]), particle.Flipbook - current, particle.ParticleSize };
}
}

static int DrawParticles(Preview* preview, const float* view, const float* projection,
    const AecnParticle* particles, int count, const AecnLighting* lighting,
    ID3D11ShaderResourceView* softDepth, int sharedDistortionPass);
static const AecnHalfLayerConfig* HalfLayer(const Preview& preview, int layer);
static const std::vector<AecnParticle>& ExpandHalfFallback(const Preview& preview,
    const std::vector<AecnParticle>& particles, std::vector<AecnParticle>& result);
static bool TryDrawHalfScene(Preview& owner, const std::vector<SceneDraw>& draws);

API int AecnVfxAbi(int layerBytes, int particleBytes, int vertexBytes, int lightingBytes)
{
    return layerBytes == sizeof(AecnLayer) && particleBytes == sizeof(AecnParticle) && vertexBytes == sizeof(AecnVertex)
        && lightingBytes == sizeof(AecnLighting) ? 1 : 0;
}
API const char* AecnVfxLastError() { return lastError.c_str(); }
API void* AecnVfxCreate(ID3D11Device* device, ID3D11DeviceContext* context)
{
#ifdef _DEBUG
    if (std::getenv("ASSETEDITOR_VFX_HEADLESS_TEST") != nullptr)
    {
        _CrtSetReportMode(_CRT_ASSERT, _CRTDBG_MODE_FILE);
        _CrtSetReportFile(_CRT_ASSERT, _CRTDBG_FILE_STDERR);
    }
#endif
    Preview* result = nullptr;
    Guard([&]
    {
        auto preview = std::make_unique<Preview>();
        auto renderer = MakeRefPtr<AecnSpriteUpload::Renderer>(8192);
        auto graphics = EffekseerRendererDX11::CreateGraphicsDevice(device, context);
        if (renderer->Initialize(graphics.DownCast<EffekseerRendererDX11::Backend::GraphicsDevice>(), D3D11_COMPARISON_LESS_EQUAL, false))
        {
            preview->renderer = renderer;
            try
            {
                renderer->InitializeSpriteQueue(); preview->uploadRenderer = renderer.Get();
                const auto& vs = CompiledBulkSpriteShaders[0]; const auto& colour = CompiledBulkSpriteShaders[1]; const auto& alpha = CompiledBulkSpriteShaders[2];
                renderer->InitializeBulkSprites(vs.Data, vs.Size, colour.Data, colour.Size, alpha.Data, alpha.Size);
            }
            catch (...) { /* The unchanged renderer remains available if the optional queue cannot be allocated. */ }
        }
        else preview->renderer = EffekseerRendererDX11::Renderer::Create(device, context, 8192);
        if (preview->renderer == nullptr) throw std::runtime_error("Cannot create Effekseer DX11 renderer");
        preview->renderer->SetDistortingCallback(new CaptureDistortionBackground(preview.get()));
        preview->renderer->SetRestorationOfStatesFlag(true);
        preview->manager = Manager::Create(1);
        preview->sprites = preview->renderer->CreateSpriteRenderer();
        preview->meshes = preview->renderer->CreateModelRenderer();
        result = preview.release();
    });
    return result;
}
API void AecnVfxDestroy(Preview* preview)
{
    if (!preview) return;
    // Cancel an unclosed client frame before dropping its own reference, including self submissions.
    auto draws = std::move(preview->sceneDraws);
    preview->sceneFrame.reset();
    draws.clear();
    preview->Release();
}
API int AecnVfxBeginScene(Preview* preview, const AecnLighting* lighting)
{
    return Guard([&]
    {
        if (!preview || preview->renderer == nullptr || preview->sceneFrame)
            throw std::runtime_error("Invalid or already active VFX scene");
        preview->halfDrawCalls = -1;
        preview->sceneFrame = std::make_unique<FloatSceneTarget::Frame>(preview->floatingScene,
            preview->renderer->GetDevice(), preview->renderer->GetContext(),
            lighting ? lighting->Display : nullptr, lighting ? lighting->Bloom : nullptr);
    });
}
API int AecnVfxEndScene(Preview* preview)
{
    return Guard([&]
    {
        if (!preview) throw std::runtime_error("Invalid VFX scene");
        auto frame = std::move(preview->sceneFrame);
        auto draws = std::move(preview->sceneDraws);
        if (!frame) return;
        struct Recycle
        {
            Preview& Owner;
            std::vector<SceneDraw>& Draws;
            ~Recycle()
            {
                for (auto& command : Draws)
                {
                    command.preview.reset(); command.softDepth.Reset();
                    command.opaque.clear(); command.colour.clear(); command.distortion.clear(); command.fallback.clear();
                }
                // Retain only buffer capacity; never retain another preview or an old scene/depth snapshot.
                if (!Draws.empty()) Owner.recycledSceneDraws = std::move(Draws);
            }
        } recycle{*preview, draws};
        auto draw = [&](const SceneDraw& command, const std::vector<AecnParticle>& particles, int phase)
        {
            if (!particles.empty() && !DrawParticles(command.preview.get(), command.view.data(), command.projection.data(),
                particles.data(), static_cast<int>(particles.size()), &command.lighting, command.softDepth.Get(), phase))
                throw std::runtime_error(lastError);
        };
        // All opaque receivers precede colour; all colour precedes one scene-wide refraction resolve.
        for (const auto& command : draws) draw(command, command.opaque, -1);
        if (!TryDrawHalfScene(*preview, draws))
            for (auto& command : draws)
            {
                if (command.preview->halfLayers.empty() && command.preview->fullLayers.empty()) draw(command, command.colour, -1);
                else draw(command, ExpandHalfFallback(*command.preview, command.colour, command.fallback), -1);
            }
        if (std::any_of(draws.begin(), draws.end(), [](const SceneDraw& command) { return !command.distortion.empty(); }))
        {
            WeightedDistortionTarget::Frame distortion(preview->weightedDistortion,
                preview->renderer->GetDevice(), preview->renderer->GetContext());
            for (const auto& command : draws) draw(command, command.distortion, 0);
            distortion.BindTransmittance();
            for (const auto& command : draws) draw(command, command.distortion, 1);
            distortion.Finish();
        }
        frame->Finish();
    });
}
API int AecnVfxDepthAvailable(Preview* preview) { return preview->depthAvailable ? 1 : 0; }
API int AecnVfxDrawCalls(Preview* preview) { return preview->halfDrawCalls >= 0 ? preview->halfDrawCalls : preview->renderer->GetDrawCallCount(); }

API int AecnVfxTextureRgbaMipChain(Preview* preview, const void* pixels, int length, int width, int height, int srgb, int mipLevels)
{
    return Guard([&]
    {
        if (!pixels || width < 1 || height < 1 || width > 4096 || height > 4096 || mipLevels < 1 || mipLevels > 13)
            throw std::runtime_error("Invalid decoded texture dimensions");
        std::array<D3D11_SUBRESOURCE_DATA, 13> data{};
        int64_t offset = 0;
        for (int mip = 0; mip < mipLevels; ++mip)
        {
            if (mip > 0 && (width >> (mip - 1)) <= 1 && (height >> (mip - 1)) <= 1)
                throw std::runtime_error("Invalid decoded mip count");
            const auto w = std::max(1, width >> mip), h = std::max(1, height >> mip);
            const int64_t size = static_cast<int64_t>(w) * h * 4;
            if (offset + size > length) throw std::runtime_error("Truncated decoded mip chain");
            data[mip].pSysMem = static_cast<const uint8_t*>(pixels) + offset;
            data[mip].SysMemPitch = w * 4;
            offset += size;
        }
        if (offset != length) throw std::runtime_error("Invalid decoded mip chain length");
        D3D11_TEXTURE2D_DESC description{};
        description.Width = width; description.Height = height;
        description.MipLevels = mipLevels; description.ArraySize = 1;
        description.Format = srgb ? DXGI_FORMAT_R8G8B8A8_UNORM_SRGB : DXGI_FORMAT_R8G8B8A8_UNORM;
        description.SampleDesc.Count = 1;
        description.Usage = D3D11_USAGE_DEFAULT; description.BindFlags = D3D11_BIND_SHADER_RESOURCE;
        ComPtr<ID3D11Texture2D> resource;
        ComPtr<ID3D11ShaderResourceView> view;
        if (FAILED(preview->renderer->GetDevice()->CreateTexture2D(&description, data.data(), resource.GetAddressOf())) ||
            FAILED(preview->renderer->GetDevice()->CreateShaderResourceView(resource.Get(), nullptr, view.GetAddressOf())))
            throw std::runtime_error("Cannot upload decoded game texture");
        auto texture = MakeRefPtr<Texture>();
        texture->SetBackend(EffekseerRendererDX11::CreateTexture(preview->renderer->GetGraphicsDevice(), view.Get(), nullptr, nullptr));
        preview->textures.push_back(texture);
    });
}
API int AecnVfxTextureRgba(Preview* preview, const void* pixels, int length, int width, int height, int srgb)
{
    return AecnVfxTextureRgbaMipChain(preview, pixels, length, width, height, srgb, 1);
}
API int AecnVfxTextureAlias(Preview* preview, int sourceIndex)
{
    return Guard([&]
    {
        if (sourceIndex < 0 || sourceIndex >= static_cast<int>(preview->textures.size()))
            throw std::runtime_error("Invalid shared texture index");
        auto texture = preview->textures[sourceIndex];
        preview->textures.push_back(texture);
    });
}
API int AecnVfxModel(Preview* preview, const AecnVertex* vertices, int vertexCount, const int32_t* indices, int indexCount)
{
    return Guard([&]
    {
        if (vertexCount < 3 || vertexCount > 1000000 || indexCount < 3 || indexCount % 3 != 0)
            throw std::runtime_error("Invalid model geometry");
        CustomVector<Model::Vertex> converted(vertexCount);
        CustomVector<Model::Face> faces(indexCount / 3);
        for (int i = 0; i < vertexCount; ++i)
        {
            const auto& source = vertices[i]; auto& vertex = converted[i];
            vertex.Position = {source.Position[0], source.Position[1], source.Position[2]};
            vertex.Normal = {source.Normal[0], source.Normal[1], source.Normal[2]};
            vertex.Tangent = {source.Tangent[0], source.Tangent[1], source.Tangent[2]};
            vertex.Binormal = {source.Binormal[0], source.Binormal[1], source.Binormal[2]};
            vertex.UV = {source.UV[0], source.UV[1]};
            auto channel = [](float value) { return static_cast<uint8_t>(std::lround(std::max(0.0f, std::min(1.0f, value)) * 255)); };
            vertex.VColor = {channel(source.Colour[0]), channel(source.Colour[1]), channel(source.Colour[2]), channel(source.Colour[3])};
        }
        for (int i = 0; i < indexCount; ++i)
        {
            if (indices[i] < 0 || indices[i] >= vertexCount) throw std::runtime_error("Model index out of range");
            faces[i / 3].Indexes[i % 3] = indices[i];
        }
        preview->models.push_back(MakeRefPtr<Model>(converted, faces));
        preview->modelAnimationCycles.push_back(0);
    });
}
API int AecnVfxModelAlias(Preview* preview, int sourceIndex)
{
    return Guard([&]
    {
        if (sourceIndex < 0 || sourceIndex >= static_cast<int>(preview->models.size()))
            throw std::runtime_error("Invalid shared model index");
        auto model = preview->models[sourceIndex];
        const auto cycles = preview->modelAnimationCycles[sourceIndex];
        preview->models.push_back(model);
        preview->modelAnimationCycles.push_back(cycles);
    });
}
API int AecnVfxModelFrames(Preview* preview, const AecnVertex* vertices, int vertexLength, int verticesPerFrame,
    int frameCount, const int32_t* indices, int indexCount, float cycles)
{
    return Guard([&]
    {
        static_assert(sizeof(AecnVertex) == 72);
        constexpr int64_t maximumBytes = 96000000, maximumIndices = 6000000;
        constexpr auto vertexBytes = static_cast<int64_t>(sizeof(AecnVertex));
        constexpr auto indexBytes = static_cast<int64_t>(sizeof(int32_t));
        const auto totalVertices = static_cast<int64_t>(verticesPerFrame) * frameCount;
        const auto totalIndices = static_cast<int64_t>(indexCount) * frameCount;
        if (preview == nullptr || vertices == nullptr || indices == nullptr || verticesPerFrame < 3 || frameCount < 2 || frameCount > 1800
            || totalVertices != vertexLength || totalVertices > maximumBytes / vertexBytes || indexCount < 3 || indexCount % 3 != 0
            || totalIndices > maximumIndices || preview->animatedVertices > maximumBytes / vertexBytes - totalVertices
            || preview->animatedIndices > maximumIndices - totalIndices || !std::isfinite(cycles) || cycles <= 0 || cycles > 10)
            throw std::runtime_error("Invalid animated model geometry or budget");
        const auto accumulatedVertices = preview->animatedVertices + totalVertices;
        const auto accumulatedIndices = preview->animatedIndices + totalIndices;
        if (accumulatedVertices * vertexBytes + accumulatedIndices * indexBytes > maximumBytes)
            throw std::runtime_error("Invalid animated model geometry or budget");
        for (int i = 0; i < indexCount; ++i)
            if (indices[i] < 0 || indices[i] >= verticesPerFrame) throw std::runtime_error("Animated model index out of range");
        // Build only a validated version-5 container; never hand untrusted binary data to the pinned constructor.
        static_assert(sizeof(Model::Face) == sizeof(int32_t) * 3);
        const auto bytesPerFrame = sizeof(int32_t) * 2 + sizeof(Model::Vertex) * verticesPerFrame + sizeof(int32_t) * indexCount;
        std::vector<uint8_t> bytes(sizeof(int32_t) * 4 + bytesPerFrame * frameCount);
        auto target = bytes.data();
        auto write = [&](const void* data, size_t length) { std::memcpy(target, data, length); target += length; };
        const int32_t version = 5, scale = 0x3f800000, modelCount = 1;
        write(&version, sizeof(version)); write(&scale, sizeof(scale)); write(&modelCount, sizeof(modelCount)); write(&frameCount, sizeof(frameCount));
        for (int frame = 0; frame < frameCount; ++frame)
        {
            write(&verticesPerFrame, sizeof(verticesPerFrame));
            for (int i = 0; i < verticesPerFrame; ++i)
            {
                const auto& source = vertices[frame * verticesPerFrame + i];
                Model::Vertex vertex{};
                vertex.Position = {source.Position[0], source.Position[1], source.Position[2]};
                vertex.Normal = {source.Normal[0], source.Normal[1], source.Normal[2]};
                vertex.Tangent = {source.Tangent[0], source.Tangent[1], source.Tangent[2]};
                vertex.Binormal = {source.Binormal[0], source.Binormal[1], source.Binormal[2]};
                vertex.UV = {source.UV[0], source.UV[1]};
                auto channel = [](float value) { return static_cast<uint8_t>(std::lround(std::max(0.0f, std::min(1.0f, value)) * 255)); };
                vertex.VColor = {channel(source.Colour[0]), channel(source.Colour[1]), channel(source.Colour[2]), channel(source.Colour[3])};
                write(&vertex, sizeof(vertex));
            }
            const int32_t faceCount = indexCount / 3;
            write(&faceCount, sizeof(faceCount)); write(indices, sizeof(int32_t) * indexCount);
        }
        preview->models.push_back(MakeRefPtr<Model>(bytes.data(), static_cast<int32_t>(bytes.size())));
        preview->modelAnimationCycles.push_back(cycles);
        preview->animatedVertices = accumulatedVertices;
        preview->animatedIndices = accumulatedIndices;
    });
}
API int AecnVfxMeshMaterial(Preview* preview, const int32_t* textures, int textureCount, const float* parameters, int parameterCount)
{
    return Guard([&]
    {
        const auto animatedUV = textureCount == 6 && parameterCount == 36;
        const auto basicSprite = textureCount == 4 && parameterCount == 20;
        const auto premultiplyAnimated = textureCount == 6 && parameterCount == 32;
        const auto solid = textureCount == 6 && parameterCount == 56;
        const auto prelitSprite = textureCount == 5 && parameterCount == 24;
        const auto soulPanning = textureCount == 4 && parameterCount == 28;
        const auto growingVines = textureCount == 5 && parameterCount == 12;
        const auto rigidDissolve = textureCount == 6 && parameterCount == 60;
        const auto tidecall = textureCount == 12 && parameterCount == 104;
        const auto vertexAnimated = textureCount == 6 && parameterCount == 64;
        const auto rigidBasicDither = textureCount == 4 && parameterCount == 8;
        if ((!rigidBasicDither && !vertexAnimated && !tidecall && !rigidDissolve && !growingVines && !soulPanning && !prelitSprite && !solid && !premultiplyAnimated && !animatedUV && !basicSprite && (textureCount != 6 || (parameterCount != 40 && parameterCount != 49 && parameterCount != 54 && parameterCount != 55))) || preview->meshMaterials.size() >= 256)
            throw std::runtime_error("Invalid scrolling mesh material layout");
        Preview::MeshMaterial material;
        material.textures.resize(textureCount);
        material.animatedUV = animatedUV;
        material.basicSprite = basicSprite;
        material.premultiplyAnimated = premultiplyAnimated;
        material.solid = solid;
        material.prelitSprite = prelitSprite;
        material.soulPanning = soulPanning;
        material.growingVines = growingVines;
        material.rigidDissolve = rigidDissolve;
        material.tidecall = tidecall;
        material.vertexAnimated = vertexAnimated;
        material.rigidBasicDither = rigidBasicDither;
        if (rigidBasicDither && (parameters[6] != 0 || parameters[7] != 0)) throw std::runtime_error("Invalid rigid dither padding");
        if (vertexAnimated && (parameters[18] < 0 || parameters[18] > 1)) throw std::runtime_error("Invalid vertex-animated fog control");
        if (rigidDissolve && parameters[59] == 1)
        {
            for (int index = 46; index < 58; index += 4)
                if (parameters[index] < 0 || parameters[index + 1] < 0 || parameters[index + 2] <= 0 || parameters[index + 3] <= 0
                    || parameters[index] + parameters[index + 2] > 1.000001f || parameters[index + 1] + parameters[index + 3] > 1.000001f)
                    throw std::runtime_error("Invalid dissolve mask rectangle");
        }
        else if (rigidDissolve)
        {
            if (parameters[59] != 0) throw std::runtime_error("Invalid dissolve binding mode");
            for (int index = 48; index < 58; ++index)
                if (!std::isfinite(parameters[index]) || parameters[index] < 0 || parameters[index] >= textureCount || std::floor(parameters[index]) != parameters[index])
                    throw std::runtime_error("Invalid dissolve texture alias");
        }
        material.scrollingMesh = parameterCount == 49;
        material.oneShot = parameterCount == 54;
        material.basicMesh = parameterCount == 55;
        material.gemstone = parameterCount == 40;
        if (material.growingVines && parameters[8] < 0) throw std::runtime_error("Invalid growing-vines fresnel power");
        if (material.gemstone && parameters[0] != 1) throw std::runtime_error("Unsupported gemstone dither alpha");
        if (material.premultiplyAnimated && parameters[25] != 0) throw std::runtime_error("Unsupported animated material metallic response");
        if (material.prelitSprite && parameters[17] != 0) throw std::runtime_error("Unsupported sun-scaled sprite emission");
        if (material.soulPanning && (parameters[0] < 0 || parameters[3] <= 0
            || (parameters[4] != 0 && parameters[4] != 1))) throw std::runtime_error("Invalid soul-panning parameters");
        for (int index = 0; index < textureCount; ++index)
        {
            if (textures[index] < 0 || textures[index] >= static_cast<int>(preview->textures.size()))
                throw std::runtime_error("Scrolling mesh texture is missing");
            material.textures[index] = textures[index];
        }
        for (int index = 0; index < parameterCount; ++index)
        {
            if (!std::isfinite(parameters[index])) throw std::runtime_error("Invalid scrolling mesh parameter");
            material.parameters[index] = parameters[index];
        }
        if (tidecall || vertexAnimated)
        {
            for (int index = 92; tidecall && index < 104; ++index)
                if (parameters[index] < 0 || parameters[index] > 10 || std::floor(parameters[index]) != parameters[index])
                    throw std::runtime_error("Invalid tidecall texture alias");
            D3D11_BUFFER_DESC description{};
            description.ByteWidth = parameterCount * sizeof(float); description.Usage = D3D11_USAGE_IMMUTABLE;
            description.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
            D3D11_SUBRESOURCE_DATA initial{}; initial.pSysMem = parameters;
            if (FAILED(preview->renderer->GetDevice()->CreateBuffer(&description, &initial, material.extendedParameters.GetAddressOf())))
                throw std::runtime_error("Cannot upload extended mesh parameters");
        }
        preview->meshMaterials.push_back(material);
    });
}
API int AecnVfxConfigure(Preview* preview, const AecnLayer* layers, int count)
{
    return Guard([&]
    {
        if (count < 1 || count > 512) throw std::runtime_error("Invalid layer count");
        for (int i = 0; i < count; ++i)
        {
            const auto& layer = layers[i];
            if (layer.TextureIndex < (layer.Distortion != 0 ? -1 : 0) || layer.TextureIndex >= static_cast<int>(preview->textures.size()))
                throw std::runtime_error("Layer texture is missing");
            if (layer.DistortionMaskIndex < -1 || layer.DistortionMaskIndex >= static_cast<int>(preview->textures.size())
                || (layer.DistortionMaskIndex >= 0 && layer.Distortion == 0)
                || (layer.Distortion != 0 && layer.TextureIndex < 0 && layer.DistortionMaskIndex < 0 && layer.GradientIndex < 0))
                throw std::runtime_error("Invalid distortion opacity texture");
            if (layer.NativeSoftDepth < 0 || layer.NativeSoftDepth > 1
                || (layer.NativeSoftDepth && (layer.Kind != 0 || layer.Distortion != 0 || layer.MeshMaterialIndex >= 0))
                || layer.DistortionSoftDepth < 0 || layer.DistortionSoftDepth > 1
                || !std::isfinite(layer.DistortionSoftScaleStrength) || !std::isfinite(layer.SoftDistance)
                || layer.SoftDistance < 0 || (layer.DistortionSoftDepth && layer.Distortion == 0))
                throw std::runtime_error("Invalid distortion soft depth");
            if (layer.Kind != 0 && ((layer.Kind != 1 && layer.Kind != 2) || layer.ModelIndex < 0 || layer.ModelIndex >= static_cast<int>(preview->models.size())))
                throw std::runtime_error("Unsupported layer geometry");
            const bool precise = layer.MaterialMode == PreciseCoverageMode || layer.MaterialMode == PreciseEmissionMode;
            if (layer.MaterialMode < 0 || layer.MaterialMode > PreciseEmissionMode || (layer.MaterialMode && layer.Distortion != 0)
                || (!precise && layer.MaterialMode >= 3 && (layer.Kind != 2 || layer.Blend != 2))
                || (precise && (layer.Kind == 2 || layer.MeshMaterialIndex >= 0 || layer.GradientIndex >= 0 || layer.OpaqueModel
                    || layer.Blend != (layer.MaterialMode == PreciseCoverageMode ? 1 : 2))))
                throw std::runtime_error("Unsupported particle material mode");
            if (layer.UvRotation < 0 || layer.UvRotation > 2)
                throw std::runtime_error("Unsupported UV orientation");
            if (layer.CameraFacing < 0 || layer.CameraFacing > 1 || (layer.CameraFacing && layer.Kind != 0))
                throw std::runtime_error("Invalid camera-facing geometry");
            if (layer.OpaqueModel < 0 || layer.OpaqueModel > 1 || (layer.OpaqueModel && (layer.Kind != 1 || (layer.MeshMaterialIndex >= 0 && (layer.MeshMaterialIndex >= static_cast<int>(preview->meshMaterials.size()) || (!preview->meshMaterials[layer.MeshMaterialIndex].tidecall && !preview->meshMaterials[layer.MeshMaterialIndex].solid && !preview->meshMaterials[layer.MeshMaterialIndex].growingVines && !preview->meshMaterials[layer.MeshMaterialIndex].rigidDissolve && !preview->meshMaterials[layer.MeshMaterialIndex].rigidBasicDither))))))
                throw std::runtime_error("Invalid opaque model material");
            if (layer.LightingMode < 0 || layer.LightingMode > 2 || layer.NormalIndex < -1 || layer.NormalIndex >= static_cast<int>(preview->textures.size()))
                throw std::runtime_error("Invalid particle lighting material");
            if (!std::isfinite(layer.EmissionShapeWeight) || layer.EmissionShapeWeight < 0 || layer.EmissionShapeWeight > 1)
                throw std::runtime_error("Invalid texture emission shape weight");
            if (layer.MeshMaterialIndex < -1 || layer.MeshMaterialIndex >= static_cast<int>(preview->meshMaterials.size())
                || (layer.MeshMaterialIndex >= 0 && (layer.Kind != (preview->meshMaterials[layer.MeshMaterialIndex].animatedUV || preview->meshMaterials[layer.MeshMaterialIndex].basicSprite || preview->meshMaterials[layer.MeshMaterialIndex].prelitSprite ? 0 : 1) || layer.Distortion != 0 || layer.MaterialMode != 0)))
                throw std::runtime_error("Invalid scrolling mesh binding");
            if (layer.GradientIndex < -1 || layer.GradientIndex >= static_cast<int>(preview->textures.size())
                || (layer.GradientIndex >= 0 && (layer.MeshMaterialIndex >= 0
                    || !std::isfinite(layer.GradientScale) || layer.GradientScale < 1 || layer.GradientScale > 1000000)))
                throw std::runtime_error("Invalid particle gradient");
        }
        if (preview->material == nullptr && std::any_of(layers, layers + count, [](const AecnLayer& layer) { return layer.MaterialMode != 0 || layer.LightingMode != 0 || layer.GradientIndex >= 0 || (layer.Kind == 0 && layer.MeshMaterialIndex < 0 && layer.TextureIndex >= 0); }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[0];
            preview->material = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->material == nullptr) throw std::runtime_error("Cannot compile the particle coverage/emission material");
        }
        if (preview->decalMaterial == nullptr && std::any_of(layers, layers + count, [](const AecnLayer& layer) { return layer.Kind == 2; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[1];
            preview->decalMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->decalMaterial == nullptr) throw std::runtime_error("Cannot compile the projected decal material");
        }
        if (preview->scrollingMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.scrollingMesh; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[2];
            preview->scrollingMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->scrollingMaterial == nullptr) throw std::runtime_error("Cannot compile the scrolling mesh material");
        }
        if (preview->oneShotMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.oneShot; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[3];
            preview->oneShotMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->oneShotMaterial == nullptr) throw std::runtime_error("Cannot compile the one-shot mesh material");
        }
        preview->layers.assign(layers, layers + count);
        preview->halfLayers.clear();
        preview->fullLayers.clear();
        preview->nativeAlphaLayers.clear();
        preview->halfDrawCalls = -1;
        if (preview->basicMeshMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.basicMesh; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[4];
            preview->basicMeshMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->basicMeshMaterial == nullptr) throw std::runtime_error("Cannot compile the basic mesh material");
        }
        if (preview->gemstoneMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.gemstone; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[5];
            preview->gemstoneMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->gemstoneMaterial == nullptr) throw std::runtime_error("Cannot compile the gemstone material");
        }
        if (preview->animatedUVMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.animatedUV; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[6];
            preview->animatedUVMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->animatedUVMaterial == nullptr) throw std::runtime_error("Cannot compile the animated UV sprite material");
        }
        if (preview->basicSpriteMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.basicSprite; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[7];
            preview->basicSpriteMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->basicSpriteMaterial == nullptr) throw std::runtime_error("Cannot load the basic sprite material");
        }
        if (preview->premultiplyAnimatedMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.premultiplyAnimated; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[8];
            preview->premultiplyAnimatedMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->premultiplyAnimatedMaterial == nullptr) throw std::runtime_error("Cannot load the premultiply animated UV material");
        }
        if (preview->solidMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.solid; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[9];
            preview->solidMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->solidMaterial == nullptr) throw std::runtime_error("Cannot load the solid mesh material");
        }
        if (preview->prelitSpriteMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.prelitSprite; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[10];
            preview->prelitSpriteMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->prelitSpriteMaterial == nullptr) throw std::runtime_error("Cannot load the pre-lit sprite material");
        }
        if (preview->soulPanningMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.soulPanning; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[11];
            preview->soulPanningMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->soulPanningMaterial == nullptr) throw std::runtime_error("Cannot load the soul-panning material");
        }
        if (preview->growingVinesMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.growingVines; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[12];
            preview->growingVinesMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->growingVinesMaterial == nullptr) throw std::runtime_error("Cannot load the growing-vines material");
        }
        if (preview->rigidDissolveMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.rigidDissolve; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[13];
            preview->rigidDissolveMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->rigidDissolveMaterial == nullptr) throw std::runtime_error("Cannot load the rigid dissolve material");
        }
        if (preview->distortionMaterial == nullptr && std::any_of(layers, layers + count, [](const AecnLayer& layer) { return layer.Distortion != 0; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[14];
            preview->distortionMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->distortionMaterial == nullptr) throw std::runtime_error("Cannot load the distortion opacity material");
        }
        if (preview->tidecallMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const Preview::MeshMaterial& material) { return material.tidecall; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[15];
            preview->tidecallMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->tidecallMaterial == nullptr) throw std::runtime_error("Cannot load the tidecall material");
        }
        if (preview->vertexAnimatedMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.vertexAnimated; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[16];
            preview->vertexAnimatedMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->vertexAnimatedMaterial == nullptr) throw std::runtime_error("Cannot load the vertex-animated mesh material");
        }
        if (preview->rigidBasicDitherMaterial == nullptr && std::any_of(preview->meshMaterials.begin(), preview->meshMaterials.end(), [](const auto& material) { return material.rigidBasicDither; }))
        {
            if (preview->materialLoader == nullptr) preview->materialLoader = preview->renderer->CreateMaterialLoader();
            const auto& bytes = CompiledVfxMaterials[17];
            preview->rigidBasicDitherMaterial = preview->materialLoader->Load(bytes.Data, bytes.Size, MaterialFileType::Compiled);
            if (preview->rigidBasicDitherMaterial == nullptr) throw std::runtime_error("Cannot load the rigid dither material");
        }
        preview->effect = MakeRefPtr<ResourceEffect>(preview->manager, preview->textures, preview->models, preview->material, preview->decalMaterial, preview->scrollingMaterial, preview->oneShotMaterial, preview->basicMeshMaterial, preview->gemstoneMaterial, preview->animatedUVMaterial, preview->basicSpriteMaterial, preview->premultiplyAnimatedMaterial, preview->solidMaterial, preview->prelitSpriteMaterial, preview->soulPanningMaterial, preview->growingVinesMaterial, preview->rigidDissolveMaterial, preview->distortionMaterial, preview->tidecallMaterial, preview->vertexAnimatedMaterial, preview->rigidBasicDitherMaterial);
        try { preview->PrepareSpriteUploads(); }
        catch (...) { preview->ResetSpriteUploads(); }
    });
}

static int ConfigureNativeAlpha(Preview* preview, const AecnHalfLayerConfig* configs, int count, int configBytes, bool halfResolution)
{
    return Guard([&]
    {
        if (!preview || preview->sceneFrame || count < 0 || count > 512 || configBytes != sizeof(AecnHalfLayerConfig)
            || (count > 0 && !configs)) throw std::runtime_error("Invalid half-resolution layer configuration");
        std::vector<AecnHalfLayerConfig> validated;
        validated.reserve(count);
        const auto& other = halfResolution ? preview->fullLayers : preview->halfLayers;
        for (int index = 0; index < count; ++index)
        {
            const auto& config = configs[index];
            const auto validLayer = [&](int layer) { return layer >= 0 && layer < static_cast<int>(preview->layers.size()); };
            if (!validLayer(config.LayerIndex) || config.Premultiplied < 0 || config.Premultiplied > 1
                || !std::isfinite(config.CoveredColourMultiplier) || config.CoveredColourMultiplier < 0
                || !std::isfinite(config.EmissionMultiplier) || config.EmissionMultiplier < 0
                || (config.LegacyCoverageIndex == -1 && config.LegacyEmissionIndex == -1))
                throw std::runtime_error("Invalid original half-resolution particle layer");
            const auto& layer = preview->layers[config.LayerIndex];
            if (layer.Kind != 0 || layer.Distortion != 0 || layer.ModelIndex >= 0 || layer.OpaqueModel != 0
                || layer.MeshMaterialIndex >= 0 || layer.GradientIndex >= 0 || layer.MaterialMode < 0 || layer.MaterialMode > 1
                || layer.TextureIndex < 0 || layer.Blend != 1 || layer.MaterialMode != config.Premultiplied)
                throw std::runtime_error("Unsupported half-resolution particle material");
            const auto checkLegacy = [&](int legacy, bool coverage)
            {
                if (legacy == -1) return;
                if (!validLayer(legacy) || legacy == config.LayerIndex)
                    throw std::runtime_error("Invalid half-resolution fallback layer");
                const auto& fallback = preview->layers[legacy];
                const bool validMode = coverage
                    ? fallback.MaterialMode == config.Premultiplied || (config.Premultiplied && fallback.MaterialMode == PreciseCoverageMode)
                    : config.Premultiplied && (fallback.MaterialMode == 2 || fallback.MaterialMode == PreciseEmissionMode);
                auto expected = layer; expected.MaterialMode = fallback.MaterialMode; expected.Blend = coverage ? 1 : 2;
                if (!validMode || std::memcmp(&expected, &fallback, sizeof(AecnLayer)) != 0)
                    throw std::runtime_error("Inconsistent half-resolution fallback material");
            };
            checkLegacy(config.LegacyCoverageIndex, true);
            checkLegacy(config.LegacyEmissionIndex, false);
            if (std::any_of(validated.begin(), validated.end(), [&](const AecnHalfLayerConfig& value) { return value.LayerIndex == config.LayerIndex; }))
                throw std::runtime_error("Duplicate half-resolution layer");
            if (std::any_of(other.begin(), other.end(), [&](const AecnHalfLayerConfig& value) { return value.LayerIndex == config.LayerIndex; }))
                throw std::runtime_error("Conflicting native alpha resolution");
            validated.push_back(config);
        }
        std::vector<Preview::NativeAlphaLayer> lookup;
        if (!validated.empty() || !other.empty())
        {
            lookup.resize(preview->layers.size());
            for (const auto& config : other) lookup[config.LayerIndex] = { &config, !halfResolution };
            for (const auto& config : validated) lookup[config.LayerIndex] = { &config, halfResolution };
        }
        // Vector moves retain element addresses; publish only after every validation/allocation succeeds.
        (halfResolution ? preview->halfLayers : preview->fullLayers) = std::move(validated);
        preview->nativeAlphaLayers = std::move(lookup);
    });
}

API int AecnVfxConfigureHalf(Preview* preview, const AecnHalfLayerConfig* configs, int count, int configBytes)
{
    return ConfigureNativeAlpha(preview, configs, count, configBytes, true);
}

API int AecnVfxConfigureFull(Preview* preview, const AecnHalfLayerConfig* configs, int count, int configBytes)
{
    return ConfigureNativeAlpha(preview, configs, count, configBytes, false);
}

static const AecnHalfLayerConfig* HalfLayer(const Preview& preview, int layer)
{
    if (preview.nativeAlphaLayers.empty() || layer < 0 || static_cast<size_t>(layer) >= preview.nativeAlphaLayers.size())
        return nullptr;
    return preview.nativeAlphaLayers[layer].config;
}

static const std::vector<AecnParticle>& ExpandHalfFallback(const Preview& preview,
    const std::vector<AecnParticle>& particles, std::vector<AecnParticle>& result)
{
    result.clear();
    result.reserve(particles.size() * 2);
    for (const auto& particle : particles)
    {
        const auto config = HalfLayer(preview, particle.Layer);
        if (!config) { result.push_back(particle); continue; }
        const auto append = [&](int layer, float multiplier)
        {
            if (layer < 0) return;
            auto copy = particle; copy.Layer = layer;
            for (int channel = 0; channel < 3; ++channel) copy.Colour[channel] *= multiplier;
            result.push_back(copy);
        };
        // Match the existing per-particle coverage/emission order, including absent constant-alpha layers.
        append(config->LegacyCoverageIndex, config->CoveredColourMultiplier);
        append(config->LegacyEmissionIndex, config->EmissionMultiplier);
    }
    if (result.size() > 1048576) throw std::runtime_error("Half-resolution fallback exceeds the particle budget");
    return result;
}

static bool TryDrawHalfScene(Preview& owner, const std::vector<SceneDraw>& draws)
{
    std::vector<HalfResolutionScene::Draw> halfDraws;
    for (const auto& command : draws)
    {
        // Preserve the established ordering for mixed full-resolution, model, decal and refraction scenes.
        if (!command.opaque.empty() || !command.distortion.empty()) return false;
        const auto& preview = *command.preview;
        size_t begin = 0;
        while (begin < command.colour.size())
        {
            const auto layerIndex = command.colour[begin].Layer;
            const auto config = HalfLayer(preview, layerIndex);
            if (!config) return false;
            size_t end = begin + 1;
            while (end < command.colour.size() && command.colour[end].Layer == layerIndex) ++end;
            const auto& layer = preview.layers[layerIndex];
            const auto texture = [&](int index)
            {
                return EffekseerRendererDX11::GetTextureProperty(preview.textures[index]->GetBackend()).ShaderResourceViewPtr;
            };
            HalfResolutionScene::Draw draw;
            draw.Layer = &layer; draw.Config = config; draw.Particles = command.colour.data() + begin;
            draw.Count = static_cast<int>(end - begin); draw.View = command.view.data(); draw.Projection = command.projection.data();
            draw.Lighting = &command.lighting; draw.SoftDepth = command.softDepth.Get();
            draw.HalfResolution = preview.nativeAlphaLayers[layerIndex].halfResolution;
            draw.Textures = { texture(layer.TextureIndex), texture(layer.NormalIndex >= 0 ? layer.NormalIndex : layer.TextureIndex),
                texture(layer.GradientIndex >= 0 ? layer.GradientIndex : layer.TextureIndex) };
            halfDraws.push_back(draw);
            begin = end;
        }
    }
    if (halfDraws.empty()) return false;
    // Even materials without soft fading need the current hardware depth for the native half-size raster pass.
    owner.CaptureDepth(halfDraws.front().Projection);
    ID3D11ShaderResourceView* fullDepth = nullptr;
    if (owner.depthAvailable)
    {
        D3D11_TEXTURE2D_DESC description{}; owner.depthCopy->GetDesc(&description);
        fullDepth = description.SampleDesc.Count > 1 ? owner.resolvedDepthView.Get() : owner.depthView.Get();
    }
    auto context = owner.renderer->GetContext();
    if (!HalfResolutionScene::CanRender(context, halfDraws, fullDepth)) return false;
    for (const auto& command : draws) { command.preview->renderer->ResetDrawCallCount(); command.preview->halfDrawCalls = 0; }
    owner.halfScene.Render(owner.renderer->GetDevice(), context, halfDraws, fullDepth);
    owner.halfDrawCalls = static_cast<int>(owner.halfScene.DrawCallCount());
    return true;
}

static int DrawParticles(Preview* preview, const float* view, const float* projection, const AecnParticle* particles, int count, const AecnLighting* lighting,
    ID3D11ShaderResourceView* softDepth, int sharedDistortionPass)
{
    return Guard([&]
    {
        if (count < 0 || count > 1048576) throw std::runtime_error("Invalid particle count");
        preview->halfDrawCalls = -1;
        preview->renderer->ResetDrawCallCount();
        if (count == 0) return;
        bool sawDistortion = false, needsFinalDistortionPass = false;
        for (int index = 0; index < count; ++index)
        {
            const auto layer = particles[index].Layer;
            if (layer < 0 || layer >= static_cast<int>(preview->layers.size())) throw std::runtime_error("Invalid particle layer");
            if (preview->layers[layer].Distortion != 0) sawDistortion = true;
            else if (sawDistortion) needsFinalDistortionPass = true;
        }
        std::vector<AecnParticle> orderedParticles;
        if (needsFinalDistortionPass)
        {
            // Native composition draws colour first, then resolves all distortion in this effect together.
            orderedParticles.reserve(count);
            for (int index = 0; index < count; ++index)
                if (preview->layers[particles[index].Layer].Distortion == 0) orderedParticles.push_back(particles[index]);
            for (int index = 0; index < count; ++index)
                if (preview->layers[particles[index].Layer].Distortion != 0) orderedParticles.push_back(particles[index]);
            particles = orderedParticles.data();
        }
        FloatSceneTarget::Frame floatingScene(preview->floatingScene, preview->renderer->GetDevice(), preview->renderer->GetContext(),
            lighting ? lighting->Display : nullptr, lighting ? lighting->Bloom : nullptr);
        Matrix44 camera, lens;
        std::memcpy(camera.Values, view, 16 * sizeof(float)); std::memcpy(lens.Values, projection, 16 * sizeof(float));
        preview->renderer->SetCameraMatrix(camera); preview->renderer->SetProjectionMatrix(lens);
        if (softDepth || std::any_of(preview->layers.begin(), preview->layers.end(), [](const AecnLayer& layer) { return layer.SoftDistance > 0 || layer.NativeSoftDepth || layer.DistortionSoftDepth || layer.Kind == 2 || layer.MeshMaterialIndex >= 0; }))
            preview->CaptureDepth(projection);
        Backend::TextureRef sceneDepth;
        EffekseerRenderer::DepthReconstructionParameter reconstruction;
        preview->renderer->GetDepth(sceneDepth, reconstruction);
        Backend::TextureRef independentDepth;
        if (softDepth)
        {
            D3D11_SHADER_RESOURCE_VIEW_DESC description{}; softDepth->GetDesc(&description);
            if (description.ViewDimension != D3D11_SRV_DIMENSION_TEXTURE2D)
                throw std::runtime_error("Soft depth must be a single-sample Texture2D view");
            independentDepth = EffekseerRendererDX11::CreateTexture(preview->renderer->GetGraphicsDevice(), softDepth, nullptr, nullptr);
        }
        bool usingIndependentDepth = false;
        bool missingRequiredDepth = false;
        ComPtr<ID3D11RenderTargetView> colourTarget;
        preview->renderer->GetContext()->OMGetRenderTargets(1, colourTarget.GetAddressOf(), nullptr);
        if (!colourTarget) throw std::runtime_error("VFX draw requires a colour target");
        ComPtr<ID3D11Resource> colourResource; colourTarget->GetResource(colourResource.GetAddressOf());
        ComPtr<ID3D11Texture2D> colourTexture;
        if (FAILED(colourResource.As(&colourTexture))) throw std::runtime_error("Invalid VFX colour target");
        D3D11_TEXTURE2D_DESC colourDescription{}; colourTexture->GetDesc(&colourDescription);
        const float inverseWidth = 1.0f / colourDescription.Width, inverseHeight = 1.0f / colourDescription.Height;
        const auto inFront = [&](const AecnParticle& particle) {
            return particle.Transform[12] * view[2] + particle.Transform[13] * view[6]
                + particle.Transform[14] * view[10] + view[14] < 0;
        };
        bool rendering = false;
        const bool preparedSprites = lighting != nullptr && preview->uploadSprites != nullptr && preview->uploadEffect != nullptr;
        bool preparedActive = false;
        std::unique_ptr<ExtendedMeshStageState> extendedMeshState;
        ComPtr<ID3D11SamplerState> displacementSampler;
        auto flushPrepared = [&] {
            if (preparedActive) { preview->uploadRenderer->FlushCachedSprites(); preparedActive = false; }
        };
        auto beginRendering = [&] { preview->renderer->BeginRendering(); preparedActive = false; rendering = true; };
        auto endRendering = [&] { if (rendering) { flushPrepared(); preview->renderer->EndRendering(); rendering = false; } };
        std::unique_ptr<WeightedDistortionTarget::Frame> distortionFrame;
        int distortionBegin = 0, distortionEnd = 0, distortionPass = std::max(0, sharedDistortionPass);
        struct SpriteMaterialTemplate
        {
            NodeRendererBasicParameter Basic;
            MaterialRenderData Material;
            explicit SpriteMaterialTemplate(const AecnLayer& layer) : Basic(BuildMaterial(layer, 1))
            {
                Material.MaterialTextures.reserve(3);
                Material.MaterialUniforms.reserve(15);
            }
        };
        // Templates live only for this draw: lighting, camera and layer resources cannot become stale.
        std::vector<std::unique_ptr<SpriteMaterialTemplate>> spriteMaterials(preview->layers.size());
        MaterialRenderData materialScratch;
        ComPtr<ID3D11GeometryShader> geometryShader;
        preview->renderer->GetContext()->GSGetShader(geometryShader.GetAddressOf(), nullptr, nullptr);
        // File sprite shaders have no vertex displacement. Only affine views and an unextended raster pipeline qualify.
        bool canCullSprites = !geometryShader && view[3] == 0 && view[7] == 0 && view[11] == 0 && view[15] == 1
            && std::all_of(view, view + 16, [](float value) { return std::isfinite(value); })
            && std::all_of(projection, projection + 16, [](float value) { return std::isfinite(value); });
        Matrix44 spriteViewProjection;
        if (canCullSprites)
        {
            Matrix44::Mul(spriteViewProjection, camera, lens);
            // Opportunistically keep an overview intact; this sample can only disable culling, never reject a particle.
            canCullSprites = !SpriteOriginInsideXY(particles[0], spriteViewProjection)
                || !SpriteOriginInsideXY(particles[count / 2], spriteViewProjection)
                || !SpriteOriginInsideXY(particles[count - 1], spriteViewProjection);
        }
        beginRendering();
        try
        {
            int begin = 0;
            while (begin < count)
            {
                int layerIndex = particles[begin].Layer;
                if (layerIndex < 0 || layerIndex >= static_cast<int>(preview->layers.size())) throw std::runtime_error("Invalid particle layer");
                const auto& layer = preview->layers[layerIndex];
                if (layer.Kind != 0 || layer.MeshMaterialIndex >= 0 || layer.Distortion != 0 || layer.OpaqueModel) flushPrepared();
                if (layer.Distortion != 0 && !distortionFrame && sharedDistortionPass < 0)
                {
                    // Submit all colour before the one final distortion accumulation and resolve.
                    endRendering(); distortionBegin = begin; distortionEnd = begin;
                    while (distortionEnd < count)
                    {
                        const auto nextLayer = particles[distortionEnd].Layer;
                        if (nextLayer < 0 || nextLayer >= static_cast<int>(preview->layers.size())) throw std::runtime_error("Invalid particle layer");
                        if (preview->layers[nextLayer].Distortion == 0) break;
                        ++distortionEnd;
                    }
                    distortionFrame = std::make_unique<WeightedDistortionTarget::Frame>(preview->weightedDistortion,
                        preview->renderer->GetDevice(), preview->renderer->GetContext());
                    distortionPass = 0; beginRendering();
                }
                const bool useIndependentDepth = independentDepth != nullptr && layer.MeshMaterialIndex < 0
                    && (layer.Distortion != 0 || layer.Kind == 0);
                if (useIndependentDepth != usingIndependentDepth)
                {
                    // Flush the deferred batch before changing its depth input; receivers keep the raster-depth copy.
                    endRendering();
                    preview->renderer->SetDepth(useIndependentDepth ? independentDepth : sceneDepth, reconstruction);
                    usingIndependentDepth = useIndependentDepth;
                    beginRendering();
                }
                preview->depthAvailable = (useIndependentDepth ? independentDepth : sceneDepth) != nullptr;
                if (!preview->depthAvailable && (layer.SoftDistance > 0 || layer.NativeSoftDepth || layer.DistortionSoftDepth))
                    missingRequiredDepth = true;
                const bool precise = layer.MaterialMode == PreciseCoverageMode || layer.MaterialMode == PreciseEmissionMode;
                const auto mode = layer.MaterialMode == PreciseCoverageMode ? 1 : layer.MaterialMode == PreciseEmissionMode ? 2 : layer.MaterialMode;
                const bool normalFlips = layer.Kind == 0 && layer.Distortion == 0 && layer.MeshMaterialIndex < 0
                    && layer.LightingMode != 0 && layer.NormalIndex >= 0 && lighting->Parameters[0] != 0 && mode < 2;
                const bool rawAlpha = layer.MeshMaterialIndex < 0 && (layer.Distortion != 0
                    || ((layer.Kind == 0 || (layer.Kind == 2 && layer.MaterialMode < 3)) && layer.TextureIndex >= 0));
                const auto alphaRange = rawAlpha ? AlphaExponent(particles[begin].Colour[3]) : 0;
                const auto colourScale = precise ? ParticleBrightness(particles[begin]) : 0;
                const auto preciseAlphaScale = precise && rawAlpha
                    ? (alphaRange < 0 ? particles[begin].Colour[3] : std::max(1.0f, particles[begin].Colour[3])) : 1.0f;
                const auto& crush = particles[begin].AlphaCrush;
                if (!std::isfinite(crush[0]) || !std::isfinite(crush[1]) || crush[1] < 0 || crush[1] > 2)
                    throw std::runtime_error("Invalid particle alpha crush");
                int end = begin + 1;
                // A sprite renderer call silently drops instances beyond its fixed vertex buffer.
                while (end < count && end - begin < preview->renderer->GetSquareMaxCount() && particles[end].Layer == layerIndex
                    && (!layer.NativeSoftDepth || (particles[end].SoftParticleScale == particles[begin].SoftParticleScale
                        && inFront(particles[end]) == inFront(particles[begin])))
                    && particles[end].AlphaCrush[0] == crush[0] && particles[end].AlphaCrush[1] == crush[1]
                    && ((layer.Distortion == 0 && !normalFlips) || ((particles[end].UV[2] < 0) == (particles[begin].UV[2] < 0)
                        && (particles[end].UV[3] < 0) == (particles[begin].UV[3] < 0)))
                    && (!rawAlpha || AlphaExponent(particles[end].Colour[3]) == alphaRange)
                    && (!precise || (ParticleBrightness(particles[end]) == colourScale
                        && (!rawAlpha || (alphaRange < 0 ? particles[end].Colour[3]
                            : std::max(1.0f, particles[end].Colour[3])) == preciseAlphaScale)))) ++end;
                if (layer.Kind == 2) end = begin + 1;
                float brightness = layer.Distortion != 0 ? 1 : 0;
                for (int i = begin; i < end; ++i)
                    for (int c = 0; c < 4; ++c)
                    {
                        if (!std::isfinite(particles[i].Colour[c])) throw std::runtime_error("Invalid particle colour");
                        if (c < 3) brightness = std::max(brightness, particles[i].Colour[c]);
                    }
                // Normalize dark RGB before the byte vertex conversion, retaining its float scale.
                if (brightness <= 0) brightness = 1;
                // Retain faint float alpha rather than rounding the entire fade to a zero byte.
                float alphaScale = alphaRange < 0 ? 0 : 1;
                if (rawAlpha)
                    for (int i = begin; i < end; ++i) alphaScale = std::max(alphaScale, particles[i].Colour[3]);
                SpriteMaterialTemplate* spriteMaterial = nullptr;
                if (layer.Kind == 0 && layer.MeshMaterialIndex < 0 && layer.Distortion == 0 && !layer.OpaqueModel
                    && (layer.MaterialMode || layer.NativeSoftDepth || layer.GradientIndex >= 0 || layer.TextureIndex >= 0
                        || (layer.LightingMode && lighting->Parameters[0] != 0)))
                {
                    auto& cached = spriteMaterials[layerIndex];
                    if (!cached) cached = std::make_unique<SpriteMaterialTemplate>(layer);
                    spriteMaterial = cached.get();
                }
                auto basic = spriteMaterial ? spriteMaterial->Basic : BuildMaterial(layer, brightness);
                if (spriteMaterial) basic.EmissiveScaling = brightness;
                float softBias = 0;
                if (layer.NativeSoftDepth)
                {
                    if (!std::isfinite(particles[begin].SoftParticleScale)) throw std::runtime_error("Invalid particle soft depth scale");
                    using namespace DirectX::PackedVector;
                    const auto factor = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.SoftDistance));
                    const auto strength = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.DistortionSoftScaleStrength));
                    softBias = factor * (1 + strength * (std::max(0.0f, std::min(1.0f, particles[begin].SoftParticleScale)) - 1));
                    if (!std::isfinite(softBias) || softBias < 0) throw std::runtime_error("Invalid native soft depth distance");
                    basic.SoftParticleDistanceFar = softBias + 0.001f;
                }
                if (layer.OpaqueModel)
                {
                    basic.AlphaBlend = AlphaBlendType::Opacity;
                    basic.EmissiveScaling = 1;
                    basic.SoftParticleDistanceFar = 0;
                }
                basic.Flipbook.OneSize = {std::abs(particles[begin].UV[2]), std::abs(particles[begin].UV[3])};
                if (!preview->depthAvailable) basic.SoftParticleDistanceFar = 0;
                auto& material = materialScratch;
                const bool reuseSpriteMaterial = spriteMaterial && spriteMaterial->Material.MaterialIndex == 0;
                // The renderer also compares this pointer when batching, so always submit the same scratch address.
                if (reuseSpriteMaterial) material = spriteMaterial->Material;
                else
                {
                    material.MaterialIndex = -1;
                    material.MaterialTextures.clear();
                    material.MaterialUniforms.clear();
                    material.MaterialGradients.clear();
                }
                if (layer.MeshMaterialIndex >= 0)
                {
                    const auto& source = preview->meshMaterials[layer.MeshMaterialIndex];
                    material.MaterialIndex = source.rigidBasicDither ? 17 : source.vertexAnimated ? 16 : source.tidecall ? 15 : source.rigidDissolve ? 13 : source.growingVines ? 12 : source.soulPanning ? 11 : source.prelitSprite ? 10 : source.solid ? 9 : source.premultiplyAnimated ? 8 : source.basicSprite ? 7 : source.animatedUV ? 6 : source.gemstone ? 5 : source.basicMesh ? 4 : source.oneShot ? 3 : 2;
                    for (size_t index = 0; index < (source.tidecall ? 6 : source.textures.size()); ++index) material.MaterialTextures.push_back({0, source.textures[index]});
                    for (int index = 0; index < (source.rigidBasicDither ? 2 : source.tidecall || source.vertexAnimated ? 0 : source.rigidDissolve ? 15 : source.growingVines ? 3 : source.soulPanning ? 7 : source.prelitSprite ? 6 : source.solid ? 14 : source.premultiplyAnimated ? 8 : source.basicSprite ? 5 : source.animatedUV ? 9 : source.gemstone ? 10 : source.oneShot || source.basicMesh ? 14 : 13); ++index)
                    {
                        const auto offset = index * 4;
                        material.MaterialUniforms.push_back({source.parameters[offset], ((source.soulPanning && index == 4) || (!source.rigidBasicDither && !source.rigidDissolve && !source.oneShot && !source.basicMesh && index == 12)) && projection[11] == 0 ? 1.0f : source.parameters[offset + 1],
                            source.oneShot && index == 13 && projection[11] == 0 ? 1.0f : source.parameters[offset + 2],
                            source.basicMesh && index == 13 && projection[11] == 0 ? 1.0f : source.parameters[offset + 3]});
                    }
                    const auto front = preview->renderer->GetCameraFrontDirection();
                    if (source.tidecall || source.vertexAnimated)
                    {
                        endRendering();
                        auto context = preview->renderer->GetContext();
                        if (!extendedMeshState) extendedMeshState = std::make_unique<ExtendedMeshStageState>(context);
                        if (!displacementSampler)
                        {
                            D3D11_SAMPLER_DESC description{}; description.Filter = D3D11_FILTER_MIN_MAG_MIP_LINEAR;
                            description.AddressU = description.AddressV = description.AddressW = D3D11_TEXTURE_ADDRESS_WRAP;
                            description.MaxLOD = D3D11_FLOAT32_MAX; description.ComparisonFunc = D3D11_COMPARISON_NEVER;
                            if (FAILED(preview->renderer->GetDevice()->CreateSamplerState(&description, displacementSampler.GetAddressOf())))
                                throw std::runtime_error("Cannot create tidecall displacement sampler");
                        }
                        auto constants = source.extendedParameters.Get();
                        auto texture = EffekseerRendererDX11::GetTextureProperty(preview->textures[source.textures[source.tidecall ? 11 : 5]]->GetBackend()).ShaderResourceViewPtr;
                        auto sampler = displacementSampler.Get();
                        ID3D11ShaderResourceView* extraTextures[5]{};
                        for (int slot = 0; slot < 5; ++slot)
                            extraTextures[slot] = source.tidecall ? EffekseerRendererDX11::GetTextureProperty(preview->textures[source.textures[slot + 6]]->GetBackend()).ShaderResourceViewPtr : texture;
                        context->VSSetConstantBuffers(2, 1, &constants); context->PSSetConstantBuffers(2, 1, &constants);
                        context->VSSetShaderResources(0, 1, &texture); context->VSSetSamplers(0, 1, &sampler);
                        context->PSSetShaderResources(8, 5, extraTextures); context->PSSetSamplers(8, 1, &sampler);
                        beginRendering();
                        D3D11_VIEWPORT viewport{}; UINT viewportCount = 1; context->RSGetViewports(&viewportCount, &viewport);
                        material.MaterialUniforms.push_back({preview->depthAvailable ? 1.0f : 0.0f, viewport.Width, viewport.Height, 0});
                        material.MaterialUniforms.push_back({front.X, front.Y, front.Z, projection[11] == 0 ? 1.0f : 0.0f});
                    }
                    else if (source.rigidDissolve)
                    {
                        D3D11_VIEWPORT viewport{}; UINT viewportCount = 1;
                        preview->renderer->GetContext()->RSGetViewports(&viewportCount, &viewport);
                        material.MaterialUniforms.push_back({viewport.Width, viewport.Height, 0, 0});
                    }
                    else material.MaterialUniforms.push_back({preview->depthAvailable ? 1.0f : 0.0f, front.X, front.Y, front.Z});
                    basic.MaterialType = RendererMaterialType::File; basic.MaterialRenderDataPtr = &material;
                    basic.AlphaBlend = source.rigidBasicDither || source.tidecall || source.solid || source.growingVines || source.rigidDissolve ? AlphaBlendType::Opacity : AlphaBlendType::Blend; basic.SoftParticleDistanceFar = 1;
                    basic.TextureWraps.fill(TextureWrapType::Repeat);
                }
                else if (layer.Distortion != 0)
                {
                    const bool softDepth = layer.DistortionSoftDepth && preview->depthAvailable;
                    const auto fallback = layer.TextureIndex >= 0 ? layer.TextureIndex : layer.DistortionMaskIndex >= 0 ? layer.DistortionMaskIndex : layer.GradientIndex;
                    material.MaterialIndex = 14;
                    material.MaterialTextures.push_back({0, layer.TextureIndex >= 0 ? layer.TextureIndex : fallback});
                    material.MaterialTextures.push_back({0, layer.DistortionMaskIndex >= 0 ? layer.DistortionMaskIndex : fallback});
                    material.MaterialTextures.push_back({0, layer.GradientIndex >= 0 ? layer.GradientIndex : fallback});
                    material.MaterialUniforms.push_back({layer.SoftDistance, layer.DistortionMaskIndex >= 0 ? 1.0f : 0.0f,
                        layer.GradientIndex >= 0 ? 1.0f : 0.0f, static_cast<float>(layer.GradientAlphaMapping)});
                    material.MaterialUniforms.push_back({alphaScale, layer.Distortion, crush[0], crush[1]});
                    // Reflected camera sprites mirror local X positions; their normal-map tangent follows that reflection.
                    const float tangentSign = layer.CameraFacing && projection[0] * projection[5] - projection[1] * projection[4] < 0 ? -1.0f : 1.0f;
                    const float normalSource = layer.TextureIndex < 0 ? 0.0f : layer.Kind == 0 ? 2.0f : 1.0f;
                    material.MaterialUniforms.push_back({normalSource,
                        (particles[begin].UV[2] < 0 ? -1.0f : 1.0f) * tangentSign,
                        particles[begin].UV[3] < 0 ? -1.0f : 1.0f, static_cast<float>(distortionPass)});
                    material.MaterialUniforms.push_back({projection[10], projection[14], projection[11], projection[15]});
                    const auto nearDistance = std::abs(projection[14] / projection[10]);
                    const auto farDistance = std::abs((projection[15] - projection[14]) / (projection[10] - projection[11]));
                    material.MaterialUniforms.push_back({nearDistance, farDistance, layer.DistortionSoftScaleStrength, softDepth ? 1.0f : 0.0f});
                    material.MaterialUniforms.push_back({inverseWidth, inverseHeight, 0, 0});
                    basic.MaterialType = RendererMaterialType::File; basic.MaterialRenderDataPtr = &material;
                    basic.AlphaBlend = distortionPass == 0 ? AlphaBlendType::Add : AlphaBlendType::Mul;
                }
                else if (reuseSpriteMaterial)
                {
                    // Retain every original batch while refreshing all particle-dependent uniforms.
                    material.MaterialUniforms[0] = {brightness, basic.SoftParticleDistanceFar, static_cast<float>(mode), layer.NormalIndex >= 0 ? 1.0f : 0.0f};
                    material.MaterialUniforms[12] = {alphaScale, layer.EmissionShapeWeight, crush[0], crush[1]};
                    material.MaterialUniforms[13] = {particles[begin].UV[2] < 0 ? -1.0f : 1.0f,
                        particles[begin].UV[3] < 0 ? -1.0f : 1.0f, 0, 0};
                    const auto nearDistance = std::abs(projection[14] / projection[10]);
                    material.MaterialUniforms[14] = {inverseWidth, inverseHeight, softBias,
                        layer.NativeSoftDepth ? (inFront(particles[begin]) ? nearDistance : -nearDistance) : 0};
                    basic.MaterialType = RendererMaterialType::File; basic.MaterialRenderDataPtr = &material;
                }
                else if (!layer.OpaqueModel && (layer.MaterialMode || layer.NativeSoftDepth || layer.GradientIndex >= 0 || layer.Kind == 2 || (layer.Kind == 0 && layer.MeshMaterialIndex < 0 && layer.TextureIndex >= 0) || (layer.LightingMode && lighting->Parameters[0] != 0)))
                {
                    material.MaterialIndex = layer.Kind == 2 ? 1 : 0; material.MaterialTextures.push_back({0, layer.TextureIndex});
                    material.MaterialTextures.push_back({0, layer.NormalIndex >= 0 ? layer.NormalIndex : layer.TextureIndex});
                    material.MaterialTextures.push_back({0, layer.GradientIndex >= 0 ? layer.GradientIndex : layer.TextureIndex});
                    material.MaterialUniforms.push_back({brightness, layer.Kind == 2 ? 0 : basic.SoftParticleDistanceFar, static_cast<float>(mode), layer.NormalIndex >= 0 ? 1.0f : 0.0f});
                    if (layer.Kind != 2)
                    {
                        for (auto value : {lighting->PositiveX, lighting->NegativeX, lighting->PositiveY, lighting->NegativeY, lighting->PositiveZ, lighting->NegativeZ, lighting->SunDirection})
                            material.MaterialUniforms.push_back({value[0], value[1], value[2], value[3]});
                        const auto front = preview->renderer->GetCameraFrontDirection();
                        const auto cosine = front.X * lighting->SunDirection[0] + front.Y * lighting->SunDirection[1] + front.Z * lighting->SunDirection[2];
                        material.MaterialUniforms.push_back({lighting->SunColour[0], lighting->SunColour[1], lighting->SunColour[2], cosine});
                        material.MaterialUniforms.push_back({static_cast<float>(layer.LightingMode), layer.Emissive, layer.SmallParticles, layer.LargeParticles});
                        material.MaterialUniforms.push_back({layer.Directionality, layer.AverageSize, lighting->Parameters[0], lighting->Parameters[1]});
                    }
                    if (layer.Kind == 2)
                    {
                        const auto& particle = particles[begin];
                        Matrix44 model, inverseModel, viewProjection, inverseViewProjection, clipToLocal;
                        std::memcpy(model.Values, particle.Transform, sizeof(model.Values));
                        Matrix44::Inverse(inverseModel, model); Matrix44::Mul(viewProjection, camera, lens);
                        Matrix44::Inverse(inverseViewProjection, viewProjection); Matrix44::Mul(clipToLocal, inverseViewProjection, inverseModel);
                        const auto nearDistance = std::abs(projection[14] / projection[10]);
                        const auto farDistance = std::abs((projection[15] - projection[14]) / (projection[10] - projection[11]));
                        material.MaterialUniforms.push_back({preview->depthAvailable ? 1.0f : 0.0f, lighting->Parameters[2], static_cast<float>(layer.UvRotation), 0});
                        material.MaterialUniforms.push_back({projection[10], projection[14], projection[11], projection[15]});
                        material.MaterialUniforms.push_back({nearDistance, farDistance, 0, 0});
                        material.MaterialUniforms.push_back({particle.UV[0], particle.UV[1], particle.UV[2], particle.UV[3]});
                        material.MaterialUniforms.push_back({layer.Pivot[0], layer.Pivot[1], 0, 0});
                        for (const auto& row : clipToLocal.Values) material.MaterialUniforms.push_back({row[0], row[1], row[2], row[3]});
                        material.MaterialUniforms.push_back({model.Values[0][1], model.Values[1][1], model.Values[2][1], model.Values[3][1] - lighting->Parameters[3]});
                        // Pack world-coordinate columns for reconstructing the receiver's geometric normal.
                        for (int column = 0; column < 3; ++column)
                            material.MaterialUniforms.push_back({model.Values[0][column], model.Values[1][column], model.Values[2][column], model.Values[3][column]});
                        // Keep the actual depth texture bound without applying ordinary soft-particle fading.
                        basic.SoftParticleDistanceFar = 1;
                    }
                    material.MaterialUniforms.push_back({layer.GradientIndex >= 0 ? 1.0f : 0.0f,
                        static_cast<float>(layer.GradientAlphaMapping), layer.GradientScale, 0});
                    material.MaterialUniforms.push_back({alphaScale, layer.EmissionShapeWeight, crush[0], crush[1]});
                    material.MaterialUniforms.push_back({particles[begin].UV[2] < 0 ? -1.0f : 1.0f,
                        particles[begin].UV[3] < 0 ? -1.0f : 1.0f, 0, 0});
                    if (layer.Kind != 2)
                    {
                        const auto nearDistance = std::abs(projection[14] / projection[10]);
                        material.MaterialUniforms.push_back({inverseWidth, inverseHeight, softBias,
                            layer.NativeSoftDepth ? (inFront(particles[begin]) ? nearDistance : -nearDistance) : 0});
                    }
                    basic.MaterialType = RendererMaterialType::File; basic.MaterialRenderDataPtr = &material;
                }
                if (spriteMaterial && !reuseSpriteMaterial) spriteMaterial->Material = material;
                NodeRendererDepthParameter depth;
                if (layer.Kind == 0)
                {
                    if (canCullSprites && layer.MeshMaterialIndex < 0 && layer.Distortion == 0 && !layer.OpaqueModel
                        && basic.MaterialType == RendererMaterialType::File && material.MaterialIndex == 0)
                    {
                        bool outside = true;
                        for (int i = begin; i < end; ++i)
                            if (!SpriteOutsideXY(layer, particles[i], view, projection, spriteViewProjection)) { outside = false; break; }
                        if (outside) { begin = end; continue; }
                    }
                    const bool usePrepared = preparedSprites && layer.MeshMaterialIndex < 0 && layer.Distortion == 0
                        && !layer.OpaqueModel && basic.MaterialType == RendererMaterialType::File && material.MaterialIndex == 0;
                    if (!usePrepared) flushPrepared();
                    else if (!preparedActive)
                    {
                        preview->uploadRenderer->FlushOriginalSprites();
                        preview->uploadRenderer->BeginCachedSprites(); preparedActive = true;
                    }
                    const auto sprites = usePrepared ? preview->uploadSprites.Get() : preview->sprites.Get();
                    SpriteRenderer::NodeParameter node{};
                    node.EffectPointer = usePrepared ? preview->uploadEffect.Get() : preview->effect.Get(); node.ZTest = layer.ZTest != 0; node.ZWrite = false;
                    node.Billboard = BillboardType::Fixed; node.IsRightHand = true; node.ZSort = ZSortType::None;
                    node.DepthParameterPtr = &depth; node.BasicParameterPtr = &basic;
                    if (usePrepared) preview->uploadRenderer->SelectCachedSpriteLayer(static_cast<size_t>(layerIndex));
                    sprites->BeginRendering(node, end - begin, nullptr);
                    for (int i = begin; i < end; ++i)
                    {
                        const auto& particle = particles[i];
                        SpriteRenderer::InstanceParameter instance;
                        std::memset(&instance, 0, sizeof(instance));
                        instance.SRTMatrix43 = Transform(particle); instance.AllColor = Colour(particle, brightness, alphaScale);
                        for (int c = 0; c < 4; ++c) instance.Colors[c] = instance.AllColor;
                        instance.Positions[0] = {-layer.Pivot[0], -layer.Pivot[1]};
                        instance.Positions[1] = {1 - layer.Pivot[0], -layer.Pivot[1]};
                        instance.Positions[2] = {-layer.Pivot[0], 1 - layer.Pivot[1]};
                        instance.Positions[3] = {1 - layer.Pivot[0], 1 - layer.Pivot[1]};
                        // Permute the UV-carrying vertices without rotating the authored rectangle or its pivot.
                        const auto first = instance.Positions[0], second = instance.Positions[1];
                        const auto third = instance.Positions[2], fourth = instance.Positions[3];
                        if (layer.UvRotation == 1)
                        {
                            instance.Positions[0] = second; instance.Positions[1] = fourth;
                            instance.Positions[2] = first; instance.Positions[3] = third;
                        }
                        else if (layer.UvRotation == 2)
                        {
                            instance.Positions[0] = third; instance.Positions[1] = first;
                            instance.Positions[2] = fourth; instance.Positions[3] = second;
                        }
                        // Match the editor's reflected projection without reversing texture order or the authored pivot.
                        if (layer.CameraFacing && projection[0] * projection[5] - projection[1] * projection[4] < 0)
                            for (auto& position : instance.Positions) position.SetX(-position.GetX());
                        instance.UV = {particle.UV[0], particle.UV[1], particle.UV[2], particle.UV[3]};
                        instance.FlipbookIndexAndNextRate = particle.Flipbook;
                        instance.CustomData1 = CustomData(layer, particle);
                        std::copy_n(particle.NormalRotation, 4, instance.CustomData2.begin());
                        if (layer.Distortion != 0)
                        {
                            if (!std::isfinite(particle.ParticleSize))
                                throw std::runtime_error("Invalid distortion particle size");
                            // Velocity sprites carry the native shading basis independently of their projected geometry.
                            if (std::any_of(particle.NormalRotation, particle.NormalRotation + 4, [](float value) { return value != 0; }))
                            {
                                if (!std::all_of(particle.NormalRotation, particle.NormalRotation + 4, [](float value) { return std::isfinite(value); }))
                                    throw std::runtime_error("Invalid distortion shading basis");
                                std::copy_n(particle.NormalRotation, 4, instance.CustomData2.begin());
                            }
                            // Keep the fixed sprite basis before byte packing when no independent payload was provided.
                            else if (layer.TextureIndex >= 0)
                            {
                                const auto& matrix = instance.SRTMatrix43;
                                const auto tangent = SIMD::Vec3f(matrix.X.GetX(), matrix.Y.GetX(), matrix.Z.GetX()).Normalize();
                                const auto normal = SIMD::Vec3f(matrix.X.GetZ(), matrix.Y.GetZ(), matrix.Z.GetZ()).Normalize();
                                const auto binormal = SIMD::Vec3f::Cross(normal, tangent);
                                instance.CustomData2 = {tangent.GetX(), tangent.GetY(), binormal.GetX(), binormal.GetY()};
                            }
                            else
                            {
                                const auto* basis = particle.Transform + 8;
                                const auto length = std::sqrt(basis[0] * basis[0] + basis[1] * basis[1] + basis[2] * basis[2]);
                                const auto divisor = std::max(length, 0.00000001f);
                                instance.CustomData2 = {basis[0] / divisor, basis[1] / divisor, basis[2] / divisor, 1};
                            }
                        }
                        if (layer.MeshMaterialIndex >= 0)
                        {
                            instance.AllColor = Color(255,255,255,255);
                            for (int c = 0; c < 4; ++c) instance.Colors[c] = instance.AllColor;
                            instance.CustomData1 = {particle.MaterialTime, particle.AppearancePhase, particle.ParticleSize, 0};
                            std::copy_n(particle.Colour, 4, instance.CustomData2.begin());
                        }
                        sprites->Rendering(node, instance, nullptr);
                    }
                    sprites->EndRendering(node, nullptr);
                }
                else
                {
                    ModelRenderer::NodeParameter node{};
                    node.EffectPointer = preview->effect.Get(); node.ZTest = layer.Kind == 2 ? false : layer.ZTest != 0;
                    node.ZWrite = layer.OpaqueModel || (layer.MeshMaterialIndex >= 0 && preview->meshMaterials[layer.MeshMaterialIndex].gemstone);
                    node.Billboard = BillboardType::Fixed; node.IsRightHand = true;
                    // The editor mirrors projected X; keep the volume's interior faces when the camera enters it.
                    const bool mirroredProjection = projection[0] * projection[5] - projection[1] * projection[4] < 0;
                    // These shipped shaders cull back faces before transparent composition.
                    const bool singleSided = layer.MeshMaterialIndex >= 0
                        && (preview->meshMaterials[layer.MeshMaterialIndex].vertexAnimated || preview->meshMaterials[layer.MeshMaterialIndex].tidecall || preview->meshMaterials[layer.MeshMaterialIndex].soulPanning || preview->meshMaterials[layer.MeshMaterialIndex].scrollingMesh || preview->meshMaterials[layer.MeshMaterialIndex].growingVines || preview->meshMaterials[layer.MeshMaterialIndex].rigidDissolve || preview->meshMaterials[layer.MeshMaterialIndex].rigidBasicDither);
                    node.Culling = layer.Kind == 2 ? (mirroredProjection ? CullingType::Back : CullingType::Front)
                        : singleSided ? (mirroredProjection ? CullingType::Front : CullingType::Back) : CullingType::Double;
                    node.ModelIndex = layer.ModelIndex; node.Magnification = 1;
                    node.DepthParameterPtr = &depth; node.BasicParameterPtr = &basic;
                    preview->meshes->BeginRendering(node, end - begin, nullptr);
                    for (int i = begin; i < end; ++i)
                    {
                        const auto& particle = particles[i];
                        ModelRenderer::InstanceParameter instance{};
                        instance.SRTMatrix43 = Transform(particle);
                        instance.AllColor = layer.MeshMaterialIndex >= 0 || layer.OpaqueModel ? Color(255,255,255,255) : Colour(particle, brightness, alphaScale);
                        instance.UV = {particle.UV[0], particle.UV[1], particle.UV[2], particle.UV[3]};
                        const auto cycles = preview->modelAnimationCycles[layer.ModelIndex];
                        instance.Time = cycles > 0 ? static_cast<int>(std::floor(std::max(0.0f, particle.AppearancePhase) * cycles
                            * preview->models[layer.ModelIndex]->GetFrameCount())) : 0;
                        instance.FlipbookIndexAndNextRate = particle.Flipbook;
                        instance.CustomData1 = CustomData(layer, particle);
                        if (layer.Kind == 2 && layer.MaterialMode < 3)
                            std::copy_n(particle.Colour, 4, instance.CustomData2.begin());
                        if (layer.MeshMaterialIndex >= 0)
                        {
                            instance.CustomData1 = {particle.MaterialTime, particle.AppearancePhase, particle.Time, 0};
                            std::copy_n(particle.Colour, 4, instance.CustomData2.begin());
                        }
                        preview->meshes->Rendering(node, instance, nullptr);
                    }
                    preview->meshes->EndRendering(node, nullptr);
                }
                begin = end;
                if (distortionFrame && begin == distortionEnd)
                {
                    endRendering();
                    if (distortionPass == 0)
                    {
                        distortionFrame->BindTransmittance(); distortionPass = 1; begin = distortionBegin;
                    }
                    else
                    {
                        distortionFrame->Finish(); distortionFrame.reset();
                    }
                    beginRendering();
                }
            }
        }
        catch (...) { endRendering(); distortionFrame.reset(); throw; }
        endRendering();
        preview->depthAvailable = !missingRequiredDepth && (sceneDepth != nullptr || independentDepth != nullptr);
        floatingScene.Finish();
#ifdef _DEBUG
        if (std::getenv("ASSETEDITOR_VFX_HEADLESS_TEST") != nullptr)
            std::fprintf(stderr, "Effekseer draw calls: %d; particles: %d\n", preview->renderer->GetDrawCallCount(), count);
#endif
    });
}

API int AecnVfxDrawInScene(Preview* owner, Preview* preview, const float* view, const float* projection,
    const AecnParticle* particles, int count, const AecnLighting* lighting, ID3D11ShaderResourceView* softDepth);

API int AecnVfxDraw(Preview* preview, const float* view, const float* projection, const AecnParticle* particles, int count,
    const AecnLighting* lighting, ID3D11ShaderResourceView* softDepth)
{
    if (preview && (!preview->halfLayers.empty() || !preview->fullLayers.empty()))
    {
        if (!AecnVfxBeginScene(preview, lighting)) return 0;
        if (!AecnVfxDrawInScene(preview, preview, view, projection, particles, count, lighting, softDepth))
        {
            auto draws = std::move(preview->sceneDraws);
            preview->sceneFrame.reset();
            return 0;
        }
        return AecnVfxEndScene(preview);
    }
    return DrawParticles(preview, view, projection, particles, count, lighting, softDepth, -1);
}

API int AecnVfxDrawInScene(Preview* owner, Preview* preview, const float* view, const float* projection,
    const AecnParticle* particles, int count, const AecnLighting* lighting, ID3D11ShaderResourceView* softDepth)
{
    return Guard([&]
    {
        if (!owner || !preview || !owner->sceneFrame || count < 0 || count > 1048576
            || owner->renderer->GetContext() != preview->renderer->GetContext())
            throw std::runtime_error("Invalid VFX scene submission");
        if (count == 0) return;
        if (!view || !projection || !particles || !lighting) throw std::runtime_error("Missing VFX scene data");
        if (softDepth)
        {
            D3D11_SHADER_RESOURCE_VIEW_DESC description{}; softDepth->GetDesc(&description);
            if (description.ViewDimension != D3D11_SRV_DIMENSION_TEXTURE2D)
                throw std::runtime_error("Soft depth must be a single-sample Texture2D view");
        }
        // Validate each submission before retaining it so a rejected effect cannot poison the scene queue.
        size_t opaqueCount = 0, colourCount = 0, distortionCount = 0;
        for (int index = 0; index < count; ++index)
        {
            const auto& particle = particles[index];
            if (particle.Layer < 0 || particle.Layer >= static_cast<int>(preview->layers.size()))
                throw std::runtime_error("Invalid particle layer");
            const auto& layer = preview->layers[particle.Layer];
            for (float value : particle.Colour)
                if (!std::isfinite(value)) throw std::runtime_error("Invalid particle colour");
            if (HalfLayer(*preview, particle.Layer))
                for (float value : particle.Transform)
                    if (!std::isfinite(value)) throw std::runtime_error("Invalid particle transform");
            if (!std::isfinite(particle.AlphaCrush[0]) || !std::isfinite(particle.AlphaCrush[1])
                || particle.AlphaCrush[1] < 0 || particle.AlphaCrush[1] > 2)
                throw std::runtime_error("Invalid particle alpha crush");
            if (layer.NativeSoftDepth)
            {
                if (!std::isfinite(particle.SoftParticleScale)) throw std::runtime_error("Invalid particle soft depth scale");
                using namespace DirectX::PackedVector;
                const auto factor = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.SoftDistance));
                const auto strength = XMConvertHalfToFloat(XMConvertFloatToHalf(layer.DistortionSoftScaleStrength));
                const auto bias = factor * (1 + strength * (std::max(0.0f, std::min(1.0f, particle.SoftParticleScale)) - 1));
                if (!std::isfinite(bias) || bias < 0) throw std::runtime_error("Invalid native soft depth distance");
            }
            if (layer.Distortion != 0)
            {
                if (!std::isfinite(particle.ParticleSize)) throw std::runtime_error("Invalid distortion particle size");
                for (float value : particle.NormalRotation)
                    if (!std::isfinite(value)) throw std::runtime_error("Invalid distortion shading basis");
            }
            if (layer.Distortion != 0) ++distortionCount;
            else if (layer.OpaqueModel) ++opaqueCount;
            else ++colourCount;
        }
        preview->Retain();
        SceneDraw command;
        if (!owner->recycledSceneDraws.empty())
        {
            command = std::move(owner->recycledSceneDraws.back());
            owner->recycledSceneDraws.pop_back();
        }
        command.preview = std::shared_ptr<Preview>(preview, [](Preview* value) { value->Release(); });
        std::copy_n(view, 16, command.view.begin()); std::copy_n(projection, 16, command.projection.begin());
        command.lighting = *lighting; command.softDepth = softDepth;
        command.opaque.reserve(opaqueCount);
        command.colour.reserve(colourCount);
        command.distortion.reserve(distortionCount);
        for (int index = 0; index < count; ++index)
        {
            const auto& particle = particles[index]; const auto& layer = preview->layers[particle.Layer];
            auto& target = layer.Distortion != 0 ? command.distortion : layer.OpaqueModel ? command.opaque : command.colour;
            target.push_back(particle);
        }
        owner->sceneDraws.push_back(std::move(command));
        ComPtr<ID3D11DepthStencilView> depth;
        preview->renderer->GetContext()->OMGetRenderTargets(0, nullptr, depth.GetAddressOf());
        preview->depthAvailable = depth != nullptr || softDepth != nullptr;
    });
}
