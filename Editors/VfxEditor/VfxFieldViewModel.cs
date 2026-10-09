using System.ComponentModel;
using System.Globalization;
using System.Xml.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Xna.Framework;
using Shared.GameFormats.Vfx;
using Shared.Ui.BaseDialogs.ColourPickerButton;

namespace Editors.VfxEditor;

public sealed class VfxFieldViewModel : ObservableObject
{
    private readonly VfxEditorViewModel _editor;
    public VfxField Model { get; }
    public string Label { get; }
    public string Description { get; }
    public string AdjustmentHint { get; }
    public string RelatedHint { get; }
    public bool HasRelatedHint => RelatedHint.Length > 0;
    public bool HasGuidance { get; }
    public bool IsCommon => HasGuidance && VfxFieldGuidance.CommonOrder(Model.Name) != int.MaxValue;
    public string SearchText => $"{Label} {Model.Name} {Description} {AdjustmentHint} {RelatedHint} {Model.Context} {_editor.Localization.GetOrDefault("Vfx.Search." + Model.Name, "")}";
    public string GestureHint => _editor.Localization.GetOrDefault("Vfx.Gesture." + Model.Name, _editor.Localization.Get("Vfx.Gesture.Generic"));
    public string Category { get; }
    public string Context { get; }
    public string Summary => Curve != null ? _editor.Localization.GetFormat("Vfx.Curve.Summary", Curve.Channels.Count)
        : IsOpaque ? _editor.Localization.Get("Vfx.Preserved")
        : string.Join(" / ", Values.Select(x => x.DisplayText));
    public bool IsOpaque { get; private set; }
    public bool HasValues => Values.Count > 0;
    public bool IsVector { get; }
    public bool HasColours => Colours.Count > 0;
    public bool ShowValues => HasValues && !HasColours;
    public bool ShowCurve => Curve != null && !HasColours;
    public string RawValue => Model.RawValue;
    public List<VfxValueViewModel> Values { get; } = [];
    public List<VfxColourViewModel> Colours { get; } = [];
    public VfxCurveViewModel? Curve { get; }
    public bool CanBrowse => Model.Attributes.Count == 1 && Model.Name is "diffuse" or "normal" or "ModelFile" or "AnimFile";
    public RelayCommand BrowseCommand { get; }
    public string Error
    {
        get
        {
            var inputError = Values.Select(x => x.Error).FirstOrDefault(x => x.Length > 0);
            if (inputError != null) return inputError;
            var min = Model.Attributes.FirstOrDefault(x => x.Name == "min" || x.Name == "scale_min" || x.Name == "start_time_min");
            var max = Model.Attributes.FirstOrDefault(x => x.Name == "max" || x.Name == "scale_max" || x.Name == "start_time_max");
            if (min != null && max != null && VfxCurve.TryNumber(min.Value, out var low)
                && VfxCurve.TryNumber(max.Value, out var high) && low > high)
                return _editor.Localization.Get("Vfx.Range.Invalid");
            return "";
        }
    }
    public bool HasErrors => Error.Length > 0 || (Curve?.HasErrors ?? false);

    public VfxFieldViewModel(VfxField model, VfxEditorViewModel editor)
    {
        Model = model;
        _editor = editor;
        BrowseCommand = new RelayCommand(() =>
        {
            var extensions = Model.Name switch
            {
                "ModelFile" => new List<string> { ".wsmodel", ".rigid_model_v2" },
                "AnimFile" => [".anim"],
                _ => [".dds", ".png", ".tga"],
            };
            var path = _editor.BrowseResource(extensions);
            if (path != null && Values.Count == 1) Values[0].Text = path;
        }, () => CanBrowse);
        var loc = editor.Localization;
        Label = loc.GetOrDefault("Vfx.Field." + model.Name, loc.GetFormat("Vfx.UnknownField", model.Name));
        Category = loc.GetOrDefault("Vfx.Group." + model.Name, "Advanced");
        HasGuidance = loc.GetOrDefault("Vfx.Help." + model.Name, "").Length > 0;
        Description = loc.GetOrDefault("Vfx.Help." + model.Name, loc.Get("Vfx.Help.Unknown"));
        AdjustmentHint = loc.GetOrDefault("Vfx.Tune." + model.Name, loc.Get("Vfx.Tune.Unknown"));
        RelatedHint = loc.GetOrDefault("Vfx.Note." + model.Name, "");
        Context = (model.Quality.Length == 0 ? "" : loc.GetFormat("Vfx.Quality", model.Quality) + " · ")
            + loc.GetOrDefault("Vfx.Context." + model.Context, loc.Get("Vfx.Context.Other"));

        if (model.Name is "revision" or "construction_type")
        {
            IsOpaque = true;
            return;
        }

        if (model.IsText)
        {
            if (VfxCurve.TryParse(model.RawValue, out var curve))
            {
                Curve = new VfxCurveViewModel(curve!, this, editor);
                BuildCurveColours(curve!);
            }
            else
                IsOpaque = true;
        }
        else
        {
            foreach (var attribute in model.Attributes)
            {
                var parts = SplitVector(attribute.Value);
                if (parts != null)
                {
                    IsVector = model.Attributes.Count == 1;
                    for (var i = 0; i < parts.Value.Values.Length; i++)
                    {
                        var axis = i;
                        var prefix = parts.Value.Prefix;
                        Values.Add(new VfxValueViewModel(loc.Get("Vfx.Component." + "xyzw"[axis]), parts.Value.Values[axis], true, false,
                            text =>
                            {
                                var current = SplitVector(attribute.Value)!.Value.Values;
                                current[axis] = text;
                                WriteAttribute(attribute, prefix + "(" + string.Join(',', current) + ")");
                            }, Changed, editor));
                    }
                }
                else
                {
                    var label = model.Attributes.Count == 1 ? loc.Get("Vfx.Value")
                        : loc.GetOrDefault("Vfx.Component." + attribute.Name.LocalName, attribute.Name.LocalName);
                    Values.Add(new VfxValueViewModel(label, attribute.Value, VfxCurve.TryNumber(attribute.Value, out _)
                        || (string?)model.Element.Attribute("type") is "float" or "int",
                        (string?)model.Element.Attribute("type") == "int", text => WriteAttribute(attribute, text), Changed, editor,
                        choices: VfxFieldGuidance.Choices(model.Attributes.Count == 1 ? model.Name : attribute.Name.LocalName, attribute.Value, loc)));
                }
            }
            IsVector |= model.Name is "reference_position" or "reference_rotation"
                || model.Attributes.Count is >= 2 and <= 4
                    && model.Attributes.All(x => x.Name.LocalName is "x" or "y" or "z" or "w");
            BuildTintColour();
        }
    }

    private void WriteAttribute(XAttribute attribute, string value)
    {
        var before = attribute.Value;
        if (before == value) return;
        _editor.Apply(() => attribute.Value = value, () => attribute.Value = before);
    }

    internal void Changed()
    {
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(RawValue));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasErrors));
        foreach (var colour in Colours) colour.Refresh();
        _editor.Changed();
    }

    private void BuildTintColour()
    {
        if (Model.Name is not ("tint_min" or "tint_max")) return;
        var rgb = Model.Attributes.Take(3).ToArray();
        if (rgb.Any(x => !VfxCurve.TryNumber(x.Value, out var v) || v < 0 || v > 1)) return;
        Colours.Add(new VfxColourViewModel(Label,
            () => new Vector3((float)double.Parse(rgb[0].Value, CultureInfo.InvariantCulture),
                (float)double.Parse(rgb[1].Value, CultureInfo.InvariantCulture), (float)double.Parse(rgb[2].Value, CultureInfo.InvariantCulture)),
            colour =>
            {
                var before = rgb.Select(x => x.Value).ToArray();
                var after = new[] { colour.X, colour.Y, colour.Z }.Select(x => x.ToString("G9", CultureInfo.InvariantCulture)).ToArray();
                if (before.SequenceEqual(after)) return;
                _editor.Apply(() => Set(after), () => Set(before));
                for (var i = 0; i < 3; i++) Values[i].Refresh(rgb[i].Value);
                Changed();

                void Set(string[] values) { for (var i = 0; i < 3; i++) rgb[i].Value = values[i]; }
            }));
    }

    private void BuildCurveColours(VfxCurve curve)
    {
        if (Model.Name != "curve_colour") return;
        foreach (var suffix in new[] { "min", "max" })
        {
            var channels = new[] { "r", "g", "b" }.Select(x => curve.Channels.FirstOrDefault(c => c.Name == x + "_" + suffix)).ToArray();
            if (channels.Any(x => x == null || x.Points.Count != 1 || x.Points[0].Value < 0 || x.Points[0].Value > 1)) continue;
            Colours.Add(new VfxColourViewModel(_editor.Localization.Get("Vfx.Colour." + suffix),
                () => new Vector3((float)channels[0]!.Points[0].Value, (float)channels[1]!.Points[0].Value, (float)channels[2]!.Points[0].Value),
                colour =>
                {
                    var before = channels.Select(x => x!.Points[0].Value).ToArray();
                    double[] after = [colour.X, colour.Y, colour.Z];
                    if (before.SequenceEqual(after)) return;
                    var beforeText = Model.Element.Value;
                    Set(after);
                    var afterText = curve.ToString();
                    _editor.Apply(() => Model.Element.Value = afterText, () => Model.Element.Value = beforeText);
                    Curve!.Refresh();
                    Changed();

                    void Set(double[] values)
                    {
                        for (var i = 0; i < 3; i++) channels[i]!.MovePoint(0, channels[i]!.Points[0].Time, values[i]);
                    }
                }));
        }
    }

    private static (string Prefix, string[] Values)? SplitVector(string value)
    {
        var open = value.IndexOf('(');
        if (open < 0 || !value.EndsWith(')')) return null;
        var prefix = value[..open];
        if (prefix is not ("float2" or "float3" or "float4")) return null;
        var parts = value[(open + 1)..^1].Split(',');
        if (parts.Length != prefix[^1] - '0' || parts.Any(x => !VfxCurve.TryNumber(x, out _))) return null;
        return (prefix, parts);
    }
}

public sealed class VfxValueViewModel : ObservableObject, IDataErrorInfo
{
    private readonly Action<string> _write;
    private readonly Action _changed;
    private readonly VfxEditorViewModel _editor;
    private readonly bool _integer;
    private string _text;
    private readonly Func<double, bool>? _validate;
    public string Label { get; }
    public bool IsNumber { get; }
    public bool IsBoolean { get; }
    public IReadOnlyList<VfxChoice> Choices { get; }
    public bool IsChoice => Choices.Count > 0;
    public bool IsInput => !IsBoolean && !IsChoice;
    public string DisplayText => IsBoolean ? _editor.Localization.Get(Boolean ? "Vfx.Enabled" : "Vfx.Disabled")
        : Choices.FirstOrDefault(x => x.Key == Text)?.Label ?? Text;
    public double Minimum { get; }
    public double Maximum { get; }
    public bool HasError => Error.Length > 0;
    public string Error => IsNumber && (!VfxCurve.TryNumber(Text, out var number) || Math.Abs(number) > float.MaxValue
        || (_integer && (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue))
        || (_validate != null && !_validate(number))) ? _editor.Localization.Get("Vfx.Number.Invalid") : "";
    public string this[string columnName] => columnName == nameof(Text) ? Error : "";
    public string Text
    {
        get => _text;
        set
        {
            if (!SetProperty(ref _text, value)) return;
            if (!HasError) _write(value);
            OnPropertyChanged(nameof(Error));
            OnPropertyChanged(nameof(HasError));
            OnPropertyChanged(nameof(Number));
            OnPropertyChanged(nameof(Boolean));
            OnPropertyChanged(nameof(DisplayText));
            _changed();
        }
    }
    public double Number => VfxCurve.TryNumber(Text, out var value) ? value : 0;
    public bool Boolean { get => bool.TryParse(Text, out var result) && result; set => Text = value ? "true" : "false"; }

    public VfxValueViewModel(string label, string value, bool number, bool integer, Action<string> write,
        Action changed, VfxEditorViewModel editor, Func<double, bool>? validate = null, IReadOnlyList<VfxChoice>? choices = null)
    {
        Label = label;
        _text = value;
        IsNumber = number;
        _integer = integer;
        IsBoolean = bool.TryParse(value, out _);
        _write = write;
        _changed = changed;
        _editor = editor;
        _validate = validate;
        Choices = choices ?? [];
        var magnitude = Math.Max(1, Math.Abs(Number) * 2);
        Minimum = Number < 0 ? -magnitude : 0;
        Maximum = magnitude;
    }

    public void SetNumber(double value) => Text = (_integer ? Math.Round(value) : value).ToString("G7", CultureInfo.InvariantCulture);
    public void Refresh(string value)
    {
        _text = value;
        OnPropertyChanged(nameof(Text));
        OnPropertyChanged(nameof(Number));
        OnPropertyChanged(nameof(Error));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(Boolean));
        OnPropertyChanged(nameof(DisplayText));
    }
}

public sealed class VfxColourViewModel
{
    private readonly Func<Vector3> _read;
    private bool _refreshing;
    public string Label { get; }
    public ColourPickerViewModel Picker { get; }

    public VfxColourViewModel(string label, Func<Vector3> read, Action<Vector3> write)
    {
        Label = label;
        _read = read;
        Picker = new ColourPickerViewModel(read(), colour =>
        {
            var current = Vector3.Clamp(read(), Vector3.Zero, Vector3.One);
            var displayed = new Vector3((byte)(current.X * 255f) / 255f, (byte)(current.Y * 255f) / 255f, (byte)(current.Z * 255f) / 255f);
            if (!_refreshing && displayed != colour) write(colour);
        });
    }
    public void Refresh()
    {
        _refreshing = true;
        try { Picker.Set(Vector3.Clamp(_read(), Vector3.Zero, Vector3.One)); }
        finally { _refreshing = false; }
    }
}

public sealed class VfxCurveViewModel : ObservableObject
{
    private readonly VfxCurve _curve;
    private readonly VfxFieldViewModel _field;
    private readonly VfxEditorViewModel _editor;
    private VfxChannelViewModel? _selectedChannel;
    private VfxPointViewModel? _selectedPoint;
    public IReadOnlyList<VfxChannelViewModel> Channels { get; }
    public string MovementHint => _field.GestureHint;
    public string ChannelHint => _editor.Localization.Get(SelectedChannel?.Model.Name.EndsWith("_min", StringComparison.Ordinal) == true ? "Vfx.Curve.MinimumHint"
        : SelectedChannel?.Model.Name.EndsWith("_max", StringComparison.Ordinal) == true ? "Vfx.Curve.MaximumHint" : "Vfx.Curve.SingleHint");
    public string PointSummary => SelectedPoint == null ? "" : _editor.Localization.GetFormat("Vfx.Curve.PointSummary",
        SelectedPoint.Model.Time.ToString("G4", CultureInfo.InvariantCulture), SelectedPoint.Model.Value.ToString("G4", CultureInfo.InvariantCulture));
    public bool HasErrors => Channels.Any(x => x.Points.Any(p => p.Time.HasError || p.Value.HasError));
    public VfxChannelViewModel? SelectedChannel
    {
        get => _selectedChannel;
        set { if (SetProperty(ref _selectedChannel, value)) { SelectedPoint = value?.Points.FirstOrDefault(); OnPropertyChanged(nameof(Revision)); OnPropertyChanged(nameof(ChannelHint)); } }
    }
    public VfxPointViewModel? SelectedPoint
    {
        get => _selectedPoint;
        set { if (SetProperty(ref _selectedPoint, value)) OnPropertyChanged(nameof(PointSummary)); }
    }
    public int Revision { get; private set; }

    public VfxCurveViewModel(VfxCurve curve, VfxFieldViewModel field, VfxEditorViewModel editor)
    {
        _curve = curve;
        _field = field;
        _editor = editor;
        var scalar = curve.Channels.All(x => x.Name is "x_min" or "x_max");
        Channels = curve.Channels.Select(channel => new VfxChannelViewModel(channel,
            scalar ? field.Label + " · " + editor.Localization.Get(channel.Name == "x_min" ? "Vfx.Component.min" : "Vfx.Component.max")
                : editor.Localization.GetOrDefault("Vfx.Channel." + channel.Name, channel.Name),
            channel.Points.Select((point, index) => new VfxPointViewModel(index, point,
                value => Move(channel, index, value, point.Value), value => Move(channel, index, point.Time, value),
                time => channel.CanMovePoint(index, time),
                Changed, editor)).ToArray())).ToArray();
        SelectedChannel = Channels.FirstOrDefault();
    }

    public void MoveSelected(double time, double value)
    {
        if (SelectedChannel == null || SelectedPoint == null) return;
        Move(SelectedChannel.Model, SelectedPoint.Index, time, value);
        Refresh();
    }

    private void Move(VfxCurveChannel channel, int index, double time, double value)
    {
        var beforeTime = channel.Points[index].Time;
        var beforeValue = channel.Points[index].Value;
        if (beforeTime == time && beforeValue == value) return;
        var beforeText = _field.Model.Element.Value;
        channel.MovePoint(index, time, value);
        var afterText = _curve.ToString();
        _editor.Apply(() => _field.Model.Element.Value = afterText, () => _field.Model.Element.Value = beforeText);
        Changed();
    }

    public void Refresh()
    {
        foreach (var channel in Channels)
            foreach (var point in channel.Points)
            {
                point.Time.Refresh(point.Model.Time.ToString("G9", CultureInfo.InvariantCulture));
                point.Value.Refresh(point.Model.Value.ToString("G9", CultureInfo.InvariantCulture));
            }
        Changed();
    }

    private void Changed()
    {
        Revision++;
        OnPropertyChanged(nameof(Revision));
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(PointSummary));
        _field.Changed();
    }
}

public sealed record VfxChannelViewModel(VfxCurveChannel Model, string Label, IReadOnlyList<VfxPointViewModel> Points);

public sealed class VfxPointViewModel
{
    public int Index { get; }
    public VfxCurvePoint Model { get; }
    public string Label { get; }
    public VfxValueViewModel Time { get; }
    public VfxValueViewModel Value { get; }

    public VfxPointViewModel(int index, VfxCurvePoint model, Action<double> setTime, Action<double> setValue,
        Func<double, bool> validateTime, Action changed, VfxEditorViewModel editor)
    {
        Index = index;
        Model = model;
        Label = editor.Localization.GetFormat("Vfx.Curve.Point", index + 1);
        Time = new VfxValueViewModel(editor.Localization.Get("Vfx.Curve.Time"), model.Time.ToString("G9", CultureInfo.InvariantCulture), true, false,
            value => setTime(double.Parse(value, CultureInfo.InvariantCulture)), changed, editor, validateTime);
        Value = new VfxValueViewModel(editor.Localization.Get("Vfx.Value"), model.Value.ToString("G9", CultureInfo.InvariantCulture), true, false,
            value => setValue(double.Parse(value, CultureInfo.InvariantCulture)), changed, editor);
    }
}
