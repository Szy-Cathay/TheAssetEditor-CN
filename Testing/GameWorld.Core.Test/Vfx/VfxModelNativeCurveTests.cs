#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

[Category("Diagnostic")]
public class VfxModelNativeCurveTests
{
    private const float Tolerance = .00001f;
    private const string Field = "ParticleSpinDeceleration";

    public static IEnumerable<TestCaseData> NativeCases()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(NativeFixture));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress);
        using var bytes = new MemoryStream(); gzip.CopyTo(bytes);
        var data = bytes.ToArray();
        Assert.That(Convert.ToHexString(SHA256.HashData(data)),
            Is.EqualTo("94B758115B47CCFC43C8D353F91DAD69B852C24894F682469DAB657976D9EB09"));
        using var document = JsonDocument.Parse(data);
        Assert.That(document.RootElement.GetProperty("DiagnosticOnly").GetBoolean(), Is.True);
        foreach (var item in document.RootElement.GetProperty("Cases").EnumerateArray())
            yield return new TestCaseData(item.Clone()).SetName("NativeModelCurve_" + item.GetProperty("Name").GetString());
    }

    [TestCaseSource(nameof(NativeCases))]
    public void BakedSamples_MatchTheActualOfficialCurveInstructions(JsonElement item)
    {
        var source = item.GetProperty("Source").GetString()!;
        var curve = VfxModelNativeCurve.Create(source, Field);
        var minimum = item.GetProperty("Minimum").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        var maximum = item.GetProperty("Maximum").EnumerateArray().Select(value => value.GetSingle()).ToArray();
        Assert.That(curve.SampleCount, Is.EqualTo(minimum.Length));
        for (var index = 0; index < minimum.Length; ++index)
        {
            Assert.That(curve.SampleBoundAt(index, false), Is.EqualTo(minimum[index]).Within(Tolerance), $"min[{index}]");
            Assert.That(curve.SampleBoundAt(index, true), Is.EqualTo(maximum[index]).Within(Tolerance), $"max[{index}]");
        }
        Assert.Multiple(() =>
        {
            Assert.That(curve.Minimum, Is.EqualTo(Math.Min(minimum.Min(), maximum.Min())).Within(Tolerance));
            Assert.That(curve.Maximum, Is.EqualTo(Math.Max(minimum.Max(), maximum.Max())).Within(Tolerance));
        });
        Assert.That(VfxModelNativeCurve.Create(source, Field).Sample(.637, .375f), Is.EqualTo(curve.Sample(.637, .375f)));
    }

    [TestCaseSource(nameof(NativeCases))]
    public void RuntimeInterpolation_MatchesTheActualOfficialScalarTableReader(JsonElement item)
    {
        var curve = VfxModelNativeCurve.Create(item.GetProperty("Source").GetString()!, Field);
        foreach (var sample in item.GetProperty("Samples").EnumerateArray())
            Assert.That(curve.Sample(sample.GetProperty("Phase").GetDouble(), sample.GetProperty("Unary").GetSingle()),
                Is.EqualTo(sample.GetProperty("Value").GetSingle()).Within(Tolerance), sample.ToString());
    }

    [Test]
    public void ExplicitAxisBinding_FollowsTheLinkedChannelsInsteadOfTheirArrayOrder()
    {
        const string source = "y_max,0,0;0,4,linear,linear,0,0,0,0,1|y_min,0,0;0,1,linear,linear,0,0,0,0,1:y,1;1,0";
        var curve = VfxModelNativeCurve.Create(source, "EmitterOrientation", "y");
        Assert.That(curve.SampleCount, Is.EqualTo(1));
        Assert.That(curve.Sample(.5, .25f), Is.EqualTo(1.75f));
    }

    [Test]
    public void DefaultBinding_WithTwoIdenticalAuthoredChannelsKeepsTheirOriginalData()
    {
        const string channel = "0,0;0,.1,linear,bezier_c,0,0,.11,1.3,1;1,2,bezier_c,linear,-.31,-1.1,0,0,1";
        var source = "x_min," + channel + "|x_max," + channel + ":x,0;0,1";
        var actual = VfxModelNativeCurve.Create(source, Field);
        var aliased = VfxModelNativeCurve.Create("x_min," + channel + ":x,0;0,0", Field);
        Assert.That(actual.SampleCount, Is.EqualTo(128));
        for (var index = 0; index < 128; ++index)
            Assert.That(actual.SampleBoundAt(index, false), Is.EqualTo(actual.SampleBoundAt(index, true)));
        foreach (var phase in new[] { -.1, .25, .637, 1 })
            foreach (var unary in new[] { 0f, .375f, 1f })
                Assert.That(actual.Sample(phase, unary), Is.EqualTo(aliased.Sample(phase, unary)));
    }

    [Test]
    public void BakedRange_IncludesBezierHandleOvershootRatherThanOnlyKeyValues()
    {
        const string source = "x_min,0,0;0,0,linear,bezier_c,0,0,.333,4,1;1,0,bezier_c,linear,-.333,4,0,0,1:x,0;0,0";
        var curve = VfxModelNativeCurve.Create(source, Field);
        Assert.That(curve.Minimum, Is.Zero);
        Assert.That(curve.Maximum, Is.GreaterThan(2.99f));
    }

    [TestCase("bezier_q")]
    [TestCase("bezier_u")]
    [TestCase("spline")]
    [TestCase("step_next")]
    [TestCase("step")]
    public void UnverifiedModes_AreStillExplicitlyRejected(string mode)
    {
        var source = $"x_min,0,0;0,0,linear,{mode},0,0,.2,1,1;1,1,{mode},linear,-.2,-1,0,0,1:x,0;0,0";
        var error = Assert.Throws<VfxMappingException>(() => VfxModelNativeCurve.Create(source, Field));
        Assert.That(error!.Reason, Is.EqualTo("Vfx.Preview.InterpolationUnsupported"));
    }

    [TestCase("x,0;0,1")]
    [TestCase("x,1;0,2")]
    [TestCase("x,1;0,1|x,0;0,0")]
    public void InvalidOrAmbiguousBindings_AreNotSilentlyGuessed(string links)
    {
        var source = "x_min,0,0;0,-1,linear,linear,0,0,0,0,1|x_max,0,0;0,2,linear,linear,0,0,0,0,1:" + links;
        Assert.Throws<VfxMappingException>(() => VfxModelNativeCurve.Create(source, Field));
    }

    // Native fixture: official Utility get_value_at and Warscape729e50 code-page execution.
    // It contains 24 diagnostic curves, 5636 baked values and 648 runtime results, not product-generated expectations.
    // Readable source and all DLL hashes remain in R96 native-curves/NativeModelScalarCurves-v1.json.
    private const string NativeFixture = """
        H4sIAAAAAAACCuy9W88mx5Wld2/A/6HQVx6jyIkd55gGDPgA3/kANMa+MAaNUvFjq9AUKZCldvc0/N+9nviKonJlieJXrHyli5RaLbGi3ozMOOyIvdfea/37
        f/vfvHr1d//Luzf/9O13P7x/9/b/+Pabf/u7//Tq/fd/eHq9m/7h7Xe/f9Kf/N3/+Pb9H9588+q7r79+9/ad/sfb7756+uL3b/7p6dXTvz69/cP7d999+/rV
        b998+9UXb7/79gc94O37p69e/fPTv339/ZvfPf3w+tW3371/9UY/+93vv3l6//TTg56+/vrp7ftX//LuB57/5u3bp9+/f/Pt26e/e36B//z+3Tfv3v/bP/z2
        TW6dFxkr5zflq69WSl9/9ebN00zl6xGr6k9zeRotjfhNqy2e3sRX+e2stX9dIlZ605/mb0Z+++G5//eb7394++b3Tz89+O1oX9X0pqS3T6vM1ebXtbY3T209
        /UZ/46m+zeXrFjnFmvWr+Lo8vWlv1VJifT16+c388OD//c37d//y9L/+4du3jMkPevL/w5//9CmjPX2VXv3T0/t//Jc33/zh6R/fvH/1xf+Q1/o6pf+o//rN
        83+9yen5gX/yriOvp5Ze6X9/8+b7V+/f/Oabp1ff/+Hb9+9+p/9+evPV0/d/xy/+y/OL/E9v/vnp//ztmx/29H39zXdv3v9377796ulf/8N///wP8R8jj//w
        +tX7//e752f98Cpeffd95Pnq6dv33797+uHDF/3PesaffMe/P//X/tTf7Ye/+eHffve7J/3k7Rffv/vh3bf/9OObs4C++8P3b/ff+td//N27b1+n1+nv9Z8v
        4/U37759evP96988/dd3T9//49vXX6Qv8+vdFK/jy/I6/j5e55+aP/x1/a0Sr78IPeD578d/+tfnR/5Jp//bu2/f/e4Pv/vpnfmXHpye/xV1pYge+fWhdebe
        eo+2Rm4x17E1t5R7HmnUEbOOVg6tJY0VjTY9YqxlrW2WlksqLdVVy2yH1ppKrmvU1FNqs616bK1lzNqSVt6cPdlPZ+ojj9rn6kMPPjS2qHpYGT1NvfIcx0a1
        xey11FHbmOvYqF2Uy6orKp98fGxPWXsgaW/o6WNaYx6960eta99Ne6Heoidt1DS0X/Tex8ZRtUnbTNqxc9jg99VaVsPoeaZs8zY08Pr4NgpfOa2x1DVpafyt
        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
        """;
}
