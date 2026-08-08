using Microsoft.Xna.Framework;

namespace GameWorld.Core.Rendering;

internal static class EditOverlayStyle
{
    public static readonly Vector3 VertexColour = Vector3.Zero;
    public static readonly Vector3 WireColour = new(
        0.15f,
        0.15f,
        0.15f);
    public static readonly Vector3 SelectedColour = new(
        1.0f,
        0.47f,
        0.0f);
    public static readonly Vector3 KitbashSelectedColour = new(
        2.0f,
        0.65f,
        0.0f);
    public static readonly Vector3 ActiveColour = Vector3.One;

    public const float VertexDiameter = 4.5f;
    public const float VertexSelectionDiameterBoost = 0.0f;
    public const float WireHalfWidth = 0.75f;
    public const float SelectedEdgeHalfWidth = 2.0f;
    public const float ActiveEdgeHalfWidth = 2.25f;
    public const float WireDepthBias = 0.00002f;
    public const float SelectedEdgeDepthBias = 0.00004f;
    public const float ActiveEdgeDepthBias = 0.00006f;
    public const float SelectedFaceOpacity = 0.24f;
    public const float ActiveFaceOpacity = 0.34f;
    public const float SelectedFaceDepthBias = 0.00001f;
    public const float ActiveFaceDepthBias = 0.00003f;
}
