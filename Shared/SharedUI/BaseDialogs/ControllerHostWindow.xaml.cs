// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System;
using CommonControls;
using System.Windows;

namespace CommonControls.BaseDialogs
{
    /// <summary>
    /// Interaction logic for ControllerHostWindow.xaml
    /// </summary>
    public partial class ControllerHostWindow : Window
    {
        public ControllerHostWindow()
        {
            InitializeComponent();
            DarkTitleBarHelper.Enable(this);
        }

        public ControllerHostWindow(bool alwaysTopMost = false, ResizeMode resizeMode = ResizeMode.NoResize)
            : this()
        {
            SizeToContent = SizeToContent.Manual;
            if (alwaysTopMost)
                Deactivated += Window_Deactivated;

            ResizeMode = resizeMode;
        }

        private void Window_Deactivated(object sender, EventArgs e)
        {
            Window window = (Window)sender;
            window.Topmost = true;
        }
    }
}
