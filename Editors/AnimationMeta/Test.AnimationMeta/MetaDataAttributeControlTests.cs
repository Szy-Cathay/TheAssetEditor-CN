using Editors.AnimationMeta.Presentation;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Shared.Core.Events;
using Shared.GameFormats.AnimationMeta.Definitions;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Test.AnimationMeta
{
    public class MetaDataAttributeControlTests
    {
        [Test]
        public void NativeBooleanField_UsesBooleanEditorAndWritesBack()
        {
            var effect = new Effect_v12
            {
                Name = "EFFECT",
                Version = 12,
                Tracking = false
            };
            var eventHub = new RecordingEventHub();
            var entry = new MetaDataEntry(effect, "", eventHub, true);
            var variable = entry.Variables.Single(item =>
                item.PropertyName == nameof(Effect_v12.Tracking));

            Assert.That(variable, Is.InstanceOf<BooleanAttributeViewModel>());

            var booleanVariable = (BooleanAttributeViewModel)variable;
            booleanVariable.Value = true;

            Assert.Multiple(() =>
            {
                Assert.That(effect.Tracking, Is.True);
                Assert.That(booleanVariable.IsModified, Is.True);
                Assert.That(eventHub.LocalPublishCount, Is.EqualTo(1));
            });

            effect.Tracking = false;
            booleanVariable.RefreshFromTarget();

            Assert.Multiple(() =>
            {
                Assert.That(booleanVariable.Value, Is.False);
                Assert.That(eventHub.LocalPublishCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void FixedChoiceFields_UseChoiceEditorAndPreserveUnknownCurrentValue()
        {
            var effect = new Effect_v12
            {
                Name = "EFFECT",
                Version = 12,
                EffectType = 77
            };
            var eventHub = new RecordingEventHub();
            var entry = new MetaDataEntry(effect, "", eventHub, true);
            var variable = entry.Variables.Single(item =>
                item.PropertyName == nameof(Effect_v12.EffectType));

            Assert.That(variable, Is.InstanceOf<ChoiceAttributeViewModel>());

            var choiceVariable = (ChoiceAttributeViewModel)variable;
            Assert.Multiple(() =>
            {
                Assert.That(
                    choiceVariable.Choices.Select(item => item.ValueAsString),
                    Is.EquivalentTo(new[] { "0", "1", "2", "77" }));
                Assert.That(choiceVariable.ValueAsString, Is.EqualTo("77"));
                Assert.That(effect.EffectType, Is.EqualTo(77));
                Assert.That(eventHub.LocalPublishCount, Is.Zero);
            });

            choiceVariable.ValueAsString = "1";

            Assert.Multiple(() =>
            {
                Assert.That(effect.EffectType, Is.EqualTo(1));
                Assert.That(eventHub.LocalPublishCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void TriStateTextChoice_PreservesEmptyAsSerializedValue()
        {
            var effect = new Effect_v2
            {
                Name = "EFFECT",
                Version = 2,
                BoolUnknown = "legacy_mod_value"
            };
            var eventHub = new RecordingEventHub();
            var entry = new MetaDataEntry(effect, "", eventHub, true);
            var variable = entry.Variables.Single(item =>
                item.PropertyName == nameof(Effect_v2.BoolUnknown));

            Assert.That(variable, Is.InstanceOf<ChoiceAttributeViewModel>());

            var choiceVariable = (ChoiceAttributeViewModel)variable;
            Assert.Multiple(() =>
            {
                Assert.That(
                    choiceVariable.Choices.Select(item => item.ValueAsString),
                    Is.EquivalentTo(new[]
                    {
                        "false",
                        "true",
                        "",
                        "legacy_mod_value"
                    }));
                Assert.That(effect.BoolUnknown, Is.EqualTo("legacy_mod_value"));
                Assert.That(eventHub.LocalPublishCount, Is.Zero);
            });

            choiceVariable.ValueAsString = "";

            Assert.Multiple(() =>
            {
                Assert.That(effect.BoolUnknown, Is.Empty);
                Assert.That(eventHub.LocalPublishCount, Is.EqualTo(1));
            });
        }

        [Test]
        public void AllMetadataFields_UseTheAuditedEditorClassification()
        {
            var assembly = typeof(ParsedMetadataAttribute).Assembly;
            var declaredFields = assembly.GetTypes()
                .Where(type => type.Namespace?.StartsWith(
                    "Shared.GameFormats.AnimationMeta",
                    StringComparison.Ordinal) == true)
                .SelectMany(type => type.GetProperties(
                    BindingFlags.Public |
                    BindingFlags.Instance |
                    BindingFlags.DeclaredOnly))
                .Select(property => new
                {
                    Property = property,
                    Definition = property.GetCustomAttribute<MetaDataTagAttribute>(
                        false)
                })
                .Where(field => field.Definition != null)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.That(
                    declaredFields.Count(field =>
                        field.Property.PropertyType == typeof(bool)),
                    Is.EqualTo(21));
                Assert.That(
                    declaredFields.Count(field =>
                        field.Property.PropertyType == typeof(string) &&
                        field.Definition!.ChoiceValues.Length != 0),
                    Is.EqualTo(24));
                Assert.That(
                    declaredFields.Count(field =>
                        field.Property.PropertyType == typeof(int) &&
                        field.Definition!.ChoiceValues.Length != 0),
                    Is.EqualTo(18));
            });

            var eventHub = new RecordingEventHub();
            var metadataTypes = assembly.GetTypes()
                .Where(type =>
                    type.IsAbstract == false &&
                    type.GetCustomAttribute<MetaDataAttribute>() != null)
                .ToList();

            foreach (var type in metadataTypes)
            {
                var metadataDefinition =
                    type.GetCustomAttribute<MetaDataAttribute>()!;
                var metadata = (ParsedMetadataAttribute)Activator.CreateInstance(
                    type)!;
                metadata.Name = metadataDefinition.Name;
                metadata.Version = metadataDefinition.Version;
                var entry = new MetaDataEntry(metadata, "", eventHub, true);

                foreach (var variable in entry.Variables)
                {
                    var property = type.GetProperty(variable.PropertyName)!;
                    var definition = property.GetCustomAttribute<MetaDataTagAttribute>(
                        false)!;

                    if (property.PropertyType == typeof(bool))
                    {
                        Assert.That(
                            variable,
                            Is.InstanceOf<BooleanAttributeViewModel>(),
                            $"{type.Name}.{property.Name}");
                    }
                    else if (definition.ChoiceValues.Length != 0)
                    {
                        Assert.That(
                            variable,
                            Is.InstanceOf<ChoiceAttributeViewModel>(),
                            $"{type.Name}.{property.Name}");
                    }
                    else if (property.PropertyType != typeof(
                        Microsoft.Xna.Framework.Vector3) &&
                        property.PropertyType != typeof(
                            Microsoft.Xna.Framework.Vector4))
                    {
                        Assert.That(
                            variable.GetType(),
                            Is.EqualTo(typeof(AttributeViewModel)),
                            $"{type.Name}.{property.Name}");
                    }
                }
            }
        }

        [Test]
        public void MetadataChoiceTemplates_UseSharedControlsAndChineseLabels()
        {
            var solutionRoot = FindSolutionRoot();
            var view = XDocument.Load(Path.Combine(
                solutionRoot,
                "Editors",
                "MetaDataEditor",
                "AnimationMeta",
                "MetaEditor",
                "View",
                "MetaDataAttributeView.xaml"));
            var templates = view.Descendants()
                .Where(element => element.Name.LocalName == "DataTemplate")
                .ToList();
            var booleanTemplate = templates.Single(template =>
                template.Attribute("DataType")?.Value.Contains(
                    "BooleanAttributeViewModel",
                    StringComparison.Ordinal) == true);
            var choiceTemplate = templates.Single(template =>
                template.Attribute("DataType")?.Value.Contains(
                    "ChoiceAttributeViewModel",
                    StringComparison.Ordinal) == true);

            Assert.Multiple(() =>
            {
                Assert.That(
                    booleanTemplate.Descendants().Any(element =>
                        element.Name.LocalName == "Style" &&
                        element.Attribute("BasedOn")?.Value ==
                        "{StaticResource AeInput.CheckBox}"),
                    Is.True);
                Assert.That(
                    choiceTemplate.Descendants().Any(element =>
                        element.Name.LocalName == "Style" &&
                        element.Attribute("BasedOn")?.Value ==
                        "{StaticResource AeInput.ComboBox}"),
                    Is.True);
            });

            using var language = JsonDocument.Parse(File.ReadAllText(
                Path.Combine(solutionRoot, "AssetEditor", "Language_Cn.json")));
            var translationKeys = new[]
            {
                "MetaData.Option.Common.false",
                "MetaData.Option.Common.true",
                "MetaData.Option.Common.Empty",
                "MetaData.Option.LegacyValue",
                "MetaData.Option.EffectType.0",
                "MetaData.Option.EffectType.1",
                "MetaData.Option.EffectType.2",
                "MetaData.Option.AoeShape.0",
                "MetaData.Option.AoeShape.1",
                "MetaData.Option.AttachMethod.1",
                "MetaData.Option.AttachMethod.2",
                "MetaData.Option.AttachMethod.3",
                "MetaData.Option.AttachMethod.4",
                "MetaData.Option.OverrideProp.0",
                "MetaData.Option.OverrideProp.1",
                "MetaData.Option.OverrideProp.2",
                "MetaData.Option.OverrideProp.3",
                "MetaData.Option.OverrideProp.4",
                "MetaData.Option.OverrideProp.5",
                "MetaData.Option.OverrideProp.6",
                "MetaData.Option.OverrideProp.7"
            };

            Assert.That(
                translationKeys.All(key =>
                    language.RootElement.TryGetProperty(key, out var value) &&
                    string.IsNullOrWhiteSpace(value.GetString()) == false),
                Is.True);
        }

        private static string FindSolutionRoot()
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(
                        directory.FullName,
                        "AssetEditor.CN.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the solution root.");
        }

        private sealed class RecordingEventHub : IEventHub
        {
            public int LocalPublishCount { get; private set; }

            public void PublishGlobalEvent<T>(T e)
            {
            }

            public void Publish<T>(T e) => LocalPublishCount++;

            public void Register<T>(object owner, Action<T> action)
            {
            }

            public void UnRegister(object owner)
            {
            }
        }
    }
}
