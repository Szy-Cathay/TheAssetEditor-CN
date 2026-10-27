#nullable enable
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxSpriteConstantSpinDampingTests
{
    public static IEnumerable<TestCaseData> NativeHalfCases()
    {
        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream(assembly.GetManifestResourceNames()
            .Single(name => name.EndsWith(".NativeSpriteHalf.json", StringComparison.Ordinal)))!;
        using var document = JsonDocument.Parse(stream);
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.GetProperty("InputBits").GetUInt32(), item.GetProperty("ExpectedHalfBits").GetUInt16())
                .SetName("NativeSpriteHalf_" + item.GetProperty("Label").GetString());
    }

    [TestCaseSource(nameof(NativeHalfCases))]
    public void HalfBits_MatchActualOfficialInstructions(uint inputBits, ushort expectedHalfBits)
        => Assert.That(VfxSpriteConstantSpinDamping.HalfBits(BitConverter.UInt32BitsToSingle(inputBits)), Is.EqualTo(expectedHalfBits));

    [TestCase(-.4f, -.39990234375f)]
    [TestCase(-1.00146484375f, -1.0009765625f)]
    [TestCase(65520f, 65504f)]
    public void ConstantRows_UseTheNativeSignedHalfValue(float input, float expected)
        => Assert.That(VfxSpriteConstantSpinDamping.Create(Source(input)), Is.EqualTo(expected));

    [TestCase(1e20f)]
    [TestCase(-1e20f)]
    public void HalfOverflow_IsAnExplicitRangeDiagnostic(float input)
    {
        var exception = Assert.Throws<VfxMappingException>(() => VfxSpriteConstantSpinDamping.Create(Source(input)))!;
        Assert.That(exception.Field, Is.EqualTo("ParticleSpinDeceleration"));
        Assert.That(exception.Reason, Is.EqualTo("Vfx.Preview.RangeUnsupported"));
    }

    private static string Source(float input)
    {
        var value = input.ToString("R", CultureInfo.InvariantCulture);
        return $"x_min,0,0;0,{value},linear,linear,0,0,0,0,1|x_max,0,0;0,{value},linear,linear,0,0,0,0,1:x,0;0,1";
    }
}
