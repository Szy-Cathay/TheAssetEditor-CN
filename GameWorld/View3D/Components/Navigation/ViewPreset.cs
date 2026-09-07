using Microsoft.Xna.Framework;
using System;

namespace GameWorld.Core.Components.Navigation
{
    /// <summary>
    /// View preset types (corresponding to Blender's numpad views)
    /// </summary>
    public enum ViewPresetType
    {
        Perspective,    // Free perspective view
        Front,          // Numpad 1
        Back,           // Ctrl + Numpad 1
        Right,          // Numpad 3
        Left,           // Ctrl + Numpad 3
        Top,            // Numpad 7
        Bottom,         // Ctrl + Numpad 7
    }

    /// <summary>
    /// View preset data - Yaw/Pitch values for each orthographic view
    /// Based on Blender's view3d_navigate_view_axis.cc
    /// </summary>
    public static class ViewPresets
    {
        /// <summary>
        /// Get Yaw and Pitch for a specific view preset
        /// </summary>
        public static (float yaw, float pitch) GetViewAngles(ViewPresetType viewType)
        {
            return viewType switch
            {
                ViewPresetType.Perspective => (0.8f, 0.32f),  // Default angled view
                ViewPresetType.Front => (0f, 0f),              // Looking at -Z
                ViewPresetType.Back => (MathHelper.Pi, 0f),    // Looking at +Z
                ViewPresetType.Right => (MathHelper.PiOver2, 0f),  // Looking at -X
                ViewPresetType.Left => (-MathHelper.PiOver2, 0f),    // Looking at +X
                ViewPresetType.Top => (0f, -MathHelper.PiOver2),   // Looking down (-Y)
                ViewPresetType.Bottom => (0f, MathHelper.PiOver2), // Looking up (+Y)
                _ => (0.8f, 0.32f)
            };
        }

        /// <summary>
        /// Detect if current camera angles match a view preset
        /// </summary>
        public static ViewPresetType? DetectViewPreset(float yaw, float pitch, float threshold = 0.1f)
        {
            float normalizedYaw = MathHelper.WrapAngle(yaw);
            pitch = MathHelper.WrapAngle(pitch);
            float absPitch = Math.Abs(pitch);

            // Check top/bottom first (pitch is dominant)
            if (Math.Abs(pitch + MathHelper.PiOver2) < threshold + 0.01f)
                return ViewPresetType.Top;
            if (Math.Abs(pitch - MathHelper.PiOver2) < threshold + 0.01f)
                return ViewPresetType.Bottom;

            // Check horizontal views (pitch near 0)
            if (absPitch < threshold)
            {
                if (Math.Abs(normalizedYaw) < threshold)
                    return ViewPresetType.Front;
                if (Math.Abs(normalizedYaw - MathHelper.Pi) < threshold ||
                    Math.Abs(normalizedYaw + MathHelper.Pi) < threshold)
                    return ViewPresetType.Back;
                if (Math.Abs(normalizedYaw - MathHelper.PiOver2) < threshold)
                    return ViewPresetType.Right;
                if (Math.Abs(normalizedYaw + MathHelper.PiOver2) < threshold)
                    return ViewPresetType.Left;
            }

            return null; // Not a preset view
        }

        public static ViewPresetType ResolveRepeatedAxisView(
            ViewPresetType requestedView,
            ViewPresetType currentView)
        {
            if (requestedView != currentView)
                return requestedView;

            return requestedView switch
            {
                ViewPresetType.Front => ViewPresetType.Back,
                ViewPresetType.Back => ViewPresetType.Front,
                ViewPresetType.Right => ViewPresetType.Left,
                ViewPresetType.Left => ViewPresetType.Right,
                ViewPresetType.Top => ViewPresetType.Bottom,
                ViewPresetType.Bottom => ViewPresetType.Top,
                _ => requestedView
            };
        }

        /// <summary>
        /// Get the view preset for clicking on an axis endpoint
        /// +X -> Right view, -X -> Left view
        /// +Y -> Top view, -Y -> Bottom view
        /// +Z -> Front view, -Z -> Back view
        /// </summary>
        public static ViewPresetType AxisToViewPreset(NavigationAxis axis)
        {
            return axis switch
            {
                NavigationAxis.PosX => ViewPresetType.Right,
                NavigationAxis.NegX => ViewPresetType.Left,
                NavigationAxis.PosY => ViewPresetType.Top,
                NavigationAxis.NegY => ViewPresetType.Bottom,
                NavigationAxis.PosZ => ViewPresetType.Front,
                NavigationAxis.NegZ => ViewPresetType.Back,
                _ => ViewPresetType.Perspective
            };
        }
    }

    /// <summary>
    /// Navigation axis enum for the gizmo (6 axis endpoints: +X, -X, +Y, -Y, +Z, -Z)
    /// </summary>
    public enum NavigationAxis
    {
        None = 0,
        PosX = 1,   // +X
        NegX = 2,   // -X
        PosY = 3,   // +Y
        NegY = 4,   // -Y
        PosZ = 5,   // +Z
        NegZ = 6,   // -Z
        // Legacy compatibility
        X = 1,
        Y = 3,
        Z = 5
    }
}
