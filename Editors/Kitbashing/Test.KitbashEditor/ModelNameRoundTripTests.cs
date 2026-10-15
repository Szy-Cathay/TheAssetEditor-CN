using Shared.GameFormats.RigidModel;
using Shared.GameFormats.RigidModel.MaterialHeaders;
using Shared.GameFormats.RigidModel.Types;

namespace Test.KitbashEditor;

public class ModelNameRoundTripTests
{
    [TestCase("胸甲_左侧")]
    [TestCase("部件部件部件部件部件AB")]
    [TestCase("mesh_ascii")]
    public void MaterialName_SaveReloadAndResave_PreservesName(string name)
    {
        var creator = new WeighterMaterialCreator();
        var material = creator.CreateEmpty(ModelMaterialEnum.weighted);
        material.ModelName = name;
        for (var round = 0; round < 2; round++)
        {
            var bytes = creator.Save(material);
            material = creator.Create(ModelMaterialEnum.weighted, RmvVersionEnum.RMV2_V8, bytes, 0);
            Assert.That(material.ModelName, Is.EqualTo(name));
        }
    }

    [TestCase("部件部件部件部件部件ABC")]
    [TestCase("名称\0后半段")]
    public void MaterialName_RejectsLossyNames(string name)
    {
        var creator = new WeighterMaterialCreator();
        var material = creator.CreateEmpty(ModelMaterialEnum.weighted);
        material.ModelName = name;
        Assert.Throws<ArgumentException>(() => creator.Save(material));
    }
}
