using System;

namespace TrueShuffle
{
    public class ValueListener<T>
    {
        private T _value;
        public EventHandler<ValueListenerEventArgs<T>> Action;

        public T Value
        {
            get => _value;
            set
            {
                T oldValue = _value;
                _value = value;
                Action?.Invoke(this, new ValueListenerEventArgs<T>(oldValue));
            }
        }
    }

    public class ValueListenerEventArgs<T> : EventArgs
    {
        public readonly T OldValue;

        public ValueListenerEventArgs(T oldValue)
        {
            OldValue = oldValue;
        }
    }
}