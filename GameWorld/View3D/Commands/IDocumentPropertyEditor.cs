namespace GameWorld.Core.Commands
{
    public interface IDocumentPropertyEditor
    {
        void Update<T>(T currentValue, T newValue, Action<T> apply);
        void Update<T>(
            T currentValue,
            T newValue,
            Action<T> updateModel,
            Action<T> updateView);
    }
}
