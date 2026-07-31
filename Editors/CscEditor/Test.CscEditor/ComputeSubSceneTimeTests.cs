using Editors.CscEditor.Data;
using Editors.CscEditor.Services;

namespace Test.CscEditor
{
    // Covers CscAnimationComponent.ComputeSubSceneTime: the referenced scene's own local clock for
    // an embedded Composite Scene (ROOT_REF). A negative ELEMENT_PERIOD speed should play the
    // referenced scene in reverse, looping continuously - not freeze at frame 0.
    public class ComputeSubSceneTimeTests
    {
        [Test]
        public void Positive_speed_advances_forward_and_wraps_at_duration()
        {
            var sub = CreateSubScene(begin: 0, speed: 1, duration: 20);

            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 5), Is.EqualTo(5f).Within(0.001));
            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 45), Is.EqualTo(5f).Within(0.001));
        }

        [Test]
        public void Zero_speed_freezes_on_the_first_frame_regardless_of_host_time()
        {
            var sub = CreateSubScene(begin: 0, speed: 0, duration: 20);

            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 0), Is.EqualTo(0f));
            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 100), Is.EqualTo(0f));
        }

        [Test]
        public void Negative_speed_plays_in_reverse_instead_of_freezing_at_zero()
        {
            var sub = CreateSubScene(begin: 0, speed: -1, duration: 20);

            var atThree = CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 3);
            var atFive = CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 5);

            // As host time advances, the local scene time should count DOWN, not sit at 0.
            Assert.That(atThree, Is.EqualTo(17f).Within(0.001));
            Assert.That(atFive, Is.EqualTo(15f).Within(0.001));
            Assert.That(atFive, Is.LessThan(atThree));
        }

        [Test]
        public void Negative_speed_loops_continuously_in_reverse_past_a_full_duration()
        {
            var sub = CreateSubScene(begin: 0, speed: -1, duration: 20);

            // hostTime=25 is a full cycle further than hostTime=5 (both land on local time 15).
            var atFive = CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 5);
            var atTwentyFive = CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 25);

            Assert.That(atTwentyFive, Is.EqualTo(atFive).Within(0.001));
        }

        [Test]
        public void Half_speed_reverse_matches_the_reported_case()
        {
            // The exact case reported: -0.5 speed should be half-speed reverse playback.
            var sub = CreateSubScene(begin: 0, speed: -0.5f, duration: 20);

            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 10), Is.EqualTo(15f).Within(0.001));
            Assert.That(CscAnimationComponent.ComputeSubSceneTime(sub, hostTime: 20), Is.EqualTo(10f).Within(0.001));
        }

        private static CscSubScene CreateSubScene(float begin, float speed, float duration)
        {
            var host = new CscElement { Begin = begin, PeriodSpeedMultiplier = speed };

            // A fresh CscScene's Duration getter falls back to 20 when its header isn't populated
            // (as with a real loaded file) - these tests are all written against that 20s duration.
            Assert.That(duration, Is.EqualTo(20), "test helper assumes the default 20s fallback duration");
            var referencedScene = new CscScene();

            return new CscSubScene { Host = host, Scene = referencedScene };
        }
    }
}
