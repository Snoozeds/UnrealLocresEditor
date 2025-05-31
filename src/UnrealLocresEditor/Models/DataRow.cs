using System.ComponentModel;

namespace UnrealLocresEditor.Models
{
    public class DataRow : INotifyPropertyChanged
    {
        private string[] _values;
        public string[] Values
        {
            get => _values;
            set
            {
                if (_values != value)
                {
                    _values = value;
                    OnPropertyChanged(nameof(Values));
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public virtual void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
