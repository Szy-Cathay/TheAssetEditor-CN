using Microsoft.Xna.Framework;

namespace Editors.VfxEditor;

// Piecewise attachment samples preserve particle birth positions when the player seeks back.
public sealed class VfxAttachmentHistory
{
    private readonly List<(float Time, Matrix Matrix)> _samples = [];
    public float EndTime => _samples.Count == 0 ? 0 : _samples[^1].Time;
    public int Count => _samples.Count;
    public bool IsConstant => _samples.Count <= 1 || _samples.All(sample => sample.Matrix == _samples[0].Matrix);
    public void Reset(Matrix matrix)
    {
        _samples.Clear();
        _samples.Add((0, matrix));
    }
    public void Record(float time, Matrix matrix)
    {
        if (!float.IsFinite(time) || time < 0) throw new ArgumentOutOfRangeException(nameof(time));
        if (_samples.Count == 0) Reset(matrix);
        if (time < EndTime) Truncate(time);
        if (_samples[^1].Time == time)
        {
            if (_samples[^1].Matrix == matrix) return;
            // Keep both sides of a paused teleport; earlier births must not interpolate toward it.
            if (_samples.Count >= 2 && _samples[^2].Time == time) _samples[^1] = (time, matrix);
            else _samples.Add((time, matrix));
        }
        else if (_samples.Count >= 2 && _samples[^1].Matrix == matrix && _samples[^2].Matrix == matrix)
            _samples[^1] = (time, matrix);
        else _samples.Add((time, matrix));
    }
    public void Truncate(float time)
    {
        var matrix = SampleCurrent(time);
        _samples.RemoveAll(sample => sample.Time > time);
        if (_samples.Count == 0 || _samples[^1].Time < time) _samples.Add((time, matrix));
    }
    public Matrix Sample(float time) => Sample(time, false);
    public Matrix SampleCurrent(float time) => Sample(time, true);
    public float? NextSampleTime(float time)
    {
        var low = 0; var high = _samples.Count;
        while (low < high)
        {
            var middle = (high + low) / 2;
            if (_samples[middle].Time <= time) low = middle + 1;
            else high = middle;
        }
        return low < _samples.Count ? _samples[low].Time : null;
    }
    private Matrix Sample(float time, bool current)
    {
        if (_samples.Count == 0) return Matrix.Identity;
        if (time < _samples[0].Time) return _samples[0].Matrix;
        if (time > EndTime) return _samples[^1].Matrix;
        var low = 0; var high = _samples.Count;
        while (low < high)
        {
            var middle = (high + low) / 2;
            if (_samples[middle].Time < time) low = middle + 1;
            else high = middle;
        }
        if (_samples[low].Time == time)
        {
            if (current) while (low + 1 < _samples.Count && _samples[low + 1].Time == time) low++;
            return _samples[low].Matrix;
        }
        var first = _samples[low - 1]; var second = _samples[low];
        var phase = (time - first.Time) / (second.Time - first.Time);
        if (first.Matrix.Decompose(out var firstScale, out var firstRotation, out var firstPosition)
            && second.Matrix.Decompose(out var secondScale, out var secondRotation, out var secondPosition))
            return Matrix.CreateScale(Vector3.Lerp(firstScale, secondScale, phase))
                * Matrix.CreateFromQuaternion(Quaternion.Slerp(firstRotation, secondRotation, phase))
                * Matrix.CreateTranslation(Vector3.Lerp(firstPosition, secondPosition, phase));
        return Matrix.Lerp(first.Matrix, second.Matrix, phase);
    }
}
