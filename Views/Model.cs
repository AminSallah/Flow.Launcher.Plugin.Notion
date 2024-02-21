using System.Runtime.CompilerServices;

namespace Flow.Launcher.Plugin.Notion
{
    public class Model : BaseModel
    {
        protected bool SetProperty<T>(ref T field, T newValue, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, newValue))
                return false;

            field = newValue;
            OnPropertyChanged(propertyName);
            return true;
        }
    }
}
