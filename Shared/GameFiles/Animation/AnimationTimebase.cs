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
