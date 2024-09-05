using Flow.Launcher.Plugin.Notion.Views;
using System.Collections.Generic;
using System.Text.Json.Serialization;
namespace Flow.Launcher.Plugin.Notion.ViewModels
{
    public class CustomPayload : BaseModel
    {
        private string _name = string.Empty;
        private string _subTitle = string.Empty;
        private string _itemSubTitle = "relation";
        private int _timeout = 100;
        private List<string> _databases = new List<string>();
        private List<string> _propertiesNames = new List<string>();
        private string _json;
        private string _icopath = System.IO.Path.Combine(Main.ImagesPath, "app.png");
        private CacheTypes _cacheType = CacheTypes.Disabled;
        private bool _status = true;
        private bool _count = true;
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
        public int Timeout
        {
            get => _timeout;
            set
            {
                _timeout = value;
                OnPropertyChanged(nameof(Timeout));
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

        public string ItemSubTitle
        {
            get => _itemSubTitle;
            set
            {
                _itemSubTitle = value;
                OnPropertyChanged(nameof(ItemSubTitle));
            }
        }

        public List<string> Databases
        {
            get => _databases;
            set
            {
                _databases = value;
                OnPropertyChanged(nameof(Databases));
                OnPropertyChanged(nameof(DatabasesString));
            }
        }

        public List<string> PropertiesNames
        {
            get => _propertiesNames;
            set
            {
                _propertiesNames = value;
                OnPropertyChanged(nameof(PropertiesNames));
            }
        }

        [JsonIgnore]
        public string DatabasesString
        {
            get
            {
                return string.Join(", ", Databases);
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

        public CacheTypes CacheType
        {
            get => _cacheType;
            set
            {
                _cacheType = value;
                OnPropertyChanged(nameof(CacheType));
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

        public bool Count
        {
            get => _count;
            set
            {
                _count = value;
                OnPropertyChanged(nameof(Count));
            }
        }
    }

}
