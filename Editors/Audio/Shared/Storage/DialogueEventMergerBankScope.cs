using System;
using System.Collections.Generic;
using System.Linq;
using Editors.Audio.Shared.GameInformation.Warhammer3;

namespace Editors.Audio.Shared.Storage
{
    internal enum DialogueEventMergerBankScopeKind
    {
        InternalLanguage,
        SpecificLanguage,
        AllVoiceLanguages
    }

    internal readonly record struct DialogueEventMergerBankScope(
        DialogueEventMergerBankScopeKind Kind,
        string Language = null);

    internal static class DialogueEventMergerBankScopeResolver
    {
        private static readonly IReadOnlyDictionary<string, string>
            VoiceLanguageByFolder = Wh3LanguageInformation
                .GetAllLanguages()
                .Where(language => !string.Equals(
                    language,
                    Wh3LanguageInformation.GetLanguageAsString(
                        Wh3Language.Sfx),
                    StringComparison.OrdinalIgnoreCase))
                .ToDictionary(
                    language => language,
                    language => language,
                    StringComparer.OrdinalIgnoreCase);
        private static readonly IReadOnlyCollection<string> VoiceLanguageNames =
            VoiceLanguageByFolder.Values.ToList();

        public static IReadOnlyCollection<string> VoiceLanguages =>
            VoiceLanguageNames;

        public static DialogueEventMergerBankScope Resolve(string bankPath)
        {
            var pathParts = bankPath.Split(
                ['\\', '/'],
                StringSplitOptions.RemoveEmptyEntries);
            for (var index = 0; index < pathParts.Length - 1; index++)
            {
                if (!string.Equals(
                        pathParts[index],
                        "audio",
                        StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        pathParts[index + 1],
                        "wwise",
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var relativePartCount = pathParts.Length - index - 2;
                if (relativePartCount == 1)
                {
                    return new DialogueEventMergerBankScope(
                        DialogueEventMergerBankScopeKind.AllVoiceLanguages);
                }

                if (relativePartCount == 2 &&
                    VoiceLanguageByFolder.TryGetValue(
                        pathParts[index + 2],
                        out var language))
                {
                    return new DialogueEventMergerBankScope(
                        DialogueEventMergerBankScopeKind.SpecificLanguage,
                        language);
                }

                break;
            }

            return new DialogueEventMergerBankScope(
                DialogueEventMergerBankScopeKind.InternalLanguage);
        }
    }
}
