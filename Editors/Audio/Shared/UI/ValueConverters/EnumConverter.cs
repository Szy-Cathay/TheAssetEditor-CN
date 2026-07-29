using System;
using System.Globalization;
using System.Windows.Data;
using Editors.Audio.Shared.GameInformation.Warhammer3;
using Editors.Audio.Shared.Wwise;
using Shared.Core.Services;

namespace Editors.Audio.Shared.UI.ValueConverters
{
    public class EnumConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null)
                return value;
            else if (value is Wh3Language language)
                return LocalizationManager.Instance.Get($"AudioExplorer.Language.{language}");
            else if (value is Wh3DialogueEventType dialogueEventType)
                return Wh3DialogueEventInformation.GetDialogueEventTypeDisplayName(dialogueEventType);
            else if (value is Wh3DialogueEventUnitProfile dialogueEventProfile)
                return Wh3DialogueEventInformation.GetDialogueEventProfileDisplayName(dialogueEventProfile);
            else if (value is ContainerType containerType)
                return GetHircEnumDisplayName(containerType);
            else if (value is RandomType randomType)
                return GetHircEnumDisplayName(randomType);
            else if (value is PlayMode containerMode)
                return GetHircEnumDisplayName(containerMode);
            else if (value is PlaylistEndBehaviour endBehaviour)
                return GetHircEnumDisplayName(endBehaviour);
            else if (value is LoopingType loopingType)
                return GetHircEnumDisplayName(loopingType);
            else if (value is TransitionType transitionType)
                return GetHircEnumDisplayName(transitionType);
            else
                return null;
        }

        private static string GetHircEnumDisplayName<TEnum>(TEnum value) where TEnum : struct, Enum
        {
            var key = $"AudioSettings.Enum.{typeof(TEnum).Name}.{value}";
            var localizedName = LocalizationManager.Instance.Get(key);
            return localizedName == key
                ? HircSettings.GetEnumDisplayName(value)
                : localizedName;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value;
        }
    }
}
