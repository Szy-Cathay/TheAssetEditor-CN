using System.Globalization;
using GameWorld.Core.Rendering.Materials.Shaders;
using KitbasherEditor.ValueConverters;
using Shared.Core.Services;
using Shared.GameFormats.RigidModel;

namespace Test.KitbashEditor
{
    [TestFixture]
    [NonParallelizable]
    public class SceneNodeEditorLocalizationTests
    {
        [OneTimeSetUp]
        public void LoadLocalization()
        {
            var localizationManager = new LocalizationManager();
            localizationManager.LoadLanguage();
        }

        [Test]
        public void DisplayConverter_LocalizesSidebarEnumsAndBooleans()
        {
            var converter = new SceneNodeEditorDisplayConverter();

            Assert.Multiple(() =>
            {
                Assert.That(
                    converter.Convert(
                        UiVertexFormat.Weighted,
                        typeof(string),
                        null!,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo("标准蒙皮（最多 2 根骨骼）"));
                Assert.That(
                    converter.Convert(
                        UiVertexFormat.Cinematic,
                        typeof(string),
                        null!,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo("高质量蒙皮（最多 4 根骨骼）"));
                Assert.That(
                    converter.Convert(
                        VertexFormat.Collision_Format,
                        typeof(string),
                        null!,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo("碰撞"));
                Assert.That(
                    converter.Convert(
                        CapabilityMaterialsEnum.MetalRoughPbr_Emissive,
                        typeof(string),
                        null!,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo("金属度/粗糙度（自发光）"));
                Assert.That(
                    converter.Convert(
                        true,
                        typeof(string),
                        null!,
                        CultureInfo.InvariantCulture),
                    Is.EqualTo("是"));
            });
        }

        [Test]
        public void Guidance_ExplainsGameSpecificMaterialsAndDiagnostics()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "SpecGloss.ToolTip"),
                    Does.Contain("战锤 2"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "ModelMaterial.SelectedShader.ToolTip"),
                    Does.Contain("切换类型会重新创建材质"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "Tint.ToolTip"),
                    Does.Contain("不会写入模型"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "WeightedMaterial.ToolTip"),
                    Does.Contain("与同游戏、同材质的正常模型对比"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "Mesh.RenderBbox.ToolTip"),
                    Does.Contain("不会写入模型"));
            });
        }

        [Test]
        public void AssignMaterialDialog_UsesChineseCopy()
        {
            Assert.Multiple(() =>
            {
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "Kitbash.AssignMaterial.SelectSource"),
                    Is.EqualTo("选择要复制材质的网格"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "Kitbash.AssignMaterial.NoSourceSelected"),
                    Is.EqualTo("未选择要复制材质的网格"));
                Assert.That(
                    LocalizationManager.Instance.Get(
                        "Kitbash.AssignMaterial.MultipleSourcesSelected"),
                    Is.EqualTo("选择了多个网格，请只选择一个材质来源"));
            });
        }

        [Test]
        public void PrimitiveCreation_UsesChineseMenuTooltipsAndUndoHint()
        {
            var keys = new[]
            {
                "Kitbash.Menu.Tools.CreatePrimitive",
                "Kitbash.Menu.Tools.CreatePrimitive.Box",
                "Kitbash.Menu.Tools.CreatePrimitive.Plane",
                "Kitbash.Menu.Tools.CreatePrimitive.Sphere",
                "Kitbash.ToolTip.ConstructBoxUiCommand",
                "Kitbash.ToolTip.ConstructPlaneUiCommand",
                "Kitbash.ToolTip.ConstructSphereUiCommand",
                "Kitbash.CommandHint.ConstructPrimitive"
            };

            Assert.Multiple(() =>
            {
                foreach (var key in keys)
                {
                    var localized = LocalizationManager.Instance.Get(key);
                    Assert.That(localized, Is.Not.EqualTo(key), key);
                    Assert.That(localized, Does.Match("[\u4e00-\u9fff]"), key);
                }
            });
        }
    }
}
