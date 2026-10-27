#nullable enable
using System.Reflection;
using System.Text.Json;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxSpriteNativeSpinDampingTests
{
    public static IEnumerable<TestCaseData> NativeCases()
    {
        using var document = Fixture();
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName(item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void BakedHalfRows_MatchActualOfficialInstructions(JsonElement item)
    {
        var curve = VfxSpriteNativeSpinDamping.Create(item.GetProperty("Source").GetString()!);
        var expected = Values(item);
        for (var index = 0; index < 128; ++index)
            Assert.That(curve.BoundAt(index), Is.EqualTo(expected[index]), $"native half row texel {index}");
        Assert.That(curve.Minimum, Is.EqualTo(expected.Min()));
        Assert.That(curve.Maximum, Is.EqualTo(expected.Max()));
    }

    [TestCaseSource(nameof(NativeCases))]
    public void Sampling_UsesIndependentNormalizedLinearClampGeometry(JsonElement item)
    {
        var curve = VfxSpriteNativeSpinDamping.Create(item.GetProperty("Source").GetString()!);
        var values = Values(item);
        double[] phases = [-1, 0, 1.0 / 256, .017, .1, .3, .501, .999, 1, 2];
        foreach (var phase in phases)
            Assert.That(curve.Sample(phase), Is.EqualTo((float)Ideal(values, phase)), $"phase {phase}");
        Assert.That(curve.Sample(1), Is.EqualTo(values[127]), "The native i/128 upload does not contain the authored endpoint at t=1.");
    }

    [Test]
    public void PortableSampling_RemainsWithinTheRecordedHardwareFilteringErrorBound()
    {
        using var document = Fixture();
        foreach (var gpuCase in document.RootElement.GetProperty("HardwareComparison").GetProperty("Cases").EnumerateArray())
        {
            var item = document.RootElement.GetProperty("Cases").EnumerateArray()
                .Single(candidate => candidate.GetProperty("Name").GetString() == gpuCase.GetProperty("Name").GetString());
            var curve = VfxSpriteNativeSpinDamping.Create(item.GetProperty("Source").GetString()!);
            var values = Values(item);
            foreach (var sample in gpuCase.GetProperty("Samples").EnumerateArray())
            {
                var phase = BitConverter.UInt32BitsToSingle(sample.GetProperty("PhaseBits").GetUInt32());
                var actualGpu = BitConverter.UInt32BitsToSingle(sample.GetProperty("GpuValueBits").GetUInt32());
                var coordinate = Math.Clamp(phase, 0, 1) * 128.0 - .5;
                var lower = (int)Math.Floor(coordinate);
                var difference = Math.Abs(values[Math.Clamp(lower + 1, 0, 127)] - values[Math.Clamp(lower, 0, 127)]);
                var ideal = Ideal(values, phase);
                // This adapter rounds weights to 8 fractional bits and its result to half.
                // Both are recorded observations, not a cross-adapter numerical contract.
                var halfUnit = Math.ScaleB(1.0, Math.Max(-14, Math.ILogB(Math.Abs(ideal))) - 10);
                var bound = difference / 512.0 + halfUnit / 2;
                Assert.That(Math.Abs(curve.Sample(phase) - actualGpu), Is.LessThanOrEqualTo(bound), $"recorded GPU phase {phase}");
            }
        }
    }

    [TestCase("x_min,0,0;0,-1,linear,linear,0,0,0,0,1;1,6,linear,linear,0,0,0,0,1:x,0;0,0", "Vfx.Preview.AmbiguousField")]
    [TestCase("x_min,0,0;0,-1,linear,linear,0,0,0,0,1|x_max,0,0;0,6,linear,linear,0,0,0,0,1:x,0;0,1", "Vfx.Preview.AmbiguousField")]
    [TestCase("x_min,0,0;0,1e20,linear,linear,0,0,0,0,1|x_max,0,0;0,1e20,linear,linear,0,0,0,0,1:x,0;0,1", "Vfx.Preview.RangeUnsupported")]
    public void UnprovedLayoutsAndHalfOverflow_KeepExplicitDiagnostics(string source, string reason)
    {
        var error = Assert.Throws<VfxMappingException>(() => VfxSpriteNativeSpinDamping.Create(source))!;
        Assert.That(error.Field, Is.EqualTo("ParticleSpinDeceleration"));
        Assert.That(error.Reason, Is.EqualTo(reason));
    }

    [TestCase("bezier_c")]
    [TestCase("step")]
    public void UnsupportedSegments_AreNotOpenedByTheNativeHalfTable(string mode)
    {
        var points = $";0,-1,linear,{mode},0,0,.3,1,1;1,6,{mode},linear,-.3,-1,0,0,1";
        var error = Assert.Throws<VfxMappingException>(() => VfxSpriteNativeSpinDamping.Create($"x_min,0,0{points}|x_max,0,0{points}:x,0;0,1"))!;
        Assert.That(error.Reason, Is.EqualTo("Vfx.Preview.InterpolationUnsupported"));
    }

    private static float[] Values(JsonElement item) => item.GetProperty("HalfBits").EnumerateArray()
        .Select(value => (float)BitConverter.UInt16BitsToHalf(value.GetUInt16())).ToArray();

    private static double Ideal(float[] values, double phase)
    {
        // Independent weighted-sum oracle, using the native normalized coordinate convention.
        var x = Math.Clamp((float)phase, 0, 1) * 128.0 - .5;
        var index = (int)Math.Floor(x);
        var fraction = x - index;
        return (1 - fraction) * values[Math.Clamp(index, 0, 127)] + fraction * values[Math.Clamp(index + 1, 0, 127)];
    }

    private static JsonDocument Fixture()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".NativeSpriteDynamicScalar.json", StringComparison.Ordinal)))!;
        return JsonDocument.Parse(stream);
    }
}
