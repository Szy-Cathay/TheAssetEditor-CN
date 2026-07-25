using Editors.Ipc;

namespace Test.Ipc
{
    public class AssetEditorIpcServerTests
    {
        [Test]
        public void PipeName_UsesCnEditionIdentity()
        {
            Assert.That(AssetEditorIpcServer.PipeName, Is.EqualTo("AssetEditor.CN.Ipc"));
        }
    }
}
