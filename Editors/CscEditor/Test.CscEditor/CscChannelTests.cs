using Editors.CscEditor.Data;
using Shared.GameFormats.Esf;

namespace Test.CscEditor
{
    public class CscChannelTests
    {
        [Test]
        public void Header_is_the_value_when_there_are_no_keyframes()
        {
            var channel = new CscChannel { Header = 42 };
            Assert.That(channel.Evaluate(0), Is.EqualTo(42));
            Assert.That(channel.Evaluate(100), Is.EqualTo(42));
        }

        [Test]
        public void Keyframes_override_the_header_entirely()
        {
            var channel = new CscChannel { Header = 42 };
            channel.AddKeyframe(0, 5);
            Assert.That(channel.Evaluate(0), Is.EqualTo(5));
            Assert.That(channel.Evaluate(100), Is.EqualTo(5));
        }

        [Test]
        public void Linear_segments_interpolate()
        {
            var channel = new CscChannel();
            channel.AddKeyframe(0, 0);
            channel.AddKeyframe(10, 100);

            Assert.That(channel.Evaluate(5), Is.EqualTo(50).Within(0.001));
            Assert.That(channel.Evaluate(-1), Is.EqualTo(0));
            Assert.That(channel.Evaluate(11), Is.EqualTo(100));
        }

        [Test]
        public void Constant_segments_hold_the_left_value()
        {
            var channel = new CscChannel();
            var a = channel.AddKeyframe(0, 1);
            channel.AddKeyframe(10, 9);
            a.ModeOut = "constant";

            Assert.That(channel.Evaluate(9.99f), Is.EqualTo(1));
            Assert.That(channel.Evaluate(10), Is.EqualTo(9));
        }

        [Test]
        public void Bezier_segments_pass_through_their_endpoints_and_stay_monotonic_in_time()
        {
            var channel = new CscChannel();
            var a = channel.AddKeyframe(0, 0);
            var b = channel.AddKeyframe(10, 100);
            a.ModeOut = "bezier_c";
            a.TangentOut = new Coord2d(3, 0);
            b.ModeIn = "bezier_c";
            b.TangentIn = new Coord2d(-3, 0);

            Assert.That(channel.Evaluate(0), Is.EqualTo(0));
            Assert.That(channel.Evaluate(10), Is.EqualTo(100));

            // Flat tangents ease in/out: below linear early, above late, and monotonic.
            var previous = float.MinValue;
            for (var t = 0f; t <= 10f; t += 0.5f)
            {
                var value = channel.Evaluate(t);
                Assert.That(value, Is.GreaterThanOrEqualTo(previous));
                previous = value;
            }
            Assert.That(channel.Evaluate(2), Is.LessThan(20));
            Assert.That(channel.Evaluate(8), Is.GreaterThan(80));
        }

        [Test]
        public void OffsetAll_shifts_header_and_every_keyframe()
        {
            var channel = new CscChannel { Header = 1 };
            channel.AddKeyframe(0, 10);
            channel.AddKeyframe(5, 20);

            channel.OffsetAll(2);

            Assert.That(channel.Header, Is.EqualTo(3));
            Assert.That(channel.Keyframes[0].Value, Is.EqualTo(12));
            Assert.That(channel.Keyframes[1].Value, Is.EqualTo(22));
        }

        [Test]
        public void AddKeyframe_keeps_keyframes_sorted_by_time()
        {
            var channel = new CscChannel();
            channel.AddKeyframe(5, 1);
            channel.AddKeyframe(1, 2);
            channel.AddKeyframe(3, 3);

            Assert.That(channel.Keyframes.Select(k => k.Time), Is.EqualTo(new[] { 1f, 3f, 5f }));
        }
    }
}
