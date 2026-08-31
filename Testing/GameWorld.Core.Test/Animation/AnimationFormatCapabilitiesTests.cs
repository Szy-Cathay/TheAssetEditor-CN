using Shared.GameFormats.Animation;

namespace Testing.GameWorld.Core.Animation;

[TestFixture]
internal class AnimationFormatCapabilitiesTests
{
    [TestCase(4u)]
    [TestCase(5u)]
    [TestCase(6u)]
    [TestCase(7u)]
    [TestCase(8u)]
    public void Evaluate_SupportedSinglePartFormat_AllowsReadEditAndSave(uint version)
    {
        var capabilities = AnimationFormatCapabilities.Evaluate(version, partCount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.CanRead, Is.True);
            Assert.That(capabilities.CanEdit, Is.True);
            Assert.That(capabilities.CanSave, Is.True);
            Assert.That(capabilities.BlockingReasons, Is.Empty);
        });
    }

    [Test]
    public void Evaluate_MultipleParts_AreReadableButReadOnly()
    {
        var capabilities = AnimationFormatCapabilities.Evaluate(version: 7, partCount: 2);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.CanRead, Is.True);
            Assert.That(capabilities.CanEdit, Is.False);
            Assert.That(capabilities.CanSave, Is.False);
            Assert.That(
                capabilities.BlockingReasons,
                Is.EqualTo(new[] { AnimationFormatBlockReason.MultiplePartsAreReadOnly }));
        });
    }

    [Test]
    public void Evaluate_UnsupportedVersion_RejectsAllCapabilities()
    {
        var capabilities = AnimationFormatCapabilities.Evaluate(version: 9, partCount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(capabilities.CanRead, Is.False);
            Assert.That(capabilities.CanEdit, Is.False);
            Assert.That(capabilities.CanSave, Is.False);
            Assert.That(
                capabilities.BlockingReasons,
                Is.EqualTo(new[] { AnimationFormatBlockReason.UnsupportedVersion }));
        });
    }

    [Test]
    public void Evaluate_VersionEightWithMultipleParts_ReportsPartReason()
    {
        var capabilities = AnimationFormatCapabilities.Evaluate(version: 8, partCount: 2);

        Assert.That(
            capabilities.BlockingReasons,
            Is.EqualTo(new[]
            {
                AnimationFormatBlockReason.MultiplePartsAreReadOnly,
            }));
    }
}
