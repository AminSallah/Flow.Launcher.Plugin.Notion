
using System.Collections.Generic;
using System.Linq;

namespace Flow.Launcher.Plugin.Notion.ViewModels
{
    public class SettingsViewModel : BaseModel
    {

        public SettingsViewModel(Settings settings)
        {
            Settings = settings;
        }

        public Settings Settings { get; set; }

        public int HiddenItemsCount
        {
            get => Main.HiddenItems.Count;
            set
            {
                OnPropertyChanged(nameof(HiddenItemsCount));
            }
        }
        public int CachedFailedRequests
        {
            get => Main._apiCacheManager.cachedFunctions.Count;
            set
            {
                OnPropertyChanged(nameof(CachedFailedRequests));
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

        public bool _notSelected = false;

        public bool NotSelected
        {
            get
            {
                return _notSelected;
            }
            set
            {
                _notSelected = value;
                OnPropertyChanged();
            }
        }

        public List<string> Databases
        {
            get => Settings.RelationDatabases;
            set
            {
                Settings.RelationDatabases = value;
                OnPropertyChanged();


            }
        }

        public List<string> DatabasesIds
        {
            get => Settings.RelationDatabasesIds;
            set => Settings.RelationDatabasesIds = value;
        }



    }
}