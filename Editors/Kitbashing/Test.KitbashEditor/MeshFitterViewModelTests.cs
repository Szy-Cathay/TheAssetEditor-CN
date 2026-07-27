using Editors.KitbasherEditor.ChildEditors.MeshFitter;

namespace Test.KitbashEditor
{
    [TestFixture]
    public class MeshFitterViewModelTests
    {
        [TestCase(2f, 4f, 2f)]
        [TestCase(4f, 2f, 0.5f)]
        [TestCase(0f, 2f, 1f)]
        [TestCase(2f, 0f, 1f)]
        public void CalculateRelativeScale_UsesTargetToSourceLengthAndRejectsDegenerateBones(
            float sourceLength,
            float targetLength,
            float expected)
        {
            var result = MeshFitterViewModel.CalculateRelativeScale(sourceLength, targetLength);

            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
