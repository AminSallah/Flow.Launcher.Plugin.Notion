using System.Collections.Generic;
using System.Text.Json;
using Flow.Launcher.Plugin.Notion.ViewModels;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using System.Linq;
using Flow.Launcher.Plugin.Notion.Views;

namespace Flow.Launcher.Plugin.Notion
{
    /// <Summary>
    /// Flow Launcher Toggl Track plugin settings.
    /// </Summary>
    public class Settings
	{

		public string InernalInegrationToken { get; set; } = string.Empty;
		public string DatabaseCachePath { get; set; } = string.Empty;
		public string RelationCachePath { get; set; } = string.Empty;
		public string FullCachePath { get; set; } = string.Empty;
		public bool Cachable { get; set; } = false;
		public bool UseBrowser { get; set; } = false;


		public Dictionary<string, JsonElement> SharedDB
		{
			get
			{
				return Main.databaseId;
			}
		}
		private string _defaultDatabase = string.Empty;

		public string DefaultDatabase
		{
			get
			{
				// If the default database is empty, set it to the first key in SharedDB
				if (string.IsNullOrEmpty(_defaultDatabase) && SharedDB.Any())
				{
					_defaultDatabase = SharedDB.Keys.First();
				}

				return _defaultDatabase;
			}
			set
			{
				_defaultDatabase = value;
			}
		}

		public IEnumerable<string> DefaultRecentCountOptions => SharedDB.Keys.ToList();



		public string RelationDatabase { get; set; } = string.Empty;
		
		public string RelationDatabaseId { get; set; } = string.Empty;





        public ObservableCollection<CustomPayload> Filters { get; set; } = new ObservableCollection<CustomPayload>
		{
			new CustomPayload
			{
				Title = "Complete",
				JsonType = JsonType.Property,
				Json = """{"properties":{"Status":{"status":{"name":"✅"}}}}""",
				IcoPath = "Images/item_complete_3d.png",

			},
			new CustomPayload
			{
				Title = "Delete",
				JsonType = JsonType.Property,
				Json = """{"archived" : true}""",
				IcoPath = "Images/item_delete_3d.png",

			}
		};


		[JsonIgnore]
		public CustomPayload SelectedSearchSource { get; set; }


		public Settings()
		{
			this.Filters = Filters;
		}





		public bool Hide { get; set; } = false;
		public bool ShowUsageExamples { get; set; } = true;
		public bool ShowUsageWarnings { get; set; } = true;

		public bool AllowSuccessNotifications { get; set; } = true;
		public bool AllowErrorNotifications { get; set; } = true;


		public static Dictionary<string, JsonElement> LoadJsonData(string filePath = "C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\cache\\cache_search.json")
		{
			string json_data = System.IO.File.ReadAllText(filePath);
			return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json_data);
		}



	}






}