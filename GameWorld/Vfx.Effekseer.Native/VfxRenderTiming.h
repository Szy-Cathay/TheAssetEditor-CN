#pragma once
#include <array>
#ifndef AECN_VFX_NATIVE_TIMINGS
#define AECN_VFX_NATIVE_TIMINGS 0
#endif
#if AECN_VFX_NATIVE_TIMINGS
#include <chrono>
#endif

namespace AecnRenderTiming
{
enum Phase { NativeDraw, Particles, FloatingScene, Depth, Sprites, Models, RendererEnd, BulkGpu, BulkMap, BulkConstants, BulkDraw, HalfScene, BulkStateCopy, SpriteCull, SpriteBegin, SpriteInstance, SpriteVertex, SpriteEnd, PhaseCount };
constexpr bool Available = AECN_VFX_NATIVE_TIMINGS != 0;
struct Stats
{
    bool Enabled = false;
    std::array<double, PhaseCount * 2> Values{};
};
#if AECN_VFX_NATIVE_TIMINGS
static thread_local Stats* Active = nullptr;
class Activate
{
    Stats* previous_;
public:
    explicit Activate(Stats& stats) : previous_(Active) { Active = stats.Enabled ? &stats : nullptr; }
    ~Activate() { Active = previous_; }
};
class Scope
{
    Stats* stats_;
    int phase_;
    std::chrono::steady_clock::time_point start_;
public:
    explicit Scope(Phase phase) : stats_(Active), phase_(phase)
    { if (stats_) start_ = std::chrono::steady_clock::now(); }
    void Finish()
    {
        if (!stats_) return;
        stats_->Values[phase_ * 2] += std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start_).count();
        stats_->Values[phase_ * 2 + 1] += 1;
        stats_ = nullptr;
    }
    ~Scope() { Finish(); }
};
#endif
}

#if AECN_VFX_NATIVE_TIMINGS
#define AECN_VFX_TIMING_SCOPE(name, phase) AecnRenderTiming::Scope name(phase)
#define AECN_VFX_TIMING_FINISH(name) name.Finish()
#define AECN_VFX_TIMING_ACTIVATE(name, stats) AecnRenderTiming::Activate name(stats)
#else
// Erase declarations and calls; an empty class still costs calls under Debug /Ob0.
#define AECN_VFX_TIMING_SCOPE(name, phase) ((void)0)
#define AECN_VFX_TIMING_FINISH(name) ((void)0)
#define AECN_VFX_TIMING_ACTIVATE(name, stats) ((void)0)
#endif