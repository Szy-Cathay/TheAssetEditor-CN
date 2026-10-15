namespace GameWorld.Core.Components
{
    public sealed class ViewportInteractionCapabilities
    {
        // Scoped to one editor; preview scenes never opt in.
        public bool ModelEditingEnabled { get; private set; }

        public void EnableModelEditing() => ModelEditingEnabled = true;
    }
}
