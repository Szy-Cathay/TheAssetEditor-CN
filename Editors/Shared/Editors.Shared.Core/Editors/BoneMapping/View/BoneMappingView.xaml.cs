// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using System.Windows;
using System.Windows.Controls;

namespace CommonControls.Editors.BoneMapping.View
{
    /// <summary>
    /// Interaction logic for BoneMappingView.xaml
    /// </summary>
    public partial class BoneMappingView : UserControl
    {
        public static readonly DependencyProperty LeftSkeletonHeaderProperty = DependencyProperty.Register(
            nameof(LeftSkeletonHeader), typeof(string), typeof(BoneMappingView));
        public static readonly DependencyProperty RightSkeletonHeaderProperty = DependencyProperty.Register(
            nameof(RightSkeletonHeader), typeof(string), typeof(BoneMappingView));
        public static readonly DependencyProperty MappingHintProperty = DependencyProperty.Register(
            nameof(MappingHint), typeof(string), typeof(BoneMappingView));
        public static readonly DependencyProperty CopyToChildrenTextProperty = DependencyProperty.Register(
            nameof(CopyToChildrenText), typeof(string), typeof(BoneMappingView));
        public static readonly DependencyProperty CopyToChildrenToolTipProperty = DependencyProperty.Register(
            nameof(CopyToChildrenToolTip), typeof(string), typeof(BoneMappingView));

        public string? LeftSkeletonHeader
        {
            get => (string?)GetValue(LeftSkeletonHeaderProperty);
            set => SetValue(LeftSkeletonHeaderProperty, value);
        }

        public string? RightSkeletonHeader
        {
            get => (string?)GetValue(RightSkeletonHeaderProperty);
            set => SetValue(RightSkeletonHeaderProperty, value);
        }

        public string? MappingHint
        {
            get => (string?)GetValue(MappingHintProperty);
            set => SetValue(MappingHintProperty, value);
        }

        public string? CopyToChildrenText
        {
            get => (string?)GetValue(CopyToChildrenTextProperty);
            set => SetValue(CopyToChildrenTextProperty, value);
        }

        public string? CopyToChildrenToolTip
        {
            get => (string?)GetValue(CopyToChildrenToolTipProperty);
            set => SetValue(CopyToChildrenToolTipProperty, value);
        }

        public BoneMappingView()
        {
            InitializeComponent();
        }
    }
}
