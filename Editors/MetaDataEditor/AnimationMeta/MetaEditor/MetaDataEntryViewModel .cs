using System.Collections.ObjectModel;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Xna.Framework;
using Shared.ByteParsing;
using Shared.ByteParsing.Parsers;
using Shared.Core.Events;
using Shared.Core.Services;
using Shared.GameFormats.AnimationMeta.Parsing;

namespace Editors.AnimationMeta.Presentation
{
    public partial class MetaDataEntry : ObservableObject
    {
        public ParsedMetadataAttribute _input;

        [ObservableProperty] ObservableCollection<AttributeViewModel> _variables = [];
        [ObservableProperty] string _displayName = "";
        [ObservableProperty] string _description = "";
        [ObservableProperty] bool _isDecodedCorrectly = false;
        [ObservableProperty] int _version;
        [ObservableProperty] bool _isSelected;

        public MetaDataEntry(ParsedMetadataAttribute typedMetaItem, string description, IEventHub eventHub, bool decodedCorrectly)
        {
            _input = typedMetaItem;
            DisplayName = typedMetaItem.DisplayName;
            Description = description;
            Version = typedMetaItem.Version;
            IsDecodedCorrectly = decodedCorrectly;

            if(IsDecodedCorrectly == false)
                return;

            var orderedPropertiesList = typedMetaItem.GetType().GetProperties()
                        .Where(x => x.CanWrite)
                        .Where(x => Attribute.IsDefined(x, typeof(MetaDataTagAttribute)))
                        .OrderBy(x => x.GetCustomAttributes<MetaDataTagAttribute>(false).Single().Order);

            foreach (var prop in orderedPropertiesList)
            {
                var attributeInfo = prop.GetCustomAttributes<MetaDataTagAttribute>(false).Single();
                var parser = ByteParserFactory.Create(prop.PropertyType);
                var value = prop.GetValue(typedMetaItem);

                var fieldName = GetLocalizedPropertyName(prop.Name);
                var itemDescription = GetLocalizedPropertyDescription(prop.Name, attributeInfo.Description, prop.PropertyType.Name);

                AttributeViewModel? editableItem = null;
                if (value is bool boolean)
                {
                    editableItem = new BooleanAttributeViewModel(
                        fieldName,
                        itemDescription,
                        parser,
                        boolean,
                        typedMetaItem,
                        prop,
                        eventHub);
                }
                else if (attributeInfo.ChoiceValues.Length != 0)
                {
                    var choices = attributeInfo.ChoiceValues.Select(choice =>
                        new AttributeChoiceValue(
                            choice,
                            GetLocalizedChoiceName(prop.Name, choice)));
                    editableItem = new ChoiceAttributeViewModel(
                        fieldName,
                        itemDescription,
                        parser,
                        value,
                        typedMetaItem,
                        prop,
                        eventHub,
                        choices,
                        GetLocalizedText(
                            "MetaData.Option.LegacyValue",
                            "{0}"));
                }
                else if (attributeInfo.DisplayOverride == MetaDataTagAttribute.DisplayType.EulerVector || value is Vector3)
                {
                    if (value is Vector3 vector3)
                        editableItem = new VectorAttributeViewModel(fieldName, itemDescription, parser as Vector3Parser, vector3, typedMetaItem, prop, eventHub);
                    else if (value is Vector4 quaternion)
                        editableItem = new OrientationAttributeViewModel(fieldName, itemDescription, parser as Vector4Parser, quaternion, typedMetaItem, prop, eventHub);
                    else
                        throw new Exception("Unknown item");
                }
                else
                {
                    editableItem = new AttributeViewModel(fieldName, itemDescription, parser, value.ToString(), typedMetaItem, prop, eventHub);
                }

                editableItem.IsReadOnly = !attributeInfo.IsEditable;
                Variables.Add(editableItem);
            }

            if (Variables.Count != 0)
                Variables.First().IsReadOnly = true;
        }

        public string? HasError()
        {
            foreach (var variable in Variables)
            {
                if (!variable.IsValid)
                    return $"Variable '{variable.FieldName}' in {DisplayName} has an error";
            }

            return null;
        }

        static string FormatFieldName(string name)
        {
            var newName = "";
            for (var i = 0; i < name.Length; i++)
            {
                if (char.IsUpper(name[i]) && i != 0)
                    newName += " ";
                newName += name[i];
            }
            return newName;
        }

        static string GetLocalizedPropertyName(string propertyName)
        {
            try
            {
                if (LocalizationManager.Instance != null)
                {
                    var key = $"MetaData.Prop.{propertyName}";
                    var localized = LocalizationManager.Instance.Get(key);
                    if (localized != key)
                        return localized;
                }
            }
            catch { }

            return FormatFieldName(propertyName);
        }

        static string GetLocalizedChoiceName(string propertyName, string value)
        {
            var token = string.IsNullOrEmpty(value) ? "Empty" : value;
            var specificKey = $"MetaData.Option.{propertyName}.{token}";
            var specific = GetLocalizedText(specificKey, specificKey);
            if (specific != specificKey)
                return specific;

            var commonKey = $"MetaData.Option.Common.{token}";
            var common = GetLocalizedText(commonKey, commonKey);
            return common != commonKey ? common : value;
        }

        static string GetLocalizedText(string key, string fallback)
        {
            try
            {
                if (LocalizationManager.Instance != null)
                {
                    var localized = LocalizationManager.Instance.Get(key);
                    if (localized != key)
                        return localized;
                }
            }
            catch { }

            return fallback;
        }

        static string GetLocalizedPropertyDescription(string propertyName, string attributeDescription, string typeName)
        {
            try
            {
                if (LocalizationManager.Instance != null)
                {
                    var key = $"MetaData.PropTip.{propertyName}";
                    var localized = LocalizationManager.Instance.Get(key);
                    if (localized != key)
                        return localized + "\n" + $"Value type is {typeName}";
                }
            }
            catch { }

            var itemDescription = $"Value type is {typeName}";
            if (string.IsNullOrWhiteSpace(attributeDescription) == false)
                itemDescription = attributeDescription + "\n" + itemDescription;
            return itemDescription;
        }
    }
}

