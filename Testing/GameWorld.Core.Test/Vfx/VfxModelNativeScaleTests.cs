#nullable enable
using System.Collections;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Xml.Linq;
using GameWorld.Core.Rendering;
using GameWorld.Core.Vfx;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class VfxModelNativeScaleTests
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly CommonShaderParameters Camera = new(Matrix.Identity, Matrix.Identity,
        new(0, 0, 20), Vector3.Zero, 0, 0, 0, 1, Vector3.One, []);

    [TestCase(.01f, 5f)]
    [TestCase(.05f, 5f)]
    [TestCase(.1f, 5f)]
    [TestCase(.2f, 5f)]
    [TestCase(.5f, 1f)]
    [TestCase(1f, .5f)]
    public void DynamicModelSizeUsesTheNativeBakedTableBeforeInitialScaleAndRootScale(float time, float speed)
    {
        using var fixture = Fixture(); var source = Source(fixture.RootElement.GetProperty("Source").GetString()!);
        var parent = VfxDocument.CreateComposition(); var reference = parent.AddReference("leaf", VfxDocument.Read(source)); reference.Apply();
        reference.Selection.SetAttributeValue("simulation_speed_multiplier", speed);
        var conversion = TotalWarVfxConverter.Convert(new(parent.Write(), "diagnostic/native-model-size.xml",
            path => path.Replace('/', '\\') == @"vfx\leaf.xml" ? source : VfxModelVariantTests.Resolve(path)));
        Assert.That(conversion.Emitters, Has.Count.EqualTo(1));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(time, Matrix.CreateScale(3));
        var particle = ((IEnumerable)Value<object>(simulation, "_particles")).Cast<object>().OrderBy(item => Value<double>(item, "Birth")).First();
        var age = Math.Clamp((double)time - Value<double>(particle, "Birth"), 0, Value<float>(particle, "Life"));
        var phase = age * speed / (Value<float>(particle, "Life") * speed + .000001);
        var unary = Value<Vector3>(particle, "RandomScale").X;
        var expected = NativeTable(fixture.RootElement, phase, unary) * Value<Vector3>(particle, "InitialScale") * 3;
        var rendered = simulation.ProjectedFrame(Camera, false).OrderByDescending(item => item.Render.Time).First().Render.Transform;
        var size = new Vector3(rendered.Right.Length(), rendered.Up.Length(), rendered.Backward.Length());
        Assert.That(Vector3.Distance(size, expected), Is.LessThan(.000001f));
        Assert.That(Value<float>(particle, "Life"), Is.EqualTo(50 / speed));
        Assert.That(VfxDocument.Read(source).Write(), Is.EqualTo(source));
    }

    [Test]
    public void TheOfficialSharpRiseIsNotEquivalentToEvaluatingItsRawBezierAtDrawTime()
    {
        using var fixture = Fixture(); var text = fixture.RootElement.GetProperty("Source").GetString()!;
        var direct = new VfxPreviewCurve(text, "ParticleScale", 1).Vector(.001, new(.375f));
        var native = NativeTable(fixture.RootElement, .001, .375f);
        Assert.That(Vector3.Distance(direct, native), Is.GreaterThan(.05f));
    }

    [TestCase(0)]
    [TestCase(.001)]
    [TestCase(.007)]
    [TestCase(.01)]
    [TestCase(.5)]
    [TestCase(1)]
    public void NativeVectorScaleRetainsAllOriginalBakedAxes(double phase)
    {
        using var fixture = Fixture(); var table = VfxModelNativeScale.Create(fixture.RootElement.GetProperty("Source").GetString()!);
        Assert.That(table, Is.Not.Null);
        foreach (var unary in new[] { 0f, .375f, 1f })
            Assert.That(Vector3.Distance(table!.Sample(phase, unary), NativeTable(fixture.RootElement, phase, unary)), Is.LessThan(.00001f));
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TrackedEventScaleUsesTheSameBakedParentSizeAsTheRenderedModel(bool track)
    {
        using var fixture = Fixture(); var document = VfxDocument.Read(Source(fixture.RootElement.GetProperty("Source").GetString()!));
        var emitter = document.Root.Descendants("emitter").Single();
        emitter.Elements().Where(item => (string?)item.Attribute("name") is "MaximumParticles" or "EmissionRateCurve").Remove();
        emitter.Add(new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 1)),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), VfxLibraryVisibilityTests.Constant("x", .1f)),
            new XElement("property", new XAttribute("name", "EventEnable"), new XAttribute("value", "true")));
        var spawn = new XElement(VfxDocument.Read(VfxParticleEventTests.Source(true, "true")).Root.Descendants("property_event").Single());
        spawn.SetAttributeValue("track_scale", track ? "true" : "false"); emitter.Add(spawn);
        var child = VfxParticleEventTests.Source(false);
        var conversion = TotalWarVfxConverter.Convert(new(document.Write(), "diagnostic/native-model-scale-event.xml",
            path => path.EndsWith("child.xml", StringComparison.OrdinalIgnoreCase) ? child : VfxModelVariantTests.Resolve(path)));
        var simulation = new TotalWarVfxSimulation(conversion); simulation.Seek(.05f, Matrix.CreateScale(3));
        var parent = ((IEnumerable)Value<object>(simulation, "_particles")).Cast<object>()
            .Single(item => Value<object>(Value<object>(item, "Emitter"), "Parent") == null);
        var phase = (.05f - Value<double>(parent, "Birth")) / (Value<float>(parent, "Life") + .000001);
        var expected = track ? NativeTable(fixture.RootElement, phase, Value<Vector3>(parent, "RandomScale").X)
            * Value<Vector3>(parent, "InitialScale") * 3 : Vector3.One;
        var rendered = simulation.ProjectedFrame(Camera, false).Single(item => item.Render.Layer != 0).Render.Transform;
        Assert.That(rendered.Right.Length(), Is.EqualTo(expected.X).Within(.000001f));
        Assert.That(rendered.Up.Length(), Is.EqualTo(expected.Y).Within(.000001f));
    }

    [Test]
    public void ConstantModelScaleRetainsTheExistingUnbakedPath()
    {
        var source = Source(VfxParticleEventTests.Curve(["x", "y", "z"], 1, 2, 3));
        var conversion = VfxModelVariantTests.Convert(source);
        Assert.That(conversion.Emitters.Single().ModelScaleTable, Is.Null);
    }

    private static Vector3 NativeTable(JsonElement fixture, double phase, float unary)
    {
        var arrays = fixture.GetProperty("Channels").EnumerateArray().ToDictionary(item => item.GetProperty("Name").GetString()!,
            item => item.GetProperty("Samples").EnumerateArray().Select(value => value.GetSingle()).ToArray());
        var position = Math.Clamp((float)phase, 0, 1) * 127; var lower = (int)position; var upper = Math.Min(lower + 1, 127); var fraction = position - lower;
        float Axis(string axis)
        {
            var minimum = arrays[axis + "_min"]; var maximum = arrays[axis + "_max"];
            var first = minimum[lower] + (minimum[upper] - minimum[lower]) * fraction;
            var second = maximum[lower] + (maximum[upper] - maximum[lower]) * fraction;
            return first + (second - first) * unary;
        }
        return new(Axis("x"), Axis("y"), Axis("z"));
    }

    private static byte[] Source(string scale)
    {
        var document = VfxDocument.Read(VfxModelVariantTests.Source(1)); var emitter = document.Root.Descendants("emitter").Single();
        foreach (var name in new[] { "ParticleScale", "ParticleInitialScale", "ParticleLifeTime", "EmissionRateCurve", "MaximumParticles" })
            emitter.Elements().Where(item => (string?)item.Attribute("name") == name).Remove();
        emitter.Add(new XElement("property", new XAttribute("name", "ParticleScale"), scale),
            new XElement("property", new XAttribute("name", "ParticleInitialScale"), "x,0,0;0,.018,linear,linear,0,0,0,0,1|y,0,0;0,.022,linear,linear,0,0,0,0,1|z,0,0;0,.018,linear,linear,0,0,0,0,1:x,0;0,0|y,0;1,1|z,0;2,2"),
            new XElement("property", new XAttribute("name", "ParticleLifeTime"), VfxLibraryVisibilityTests.Constant("x", 50)),
            new XElement("property", new XAttribute("name", "EmissionRateCurve"), VfxLibraryVisibilityTests.Constant("x", 500)),
            new XElement("property", new XAttribute("name", "MaximumParticles"), new XAttribute("value", 30)));
        return document.Write();
    }

    private static T Value<T>(object target, string name) => (T)target.GetType().GetField(name, Fields)!.GetValue(target)!;
    private static JsonDocument Fixture()
    {
        using var compressed = new MemoryStream(Convert.FromBase64String(NativeFixture));
        using var gzip = new GZipStream(compressed, CompressionMode.Decompress); using var output = new MemoryStream(); gzip.CopyTo(output);
        var bytes = output.ToArray(); Assert.That(Convert.ToHexString(SHA256.HashData(bytes)), Is.EqualTo("40D46A40D1DF25B0173231A7CF2C36C0EC0E8EB892E44FF2C070300E23C5574C"));
        return JsonDocument.Parse(bytes);
    }

    private const string NativeFixture = "H4sIAAAAAAACCu2ZS4/bNhCA7/kVxJ6SQnb5FKndU7rb16HbBdwgh6owRtTIZiNLhiRvLCf576Vk79o9BFggBowWFGxrPg5nNBxSHsv89IqQqzsHi6puO2d/r8r+6pp0zQajQTOz9Rp9w9VPDbZLArbbQEnqonDWeeFd50rX9VphTskCu/kjlBucQ0dc1XontnN1RXCLdjNKriLrxj1Ch2QF6zXmZIWruulvSN24hau8ywWu2pg8QOOjKXFmoUTyAfuWtHXTeYOs99KmsUg6t8Ipua/JXVn+Bq6KSAWde0SCuevqJiINVjk26KWf3/3qr/BkCG2L3dirnV7thzkqHqBbDmO9u07Th6b+G22XprNdP7n1CujT9I8lvh1sfxwvMLm9T9MhzsKnpU3Tx2I7+YiwrqtJ14ArJw3jMk33F53Axhul6dM4J1nf4d4oTT8uxTwvLZfz0mVzrDqf0/miQazmeQOLuppuV+VppLNf3nIVD7HGmCTcMkULamiWQaGsKgqbC2s0LXILOpcZp7myVPM4SzINBgtFsTAKbAZ7r4d5PLrVCecg8jyh3gkAGioKzRLpW7lArahmmZKKIbCcWyNlXAjGEgoxmkxzu3f7HprWwvokXKtVLikIajERJlGmkFIBqgQz3wOl5aJQjFOWGJmzQiAo6zWCJYWORWb2fn+AD/iwhHZcmUVZQ/favfluL7DvGddvIsbN03QPq+c0eYPRdr7yC8ZIqqiKI3pDp5RFbKqiDHcOm7k9ChOvo8IfkRf8W4hnYDf7Jq9XSfw1W6lZPHQSnIkkOvHGmPbKiH320cA2YrHmxsf9FA69VDj9mBw/15qJ5+Rw7/pS0fjkSO6dSHOcqgtFszvkhlHDLr9wdofcMJpIcel1c70dPA0z5s88Ep93/iwjtb/3bpdQVVi2/u770zMhn8ZPr7mH1fGeHDuPzTNYrUs89h+OQ4wq8QfVmnJOdRw9q5kflRle/gsqYUpxfapTAQIECBAgwFnhIP81nr9EXy1vsP2m8kanUknNhyKt/E/Ff6tUgAABAgQIcFZ4UXXrz/Dwpg1lUulYCkVNwthRN/z5ECBAgAABApwTXlrevvnhzfjHNkETo4zgnEl5WvpogAABAgQIcFZ4UXnbha23AAECBAjw/9t624WttwABAgQI8N/dens1SF/+AZc3cVCyLwAA";
}
