using System;
using System.Collections.Generic;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Shared.Core.Misc;

namespace Shared.Ui.Common.MenuSystem
{
    public class MenuAction
    {
        public Hotkey Hotkey { get; set; }
        public ICommand Command { get; set; }
        public NotifyAttr<string> ToolTipAttribute { get; set; } = new NotifyAttr<string>();
        public ActionEnabledRule EnableRule { get; set; }
        public NotifyAttr<bool> IsActionEnabled { get; set; } = new NotifyAttr<bool>(true);

        public void TriggerAction()
        {
            if (ActionTriggeredCallback != null)
                ActionTriggeredCallback();
            TriggerInternal();

        }

        public virtual void TriggerInternal()
        { }

        public Action ActionTriggeredCallback { get; set; }


        public MenuAction()
        {
            Command = new RelayCommand(TriggerAction);
        }

        public string _toopTipText;

        public string ToolTip
        {
            set
            {
                _toopTipText = value;
                UpdateToolTip();
            }
        }

        public string ToopTipText()
        {
            if (Hotkey == null)
                return "";

            if (Hotkey.ModifierKeys == ModifierKeys.None)
                return $" ({FormatKey(Hotkey.Key)})";

            var keys = new List<string>();
            if (Hotkey.ModifierKeys.HasFlag(ModifierKeys.Control))
                keys.Add("Ctrl");
            if (Hotkey.ModifierKeys.HasFlag(ModifierKeys.Shift))
                keys.Add("Shift");
            if (Hotkey.ModifierKeys.HasFlag(ModifierKeys.Alt))
                keys.Add("Alt");
            if (Hotkey.ModifierKeys.HasFlag(ModifierKeys.Windows))
                keys.Add("Win");
            keys.Add(FormatKey(Hotkey.Key));
            return $" ({string.Join("+", keys)})";
        }

        static string FormatKey(Key key) => key switch
        {
            Key.Add => "Num+",
            Key.Subtract => "Num-",
            _ => key.ToString()
        };

        public void UpdateToolTip()
        {
            ToolTipAttribute.Value = _toopTipText + ToopTipText();
        }
    }
}
