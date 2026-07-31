using System;
using System.Collections.Generic;
using System.Linq;
using Shared.GameFormats.Esf;

namespace Editors.CscEditor.Data
{
    /// <summary>
    /// One keyframe of a CSC parameter curve:
    /// F32(x=time), F32(y=value), Ascii(mode), Coord2d(tangentIn), Coord2d(tangentOut), Ascii(mode)
    /// on the wire. Mode vocabulary seen in real files: "constant", "linear", "bezier_c".
    /// </summary>
    public class CscKeyframe
    {
        public float Time { get; set; }
        public float Value { get; set; }
        public string ModeIn { get; set; } = "linear";
        public Coord2d TangentIn { get; set; }
        public Coord2d TangentOut { get; set; }
        public string ModeOut { get; set; } = "linear";

        public CscKeyframe Clone() => new()
        {
            Time = Time,
            Value = Value,
            ModeIn = ModeIn,
            TangentIn = TangentIn,
            TangentOut = TangentOut,
            ModeOut = ModeOut,
        };
    }

    /// <summary>
    /// One channel of a parameter group. The header F32 is the channel's live value only when it
    /// has zero keyframes; any keyframe overrides it entirely (in-game confirmed behaviour).
    /// <see cref="HasModeTags"/> tracks which of the two wire encodings the channel came from -
    /// the modern header form (F32, Ascii, Ascii) or the older bare-F32 form - so it round-trips
    /// in the same shape it was read in.
    /// </summary>
    public class CscChannel
    {
        public float Header { get; set; }
        public string ModeA { get; set; } = "constant";
        public string ModeB { get; set; } = "constant";
        public bool HasModeTags { get; set; } = true;
        public List<CscKeyframe> Keyframes { get; } = [];

        public float Evaluate(float time)
        {
            if (Keyframes.Count == 0)
                return Header;
            if (time <= Keyframes[0].Time)
                return Keyframes[0].Value;
            if (time >= Keyframes[^1].Time)
                return Keyframes[^1].Value;

            var i = 0;
            while (i < Keyframes.Count - 1 && Keyframes[i + 1].Time < time)
                i++;

            var a = Keyframes[i];
            var b = Keyframes[i + 1];
            var span = b.Time - a.Time;
            if (span <= 0)
                return b.Value;

            if (a.ModeOut.StartsWith("constant", StringComparison.OrdinalIgnoreCase))
                return a.Value;
            if (a.ModeOut.StartsWith("linear", StringComparison.OrdinalIgnoreCase))
                return a.Value + (b.Value - a.Value) * ((time - a.Time) / span);

            return EvaluateBezier(a, b, time);
        }

        /// <summary>
        /// Cubic bezier segment with the keyframes' tangent handles as relative control-point
        /// offsets: P0=(a.t,a.v), P1=P0+a.TangentOut, P2=P3+b.TangentIn, P3=(b.t,b.v). Control X
        /// is clamped inside [a.t, b.t] so x(s) stays monotonic and the x->s inversion (bisection)
        /// is well defined.
        /// </summary>
        static float EvaluateBezier(CscKeyframe a, CscKeyframe b, float time)
        {
            var x0 = a.Time;
            var y0 = a.Value;
            var x3 = b.Time;
            var y3 = b.Value;
            var x1 = Math.Clamp(x0 + a.TangentOut.X, x0, x3);
            var y1 = y0 + a.TangentOut.Y;
            var x2 = Math.Clamp(x3 + b.TangentIn.X, x0, x3);
            var y2 = y3 + b.TangentIn.Y;

            float BezierX(float s)
            {
                var u = 1 - s;
                return u * u * u * x0 + 3 * u * u * s * x1 + 3 * u * s * s * x2 + s * s * s * x3;
            }

            float lo = 0, hi = 1;
            for (var iter = 0; iter < 32; iter++)
            {
                var mid = (lo + hi) * 0.5f;
                if (BezierX(mid) < time)
                    lo = mid;
                else
                    hi = mid;
            }

            var t = (lo + hi) * 0.5f;
            var v = 1 - t;
            return v * v * v * y0 + 3 * v * v * t * y1 + 3 * v * t * t * y2 + t * t * t * y3;
        }

        /// <summary>The value shown/edited when treating the channel as static (no time context).</summary>
        public float StaticValue => Keyframes.Count == 0 ? Header : Keyframes[0].Value;

        /// <summary>Adds a delta to the channel everywhere - header and every keyframe - so an
        /// animated channel keeps its motion but shifts its base value.</summary>
        public void OffsetAll(float delta)
        {
            Header += delta;
            foreach (var k in Keyframes)
                k.Value += delta;
        }

        public void ScaleAll(float factor)
        {
            Header *= factor;
            foreach (var k in Keyframes)
                k.Value *= factor;
        }

        public void SetStatic(float value)
        {
            if (Keyframes.Count == 0)
                Header = value;
            else
                OffsetAll(value - StaticValue);
        }

        public CscKeyframe AddKeyframe(float time, float value)
        {
            var key = new CscKeyframe { Time = time, Value = value, ModeIn = "linear", ModeOut = "linear" };
            Keyframes.Add(key);
            SortKeyframes();
            return key;
        }

        public void SortKeyframes() => Keyframes.Sort((a, b) => a.Time.CompareTo(b.Time));

        public CscChannel Clone()
        {
            var clone = new CscChannel { Header = Header, ModeA = ModeA, ModeB = ModeB, HasModeTags = HasModeTags };
            clone.Keyframes.AddRange(Keyframes.Select(k => k.Clone()));
            return clone;
        }
    }

    /// <summary>
    /// A parameter group: U32(marker) + one or more channels. The marker is load-bearing
    /// (flipping it from 1 crashed the game's loader in testing) so it is preserved verbatim.
    /// </summary>
    public class CscChannelGroup
    {
        public uint Marker { get; set; } = 1;
        public List<CscChannel> Channels { get; } = [];

        public static CscChannelGroup CreateStatic(params float[] channelHeaders)
        {
            var group = new CscChannelGroup();
            foreach (var header in channelHeaders)
                group.Channels.Add(new CscChannel { Header = header });
            return group;
        }

        public CscChannelGroup Clone()
        {
            var clone = new CscChannelGroup { Marker = Marker };
            clone.Channels.AddRange(Channels.Select(c => c.Clone()));
            return clone;
        }
    }
}
