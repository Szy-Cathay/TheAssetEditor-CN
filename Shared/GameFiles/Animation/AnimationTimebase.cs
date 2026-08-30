namespace Shared.GameFormats.Animation
{
    public sealed class AnimationTimebase
    {
        public AnimationTimebase(int frameCount, TimeSpan duration)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (duration <= TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));

            FrameCount = frameCount;
            Duration = duration;
        }

        public int FrameCount { get; }

        public TimeSpan Duration { get; }

        public double FramesPerSecond => FrameCount / Duration.TotalSeconds;

        public TimeSpan SampleDuration => TimeSpan.FromTicks(
            Math.Max(1, Duration.Ticks / FrameCount));

        public static AnimationTimebase FromFramesPerSecond(
            int frameCount,
            double framesPerSecond)
        {
            if (frameCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(frameCount));
            if (!double.IsFinite(framesPerSecond) || framesPerSecond <= 0)
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));

            var durationTicks = frameCount *
                (double)TimeSpan.TicksPerSecond / framesPerSecond;
            if (!double.IsFinite(durationTicks) ||
                durationTicks < 1 ||
                durationTicks > TimeSpan.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
            }

            return new AnimationTimebase(
                frameCount,
                TimeSpan.FromTicks((long)Math.Round(
                    durationTicks,
                    MidpointRounding.AwayFromZero)));
        }

        public AnimationTimebase WithPlaybackSpeed(double playbackSpeed)
        {
            if (!double.IsFinite(playbackSpeed) || playbackSpeed <= 0)
                throw new ArgumentOutOfRangeException(nameof(playbackSpeed));

            var scaledTicks = Duration.Ticks / playbackSpeed;
            if (!double.IsFinite(scaledTicks) ||
                scaledTicks < 1 ||
                scaledTicks > TimeSpan.MaxValue.Ticks)
            {
                throw new ArgumentOutOfRangeException(nameof(playbackSpeed));
            }

            var scaledDuration = TimeSpan.FromTicks((long)Math.Round(
                scaledTicks,
                MidpointRounding.AwayFromZero));
            var scaledFrameCount = (decimal)FrameCount *
                scaledDuration.Ticks / Duration.Ticks;
            if (scaledFrameCount > int.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(playbackSpeed));

            return new AnimationTimebase(
                Math.Max(1, (int)Math.Round(
                    scaledFrameCount,
                    MidpointRounding.AwayFromZero)),
                scaledDuration);
        }

        public TimeSpan GetSampleTime(int frameIndex)
        {
            if (frameIndex < 0 || frameIndex >= FrameCount)
                throw new ArgumentOutOfRangeException(nameof(frameIndex));

            return GetSampleTime((double)frameIndex);
        }

        public TimeSpan GetSampleTime(double samplePosition)
        {
            if (double.IsFinite(samplePosition) == false ||
                samplePosition < 0 ||
                samplePosition >= FrameCount)
            {
                throw new ArgumentOutOfRangeException(nameof(samplePosition));
            }

            var sampleTicks = (long)((decimal)Duration.Ticks * (decimal)samplePosition / FrameCount);
            return TimeSpan.FromTicks(sampleTicks);
        }

        public double GetSamplePosition(TimeSpan playheadTime)
        {
            if (playheadTime < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(playheadTime));
            if (FrameCount == 1)
                return 0;
            if (playheadTime >= Duration)
                return FrameCount - 1;

            var frameIndexUpperBoundExclusive =
                (decimal)(playheadTime.Ticks + 1) * FrameCount / Duration.Ticks;
            var frameIndex = Math.Clamp(
                (int)Math.Ceiling(frameIndexUpperBoundExclusive) - 1,
                0,
                FrameCount - 1);
            if (frameIndex == FrameCount - 1)
                return frameIndex;

            var frameStartTicks = GetSampleTime(frameIndex).Ticks;
            var nextFrameStartTicks = GetSampleTime(frameIndex + 1).Ticks;
            if (nextFrameStartTicks == frameStartTicks)
                return frameIndex;

            return frameIndex +
                (double)(playheadTime.Ticks - frameStartTicks) /
                (nextFrameStartTicks - frameStartTicks);
        }
    }
}
