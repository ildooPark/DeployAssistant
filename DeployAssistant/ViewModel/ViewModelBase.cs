using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace DeployAssistant.ViewModel
{
    public class ViewModelBase : INotifyPropertyChanged, IDisposable
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private readonly List<Action> _unsubscribers = new();
        private bool _disposed;

        protected virtual void OnPropertyChanged([CallerMemberName] string? property = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));

        /// <summary>
        /// Sets the backing field to <paramref name="value"/> and raises
        /// <see cref="PropertyChanged"/> when the value actually changes.
        /// Returns <c>true</c> if the value changed.
        /// </summary>
        protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(propertyName);
            return true;
        }

        /// <summary>
        /// Registers an unsubscribe action that runs on Dispose. Use when subscribing
        /// to long-lived events (e.g. MetaDataManager events) from a per-window VM.
        /// </summary>
        protected void TrackUnsubscribe(Action unsubscribe) => _unsubscribers.Add(unsubscribe);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var u in _unsubscribers) u();
            _unsubscribers.Clear();
            GC.SuppressFinalize(this);
        }
    }
}
