
namespace Flow.Launcher.Plugin.Notion.ViewModels
{
    public class SettingsViewModel : BaseModel
    {

        public SettingsViewModel(Settings settings)
        {
            Settings = settings;
        }

        public Settings Settings { get; }

        public int HiddenItemsCount
        {
            get => Main.HiddenItems.Count;
            // Add OnPropertyChanged for HiddenItemsCount
            set
            {
                OnPropertyChanged(nameof(HiddenItemsCount));
            }
        }

        public string InernalInegrationToken
        {
            get => new string('*', Settings.InernalInegrationToken.Length);
            set
            {
                Settings.InernalInegrationToken = value;
                this.OnPropertyChanged();
            }
        }
    }
}