#pragma once
#include <Effekseer/Backend/GraphicsDevice.h>

// Preserve the same pose samples without uploading every deformed vertex in advance.
class CompactAnimatedModel final : public Effekseer::Model
{
    static constexpr int CachedFrames = 4;
    std::vector<AecnVertex> source_;
    std::vector<AecnSkinWeight> weights_;
    std::vector<float> poses_;
    Effekseer::CustomVector<Face> faces_;
    Effekseer::RefPtr<Effekseer::Backend::IndexBuffer> indexBuffer_;
    int boneCount_;
    Effekseer::Vector3D offset_;
    std::array<int, CachedFrames> cached_{{-1, -1, -1, -1}};
    std::array<uint64_t, CachedFrames> used_{};
    uint64_t clock_ = 0;

    static Effekseer::Vector3D Transform(const Effekseer::Vector3D& value, const float* matrix, bool position)
    {
        return {value.X * matrix[0] + value.Y * matrix[4] + value.Z * matrix[8] + (position ? matrix[12] : 0),
            value.X * matrix[1] + value.Y * matrix[5] + value.Z * matrix[9] + (position ? matrix[13] : 0),
            value.X * matrix[2] + value.Y * matrix[6] + value.Z * matrix[10] + (position ? matrix[14] : 0)};
    }

    static Effekseer::Vector3D Unit(Effekseer::Vector3D value)
    {
        const auto squared = value.X * value.X + value.Y * value.Y + value.Z * value.Z;
        if (!std::isfinite(squared) || squared <= 0.00000001f) throw std::runtime_error("Invalid animated normal");
        const auto reciprocal = 1.0f / std::sqrt(squared);
        return {value.X * reciprocal, value.Y * reciprocal, value.Z * reciprocal};
    }

public:
    CompactAnimatedModel(const AecnVertex* vertices, int vertexCount, const int32_t* indices, int indexCount,
        const AecnSkinWeight* weights, const float* poses, int poseCount, int bones, int frames, Effekseer::Vector3D offset)
        : Model(Effekseer::CustomVector<Vertex>{}, Effekseer::CustomVector<Face>{}),
          source_(vertices, vertices + vertexCount), weights_(weights, weights + vertexCount),
          poses_(poses, poses + static_cast<size_t>(poseCount) * 16), boneCount_(bones), offset_(offset)
    {
        static_assert(sizeof(InternalModel) <= 128, "Update the compact-model budget for this dependency");
        models_.clear(); models_.resize(frames);
        faces_.resize(indexCount / 3);
        for (int index = 0; index < indexCount; ++index) faces_[index / 3].Indexes[index % 3] = indices[index];
    }

    void PrepareFrame(Effekseer::Backend::GraphicsDevice* device, int requested)
    {
        const int frame = requested % GetFrameCount();
        for (int slot = 0; slot < CachedFrames; ++slot)
            if (cached_[slot] == frame) { used_[slot] = ++clock_; return; }
        int slot = 0;
        for (int candidate = 0; candidate < CachedFrames; ++candidate)
            if (cached_[candidate] == -1) { slot = candidate; break; }
            else if (used_[candidate] < used_[slot]) slot = candidate;
        const auto evicted = cached_[slot];
        // Invalidate before releasing the old pose; publish the replacement only after a complete build.
        cached_[slot] = -1; used_[slot] = 0;
        if (evicted >= 0) models_[evicted] = InternalModel{};
        InternalModel model;
        model.vertexes.resize(source_.size()); model.faces = faces_;
        for (size_t index = 0; index < source_.size(); ++index)
        {
            const auto& source = source_[index]; const auto& skin = weights_[index];
            float transform[16]{};
            for (int weight = 0; weight < 4; ++weight)
                if (skin.Weights[weight] > 0)
                {
                    const auto matrix = poses_.data() + (static_cast<size_t>(frame) * boneCount_ + skin.Bones[weight]) * 16;
                    for (int element = 0; element < 16; ++element) transform[element] += matrix[element] * skin.Weights[weight];
                }
            auto& vertex = model.vertexes[index];
            vertex.Position = Transform({source.Position[0], source.Position[1], source.Position[2]}, transform, true);
            vertex.Position += offset_;
            vertex.Normal = Unit(Transform({source.Normal[0], source.Normal[1], source.Normal[2]}, transform, false));
            vertex.Tangent = Unit(Transform({source.Tangent[0], source.Tangent[1], source.Tangent[2]}, transform, false));
            vertex.Binormal = Unit(Transform({source.Binormal[0], source.Binormal[1], source.Binormal[2]}, transform, false));
            vertex.UV = {source.UV[0], source.UV[1]};
            const auto channel = [](float value) { return static_cast<uint8_t>(std::lround(std::max(0.0f, std::min(1.0f, value)) * 255)); };
            vertex.VColor = {channel(source.Colour[0]), channel(source.Colour[1]), channel(source.Colour[2]), channel(source.Colour[3])};
        }
        if (indexBuffer_ == nullptr)
            indexBuffer_ = device->CreateIndexBuffer(static_cast<int32_t>(faces_.size() * 3), faces_.data(), Effekseer::Backend::IndexBufferStrideType::Stride4);
        model.indexBuffer = indexBuffer_;
        model.vertexBuffer = device->CreateVertexBuffer(static_cast<int32_t>(model.vertexes.size() * sizeof(Vertex)), model.vertexes.data(), false);
        if (model.indexBuffer == nullptr || model.vertexBuffer == nullptr) throw std::runtime_error("Cannot prepare compact model pose");
        models_[frame] = std::move(model);
        cached_[slot] = frame; used_[slot] = ++clock_;
        isBufferStoredOnGPU_ = true;
    }
};
