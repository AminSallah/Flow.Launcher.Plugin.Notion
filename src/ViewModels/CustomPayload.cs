using Flow.Launcher.Plugin.Notion.Views;

namespace Flow.Launcher.Plugin.Notion.ViewModels
{
    public class CustomPayload : BaseModel
    {
        private string _name = string.Empty;
        private string _subTitle = string.Empty;
        private string _database;
        private string _json;
        private string _icopath = System.IO.Path.Combine(
                          System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData),
                          "FlowLauncher", "Plugins", "Flow.Launcher.Plugin.Notion", "Images", "app.png");
        private bool _cachable = false;
        private bool _status = true;
        private JsonType _jsonType = JsonType.Filter;

        public string Title
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Title));
            }
        }

        public string SubTitle
        {
            get => _subTitle;
            set
            {
                _subTitle = value;
                OnPropertyChanged(nameof(SubTitle));
            }
        }

        public string Database
        {
            get => _database;
            set
            {
                _database = value;
                OnPropertyChanged(nameof(Database));
            }
        }

        public JsonType JsonType
        {
            get => _jsonType;
            set
            {
                _jsonType = value;
                OnPropertyChanged(nameof(JsonType));
            }
        }

        public string Json
        {
            get => _json;
            set
            {
                _json = value;
                OnPropertyChanged(nameof(Json));
            }
        }
        public string IcoPath
        {
            get => _icopath;
            set
            {
                _icopath = value;
                OnPropertyChanged(nameof(IcoPath));
            }
        }

        public bool Cachable
        {
            get => _cachable;
            set
            {
                _cachable = value;
                OnPropertyChanged(nameof(Cachable));
            }
        }

        public bool Enabled
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(nameof(Enabled));
            }
        }
    }

    public enum BrowserType
    {
        Chromium,
        Firefox,
    }
}
