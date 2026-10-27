#pragma once
#include <cstdint>

constexpr int PreciseCoverageMode = 5, PreciseEmissionMode = 6;

// Game simulation and the source document remain managed; this ABI only carries render state.
struct AecnLayer
{
    int32_t Kind, Blend, ZTest, TextureIndex, NormalIndex, ModelIndex;
    float SoftDistance, Distortion;
    float Pivot[2];
    int32_t AtlasColumns, AtlasRows, AtlasLoop, AtlasBlend;
    int32_t MaterialMode, UvRotation;
    int32_t LightingMode;
    float Emissive, SmallParticles, LargeParticles, Directionality, AverageSize;
    int32_t MeshMaterialIndex;
    int32_t GradientIndex, GradientAlphaMapping;
    float GradientScale;
    int32_t CameraFacing, OpaqueModel;
    float EmissionShapeWeight;
    int32_t DistortionMaskIndex;
    float DistortionSoftScaleStrength;
    int32_t DistortionSoftDepth;
    int32_t NativeSoftDepth;
};

struct AecnParticle
{
    int32_t Layer;
    float Transform[16], Colour[4], UV[4], Time, Flipbook;
    float NormalRotation[4], ParticleSize, MaterialTime, AppearancePhase;
    float AlphaCrush[2];
    float SoftParticleScale;
};

// Original particles remain unsplit; legacy layer IDs permit an exact fallback.
struct AecnHalfLayerConfig
{
    int32_t LayerIndex, LegacyCoverageIndex, LegacyEmissionIndex, Premultiplied;
    float CoveredColourMultiplier, EmissionMultiplier;
};
static_assert(sizeof(AecnHalfLayerConfig) == 24, "Invalid half-resolution layer ABI");

struct AecnLighting
{
    float PositiveX[4], NegativeX[4], PositiveY[4], NegativeY[4], PositiveZ[4], NegativeZ[4];
    float SunDirection[4], SunColour[4], Parameters[4];
    float Display[4];
    float Bloom[4];
};

struct AecnVertex
{
    float Position[3], Normal[3], Tangent[3], Binormal[3], UV[2], Colour[4];
};
