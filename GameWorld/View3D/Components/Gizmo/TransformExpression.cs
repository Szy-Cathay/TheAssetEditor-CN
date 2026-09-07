using System;
using System.Collections.Generic;
using System.Globalization;

namespace GameWorld.Core.Components.Gizmo;

internal static class TransformExpression
{
    public static bool TryEvaluate(string text, out float value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text) || text.Length > 512)
            return false;
        try
        {
            var parser = new Parser(text);
            var result = parser.Sum();
            if (!parser.AtEnd || !double.IsFinite(result) || Math.Abs(result) > float.MaxValue)
                return false;
            value = (float)result;
            return true;
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
        catch (OverflowException) { return false; }
    }

    private sealed class Parser(string text)
    {
        private int _position;
        private int _depth;
        public bool AtEnd { get { SkipSpace(); return _position == text.Length; } }

        private void SkipSpace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
                _position++;
        }

        private bool Take(string token)
        {
            SkipSpace();
            if (!text.AsSpan(_position).StartsWith(token, StringComparison.Ordinal))
                return false;
            _position += token.Length;
            return true;
        }

        public double Sum()
        {
            var value = Product();
            while (true)
            {
                if (Take("+")) value += Product();
                else if (Take("-")) value -= Product();
                else return value;
            }
        }

        private double Product()
        {
            var value = Unary();
            while (true)
            {
                if (Take("*")) value *= Unary();
                else if (Take("//")) value = Math.Floor(value / Unary());
                else if (Take("/")) value /= Unary();
                else if (Take("%"))
                {
                    var divisor = Unary();
                    value -= Math.Floor(value / divisor) * divisor;
                }
                else return value;
            }
        }

        private double Unary()
        {
            if (++_depth > 32) throw new FormatException();
            double value;
            if (Take("+")) value = Unary();
            else if (Take("-")) value = -Unary();
            else
            {
                value = Atom();
                if (Take("**") || Take("^")) value = Math.Pow(value, Unary());
            }
            _depth--;
            return value;
        }

        private double Atom()
        {
            if (Take("("))
            {
                var value = Sum();
                if (!Take(")")) throw new FormatException();
                return value;
            }
            SkipSpace();
            var start = _position;
            if (_position < text.Length && char.IsLetter(text[_position]))
            {
                while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '.'))
                    _position++;
                var name = text[start.._position].ToLowerInvariant();
                if (name.StartsWith("math.", StringComparison.Ordinal)) name = name[5..];
                if (!Take("("))
                    return name switch { "pi" => Math.PI, "tau" => Math.Tau, "e" => Math.E, _ => throw new FormatException() };
                var args = new List<double>();
                do
                {
                    if (args.Count == 8) throw new FormatException();
                    args.Add(Sum());
                } while (Take(","));
                if (!Take(")")) throw new FormatException();
                return Function(name, args);
            }
            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] == '.'))
                _position++;
            if (_position < text.Length && text[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < text.Length && text[_position] is '+' or '-') _position++;
                while (_position < text.Length && char.IsAsciiDigit(text[_position])) _position++;
            }
            return double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException();
        }

        private static double Function(string name, List<double> args)
        {
            if (name is "min" or "max")
            {
                var value = args[0];
                foreach (var arg in args) value = name == "min" ? Math.Min(value, arg) : Math.Max(value, arg);
                return value;
            }
            if (args.Count == 2)
                return name switch
                {
                    "pow" => Math.Pow(args[0], args[1]),
                    "atan2" => Math.Atan2(args[0], args[1]),
                    "log" => Math.Log(args[0], args[1]),
                    "round" when args[1] == Math.Truncate(args[1]) && args[1] is >= 0 and <= 15 => Math.Round(args[0], (int)args[1]),
                    _ => throw new FormatException()
                };
            if (args.Count != 1) throw new FormatException();
            var x = args[0];
            return name switch
            {
                "sin" => Math.Sin(x), "cos" => Math.Cos(x), "tan" => Math.Tan(x),
                "asin" => Math.Asin(x), "acos" => Math.Acos(x), "atan" => Math.Atan(x),
                "sqrt" => Math.Sqrt(x), "abs" => Math.Abs(x), "exp" => Math.Exp(x),
                "log" or "ln" => Math.Log(x), "log10" => Math.Log10(x), "log2" => Math.Log2(x),
                "floor" => Math.Floor(x), "ceil" => Math.Ceiling(x), "round" => Math.Round(x),
                "radians" => x * Math.PI / 180, "degrees" => x * 180 / Math.PI,
                _ => throw new FormatException()
            };
        }
    }
}
