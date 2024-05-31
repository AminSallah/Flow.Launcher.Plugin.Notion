using System.Collections.Generic;
using System;
using System.Text.Json;
using Flow.Launcher.Plugin.Notion.ViewModels;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Linq;
using Flow.Launcher.Plugin.Notion.Views;
using System.Collections.Immutable;

namespace Flow.Launcher.Plugin.Notion
{
	public class Settings: BaseModel
	{
		public string InernalInegrationToken { get; set; } = string.Empty;
		public string DatabaseCachePath { get; set; } = string.Empty;
		public string FullCachePath { get; set; } = string.Empty;
		public string SearchResultSubtitle { get; set; } = "DB / Relation / Chain";
        public bool Cachable { get; set; } = false;
		public bool RelationsIcons { get; set; } = true;
		public bool DatabaseIcons { get; set; } = true;
		public bool PagesIcons { get; set; } = true;
		public bool UseBrowser { get; set; } = false;
		public bool FailedRequests { get; set; } = false;
		public bool Hide { get; set; } = false;
		public bool RelationSubtitle { get; set; } = true;
		public bool PopUpPageAfterCreate { get; set; } = false;

		[JsonIgnore]
		public IEnumerable<string> DatabaseSelectionOptions => Main.databaseId.Keys.ToList();

		
		

		public string _searchBase = "All pages";
		public string SearchBase 
		{
			get => _searchBase;
			set
			{
				_searchBase = value;
				OnPropertyChanged(nameof(SearchBase));
			}
		}
		private string _defaultDatabase = string.Empty;
		public string DefaultDatabase
		{
			get
			{
				if (string.IsNullOrEmpty(_defaultDatabase) && DatabaseSelectionOptions.Any())
				{
					_defaultDatabase = DatabaseSelectionOptions.FirstOrDefault();
				}

				return _defaultDatabase;
			}
			set
			{
				_defaultDatabase = value;
			}
		}
		
		public string RelationDatabase { get; set; } = string.Empty;
		public List<string> RelationDatabases { get; set; } = new List<string>();
		public List<string> RelationDatabasesIds { get; set; } = new List<string>();
		public string RelationDatabaseId { get; set; } = string.Empty;

		public ObservableCollection<CustomPayload> Filters { get; set; } = new ObservableCollection<CustomPayload>
		{
			new CustomPayload
			{
				Title = "Complete",
				JsonType = JsonType.Property,
				Json = """{"properties":{"Status":{"status":{"name":"✅"}}}}""",
				IcoPath = System.IO.Path.Combine(Main.ImagesPath,"item_complete.png"),
			},
			new CustomPayload
			{
				Title = "Delete",
				JsonType = JsonType.Property,
				Json = """{"archived" : true}""",
				IcoPath = System.IO.Path.Combine(Main.ImagesPath,"item_delete.png"),
			}
		};

		[JsonIgnore]
		public CustomPayload SelectedPayload { get; set; }

		public Settings()
		{
			this.Filters = Filters;
			UpdateSearchFiltersOptions();
		}

		private ObservableCollection<string> _searchFiltersOptions;
        public ObservableCollection<string> SearchFiltersOptions
        {
            get { return _searchFiltersOptions; }
            set
            {
                _searchFiltersOptions = value;
				if (!_searchFiltersOptions.Contains(SearchBase))
					SearchBase = _searchFiltersOptions[0];
                OnPropertyChanged(nameof(SearchFiltersOptions));
            }
        }

        public void UpdateSearchFiltersOptions()
        {
            var titles = Filters.Where(x =>x.JsonType == JsonType.Filter && x.Enabled && x.CacheType != CacheTypes.Disabled)
								.Select(x => x.Title).ToList();
            titles.Insert(0, "Disabled");
            titles.Insert(1, "All pages");

            SearchFiltersOptions = new ObservableCollection<string>(titles);
        }
	}
}