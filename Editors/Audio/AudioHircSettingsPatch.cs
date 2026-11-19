using System;
using System.Linq;
using System.Text.Json.Nodes;
using HircSettings = Editors.Audio.Shared.AudioProject.Models.HircSettings;
using Editors.Audio.Shared.Wwise;
using static Shared.Core.ModAssistant.AssistantJson;

namespace Editors.Audio;

public static class AudioHircSettingsPatch
{
    public static JsonObject Schema() => global::Shared.Core.ModAssistant.AssistantJson.Schema(
        ("container_type", Str("multiple sounds only", Enum.GetNames<ContainerType>()), false),
        ("random_type", Str("Random container only", Enum.GetNames<RandomType>()), false),
        ("enable_repetition_interval", Bool(), false), ("repetition_interval", Num(1, 999, "items", true), false),
        ("playlist_end", Str("Sequence container only", Enum.GetNames<PlaylistEndBehaviour>()), false),
        ("always_reset_playlist", Bool(), false), ("play_mode", Str("", Enum.GetNames<PlayMode>()), false),
        ("looping_type", Str("single sound or Continuous container", Enum.GetNames<LoopingType>()), false),
        ("number_of_loops", Num(1, 32767, "FiniteLooping count", true), false),
        ("transition_type", Str("Continuous container only", Enum.GetNames<TransitionType>()), false),
        ("transition_duration", Num(0, 3600, "seconds"), false));

    public static HircSettings Apply(HircSettings source, JsonObject patch, bool container)
    {
        Validate(patch, Schema());
        if (patch.Count == 0) throw new ArgumentException(Text("InvalidParameters"));
        var value = HircSettings.CreateRandomSequenceContainerSettings(source);
        foreach (var field in patch)
        {
            switch (field.Key)
            {
                case "container_type": value.ContainerType = Enum.Parse<ContainerType>(patch.String(field.Key)); break;
                case "random_type": value.RandomType = Enum.Parse<RandomType>(patch.String(field.Key)); break;
                case "enable_repetition_interval": value.EnableRepetitionInterval = patch.Boolean(field.Key); break;
                case "repetition_interval": value.RepetitionInterval = (uint)patch.Integer(field.Key); break;
                case "playlist_end": value.PlaylistEndBehaviour = Enum.Parse<PlaylistEndBehaviour>(patch.String(field.Key)); break;
                case "always_reset_playlist": value.AlwaysResetPlaylist = patch.Boolean(field.Key); break;
                case "play_mode": value.PlayMode = Enum.Parse<PlayMode>(patch.String(field.Key)); break;
                case "looping_type": value.LoopingType = Enum.Parse<LoopingType>(patch.String(field.Key)); break;
                case "number_of_loops": value.NumberOfLoops = (uint)patch.Integer(field.Key); break;
                case "transition_type": value.TransitionType = Enum.Parse<TransitionType>(patch.String(field.Key)); break;
                case "transition_duration": value.TransitionDuration = (decimal)patch.Number(field.Key); break;
                default: throw new ArgumentException(Text("InvalidParameters"));
            }
        }
        foreach (var key in patch.Select(p => p.Key))
        {
            var available = key switch
            {
                "looping_type" => !container || value.PlayMode == PlayMode.Continuous,
                "number_of_loops" => (!container || value.PlayMode == PlayMode.Continuous) && value.LoopingType == LoopingType.FiniteLooping,
                "random_type" or "enable_repetition_interval" or "repetition_interval" => container && value.ContainerType == ContainerType.Random,
                "playlist_end" => container && value.ContainerType == ContainerType.Sequence,
                "always_reset_playlist" => container && value.ContainerType == ContainerType.Sequence && value.PlayMode == PlayMode.Continuous,
                "transition_type" => container && value.PlayMode == PlayMode.Continuous,
                "transition_duration" => container && value.PlayMode == PlayMode.Continuous && value.TransitionType != TransitionType.Disabled,
                _ => container,
            };
            if (!available) throw new ArgumentException(Text("AudioSettingUnavailable") + ": " + key);
        }
        return value;
    }
}
