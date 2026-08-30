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

            var sampleTicks = (long)((decimal)Duration.Ticks * frameIndex / FrameCount);
            return TimeSpan.FromTicks(sampleTicks);
        }
    }
}
