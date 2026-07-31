namespace Editors.KitbasherEditor.ChildEditors.PhotoStudio
{
    public class PhotoStudioSettings
    {
        public float CameraPositionX { get; set; }
        public float CameraPositionY { get; set; }
        public float CameraPositionZ { get; set; }
        public float CameraYaw { get; set; }
        public float CameraPitch { get; set; }
        public float CameraZoom { get; set; }
        public float CameraLookAtX { get; set; }
        public float CameraLookAtY { get; set; }
        public float CameraLookAtZ { get; set; }
        public float LightIntensity { get; set; }
        public float LightColourX { get; set; }
        public float LightColourY { get; set; }
        public float LightColourZ { get; set; }
        public float EnvLightRotationY { get; set; }
        public float DirectLightRotationX { get; set; }
        public float DirectLightRotationY { get; set; }
        public bool DoubleImageResolution { get; set; } = true;
    }
}
