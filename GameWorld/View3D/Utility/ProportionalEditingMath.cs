namespace GameWorld.Core.Utility
{
    internal static class ProportionalEditingMath
    {
        const float WeightBandCount = 256.0f;

        public static float CalculateLinearWeight(
            float distanceSquared,
            float falloffDistance)
        {
            if (falloffDistance <= 0.0f ||
                distanceSquared >= falloffDistance * falloffDistance)
            {
                return 0.0f;
            }

            var weight = 1.0f - MathF.Sqrt(distanceSquared) / falloffDistance;
            return MathF.Round(weight * WeightBandCount) / WeightBandCount;
        }
    }
}
