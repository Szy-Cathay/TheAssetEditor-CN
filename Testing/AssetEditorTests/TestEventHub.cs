using Shared.Core.Events;

namespace AssetEditorTests
{
    internal sealed class TestEventHub : IEventHub, IGlobalEventHub
    {
        private readonly Dictionary<Type, List<(object Owner, Delegate Callback)>>
            _callbacks = [];

        public void PublishGlobalEvent<T>(T e) => Publish(e);

        public void Publish<T>(T e)
        {
            if (!_callbacks.TryGetValue(typeof(T), out var callbacks))
                return;

            foreach (var callback in callbacks.ToList())
                ((Action<T>)callback.Callback)(e);
        }

        public void Register<T>(object owner, Action<T> action)
        {
            if (!_callbacks.TryGetValue(typeof(T), out var callbacks))
            {
                callbacks = [];
                _callbacks.Add(typeof(T), callbacks);
            }

            callbacks.Add((owner, action));
        }

        public void UnRegister(object owner)
        {
            foreach (var callbacks in _callbacks.Values)
                callbacks.RemoveAll(callback => callback.Owner == owner);
        }
    }
}
