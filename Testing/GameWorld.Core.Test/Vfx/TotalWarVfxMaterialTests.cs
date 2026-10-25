using GameWorld.Core.Vfx;

namespace GameWorld.Core.Test.Vfx;

public class TotalWarVfxMaterialTests
{
    [Test]
    public void Dxt5Normal_UsesAlphaForHorizontalDisplacementAndDoesNotUseItAsOpacity()
    {
        byte[] pixels = [255, 64, 0, 192, 0, 128, 0, 128];
        TotalWarVfxMaterial.ConvertDxt5Normal(pixels);
        Assert.That(pixels, Is.EqualTo(new byte[] { 192, 64, 0, 255, 128, 128, 0, 255 }));
    }
}
