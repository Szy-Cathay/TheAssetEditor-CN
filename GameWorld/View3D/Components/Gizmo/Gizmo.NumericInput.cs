using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Shared.Core.Services;

namespace GameWorld.Core.Components.Gizmo;

public partial class Gizmo
{
    private readonly string[] _numericFields = ["", "", ""];
    private readonly float[] _numericValues = new float[3];
    private int _numericIndex;
    private int _numericCaret;
    private bool _numericExpressionMode;
    private bool _numericValid = true;
    private bool _numericSelectAll;

    private int NumericFieldCount => ActiveMode == GizmoMode.Rotate ? (IsTrackballRotation ? 2 : 1) :
        ActiveAxis is GizmoAxis.X or GizmoAxis.Y or GizmoAxis.Z ? 1 :
        ActiveAxis == GizmoAxis.None ? 3 : 2;

    private void HandleNumericInput()
    {
        var text = _keyboard.TextInput;
        var control = _keyboard.IsKeyDownOrReleased(Keys.LeftControl) || _keyboard.IsKeyDownOrReleased(Keys.RightControl);
        if (control && _keyboard.IsKeyPressed(Keys.V) && !string.IsNullOrEmpty(text))
        {
            IsInNumericInput = true;
            _numericExpressionMode = true;
        }
        if (text != null)
        {
            foreach (var character in text)
                InsertNumericCharacter(character);
        }
        else
        {
            // Non-text input providers retain the original key-based numeric entry.
            for (var i = 0; i <= 9; i++)
                if (_keyboard.IsKeyReleased(Keys.D0 + i) || _keyboard.IsKeyReleased(Keys.NumPad0 + i))
                    InsertNumericCharacter((char)('0' + i));
            if (_keyboard.IsKeyReleased(Keys.OemMinus) || _keyboard.IsKeyReleased(Keys.Subtract)) InsertNumericCharacter('-');
            if (_keyboard.IsKeyReleased(Keys.OemPeriod) || _keyboard.IsKeyReleased(Keys.Decimal)) InsertNumericCharacter('.');
        }
        if (!IsInNumericInput)
            return;
        _numericIndex = Math.Min(_numericIndex, NumericFieldCount - 1);
        _numericInput = _numericFields[_numericIndex];
        _numericCaret = Math.Min(_numericCaret, _numericInput.Length);
        if (control && _keyboard.IsKeyReleased(Keys.A)) _numericSelectAll = true;
        if (_keyboard.IsKeyReleased(Keys.Left)) { _numericCaret = Math.Max(0, _numericCaret - 1); _numericSelectAll = false; }
        if (_keyboard.IsKeyReleased(Keys.Right)) { _numericCaret = Math.Min(_numericInput.Length, _numericCaret + 1); _numericSelectAll = false; }
        if (_keyboard.IsKeyReleased(Keys.Home)) _numericCaret = 0;
        if (_keyboard.IsKeyReleased(Keys.End)) _numericCaret = _numericInput.Length;
        if (_keyboard.IsKeyReleased(Keys.Back))
        {
            if (_numericInput.Length == 0 && Array.TrueForAll(_numericFields, string.IsNullOrEmpty))
            {
                ResetNumericInput();
                return;
            }
            if (control || _numericSelectAll) { _numericInput = ""; _numericCaret = 0; }
            else if (_numericCaret > 0) _numericInput = _numericInput.Remove(--_numericCaret, 1);
            _numericSelectAll = false;
        }
        if (_keyboard.IsKeyReleased(Keys.Delete))
        {
            if (_numericSelectAll) { _numericInput = ""; _numericCaret = 0; }
            else if (_numericCaret < _numericInput.Length) _numericInput = _numericInput.Remove(_numericCaret, 1);
            _numericSelectAll = false;
        }
        _numericFields[_numericIndex] = _numericInput;
        if (_keyboard.IsKeyReleased(Keys.Tab))
        {
            var reverse = _keyboard.IsKeyDownOrReleased(Keys.LeftShift) || _keyboard.IsKeyDownOrReleased(Keys.RightShift);
            _numericIndex = (_numericIndex + (reverse ? NumericFieldCount - 1 : 1)) % NumericFieldCount;
            _numericInput = _numericFields[_numericIndex];
            _numericCaret = _numericInput.Length;
            _numericSelectAll = true;
        }
        _numericValid = true;
        for (var i = 0; i < NumericFieldCount; i++)
        {
            if (_numericFields[i].Length == 0)
                _numericValues[i] = ActiveMode is GizmoMode.NonUniformScale or GizmoMode.UniformScale ? 1 : 0;
            else if (!TransformExpression.TryEvaluate(_numericFields[i], out _numericValues[i]))
                _numericValid = false;
        }
        _numericValue = _numericValues[0];
    }

    private void InsertNumericCharacter(char character)
    {
        if (character == '=')
        {
            IsInNumericInput = true;
            _numericExpressionMode = true;
            return;
        }
        if (!IsInNumericInput && !(char.IsAsciiDigit(character) || character is '.' or '-' or '(' or '+'))
            return;
        if (!(char.IsAsciiLetterOrDigit(character) || " .,+-*/%^()".Contains(character)))
            return;
        if (!_numericExpressionMode && char.IsLetter(character) && character is not ('e' or 'E'))
            return;
        IsInNumericInput = true;
        if (character is '+' or '*' or '/' or '%' or '^' or '(' or ')' or 'e' or 'E') _numericExpressionMode = true;
        _numericIndex = Math.Min(_numericIndex, NumericFieldCount - 1);
        var field = _numericFields[_numericIndex];
        _numericCaret = Math.Min(_numericCaret, field.Length);
        if (_numericSelectAll) { field = ""; _numericCaret = 0; _numericSelectAll = false; }
        if (character == '-' && !_numericExpressionMode)
        {
            field = field.StartsWith('-') ? field[1..] : "-" + field;
            _numericCaret = field.Length;
        }
        else if (field.Length < 512)
        {
            field = field.Insert(_numericCaret++, character.ToString());
        }
        _numericFields[_numericIndex] = field;
    }

    private void ApplyNumericInput()
    {
        if (!_numericValid)
            return;
        if (ActiveMode == GizmoMode.Rotate)
        {
            if (IsTrackballRotation)
                ApplyTrackballRotationFromInitial(new Vector2(MathHelper.ToRadians(_numericValues[0]), MathHelper.ToRadians(_numericValues[1])));
            else
                ApplyModalRotationFromInitial(MathHelper.ToRadians(_numericValue));
            return;
        }
        var multiple = NumericFieldCount > 1 && (_numericFields[1].Length != 0 || _numericFields[2].Length != 0);
        if (!multiple)
        {
            if (ActiveMode == GizmoMode.Translate)
                ApplyModalTranslationFromInitial(CreateNumericTranslation(_rotationMatrix.Right, _rotationMatrix.Up,
                    _rotationMatrix.Backward, ActiveAxis, _numericValue));
            else
                ApplyModalScaleFromInitial(_numericValue);
            return;
        }
        var value = new Vector3(_numericValues[0], _numericValues[1], _numericValues[2]);
        var neutral = ActiveMode == GizmoMode.Translate ? 0 : 1;
        value = ActiveAxis switch
        {
            GizmoAxis.XY => new Vector3(value.X, value.Y, neutral),
            GizmoAxis.XZ => new Vector3(value.X, neutral, value.Y),
            GizmoAxis.YZ => new Vector3(neutral, value.X, value.Y),
            _ => value
        };
        if (ActiveMode == GizmoMode.Translate)
            ApplyModalTranslationFromInitial(Vector3.TransformNormal(value, _rotationMatrix));
        else
            RequestModalPreviewReplacement(ModalPreviewReplacement.Scale(value - Vector3.One, ActivePivot, _rotationMatrix));
    }

    private string NumericInputLabel()
    {
        var fields = new string[NumericFieldCount];
        for (var i = 0; i < fields.Length; i++)
        {
            var field = _numericFields[i];
            fields[i] = i == _numericIndex ? "[" + field.Insert(Math.Min(_numericCaret, field.Length), "|") + "]" : field;
        }
        var label = string.Join("; ", fields);
        return _numericValid ? label : label + "\n" + LocalizationManager.Instance.Get("Viewport.Transform.InvalidExpression");
    }

    private void ResetNumericInput()
    {
        IsInNumericInput = false;
        _numericInput = "";
        _numericValue = 0;
        Array.Fill(_numericFields, "");
        Array.Clear(_numericValues);
        _numericIndex = _numericCaret = 0;
        _numericExpressionMode = _numericSelectAll = false;
        _numericValid = true;
    }
}
