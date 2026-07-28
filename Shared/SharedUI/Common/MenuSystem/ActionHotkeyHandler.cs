// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace Shared.Ui.Common.MenuSystem
{
    public class ActionHotkeyHandler
    {
        List<MenuAction> _actions = new List<MenuAction>();

        public void Register(MenuAction action)
        {
            if (action.Hotkey != null &&
                _actions.Any(item =>
                    item.Hotkey != null &&
                    item.Hotkey.Key == action.Hotkey.Key &&
                    item.Hotkey.ModifierKeys ==
                    action.Hotkey.ModifierKeys))
            {
                throw new InvalidOperationException(
                    $"Hotkey {action.Hotkey.ModifierKeys}+{action.Hotkey.Key} is already registered.");
            }

            _actions.Add(action);
        }

        public bool TriggerCommand(Key key, ModifierKeys modifierKeys)
        {
            foreach (var item in _actions)
            {
                if (item.Hotkey == null)
                    continue;

                if (item.Hotkey.Key == key &&
                    item.Hotkey.ModifierKeys == modifierKeys &&
                    item.IsActionEnabled.Value)
                {
                    item.TriggerAction();
                    return true;
                }
            }

            return false;
        }


    }
}
