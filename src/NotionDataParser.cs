using System.Text.RegularExpressions;
using System.Linq;
using System.Dynamic;
using System.Text.Json;
using Microsoft.Recognizers.Text.DateTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Specialized;
using Newtonsoft.Json;
using System.Reflection;
using Microsoft.Recognizers.Text.InternalCache;
using System.DirectoryServices.ActiveDirectory;
namespace Flow.Launcher.Plugin.Notion
{

    public class NotionDataParser

    {

        static Dictionary<string, string> projectIdForName { get; set; }
        static Dictionary<string, string> databaseIdForName
        {

            get
            {
                try
                {
                    return Main.databaseId.ToDictionary(
                                        kv => kv.Value.GetProperty("id").GetString(),
                                        kv => kv.Key
                                    );
                }
                catch
                {
                    return new Dictionary<string, string>();
                }
            }
        }
        private static string _defaultIconPath = "Images\\app.png";
        private static string _emojiIconPath = "Icons\\emojis";
        private string _iconPath;
        private PluginInitContext _context { get; set; }
        private Settings _settings { get; set; }

        internal NotionDataParser(PluginInitContext context, Settings settings)
        {
            this._context = context;
            this._settings = settings;
            UpdateProjectsMap();
            _iconPath = Path.Combine("Icons", "icons");
        }

        void UpdateProjectsMap()
        {
            try
            {
                projectIdForName = Main.LoadJsonData(Path.Combine(Main.cacheDirectory, _settings.RelationDatabasesIds[0] + ".json"))
                                                .ToDictionary(
                                                        kv => kv.Key,
                                                        kv => kv.Value[0].GetString()
                                                    );
            }
            catch
            {
                projectIdForName = new Dictionary<string, string>();
            }

        }


        public static string GetHumanDateFormat(DateTime date)
        {
            DateTime today = DateTime.Today;
            DateTime tomorrow = today.AddDays(1);
            DateTime yesterday = today.AddDays(-1);

            if (date.Date == today)
            {
                return "Today";
            }
            else if (date.Date == tomorrow)
            {
                return "Tomorrow";
            }
            else if (date.Date == yesterday)
            {
                return "Yesterday";
            }
            else
            {

                if (IsPast(date, today))
                {
                    DateTime lastMonday = GetLastWeekday(today, DayOfWeek.Monday);

                    if (date.Date < lastMonday && date.Date >= GetLastWeekday(today, DayOfWeek.Friday))
                    {
                        return "Last " + date.DayOfWeek.ToString();
                    }
                    else if (date.Date >= lastMonday)
                    {
                        return date.DayOfWeek.ToString();
                    }

                    return date.ToString("MMMM d, yyyy");
                }
                if (GetNextWeekday(today, DayOfWeek.Monday) <= date.Date && date.Date <= GetNextWeekday(today, DayOfWeek.Thursday))
                {
                    return (IsPast(date, today) ? "Last " : "Next ") + date.DayOfWeek.ToString();
                }
                else if (GetNextWeekday(today, DayOfWeek.Monday) >= date.Date)
                {
                    return date.DayOfWeek.ToString();
                }
                else
                {
                    return date.ToString("MMMM d, yyyy");
                }

            }
            static DateTime GetNextWeekday(DateTime start, DayOfWeek day)
            {
                int daysToAdd = ((int)day - (int)start.DayOfWeek + 7) % (start.DayOfWeek == day ? 14 : 7);
                return start.AddDays(daysToAdd);
            }
            static DateTime GetLastWeekday(DateTime start, DayOfWeek day)
            {
                int daysToAdd = ((int)day - (int)start.DayOfWeek + 6) % (start.AddDays(+1).DayOfWeek == day ? 14 : 7);
                return start.AddDays(-daysToAdd);
            }
            static bool IsPast(DateTime date, DateTime now)
            {
                if (date < now)
                {
                    return true;
                }
                else
                {
                    return false;
                }

            }
        }

        public string GetFullTitle(JToken titleList)
        {
            string extractedTitle = string.Empty;
            foreach (var titleType in titleList)
            {
                if ((string)titleType["type"] == "mention" && (string)titleType["mention"]["type"] == "date")
                {
                    DateTime dateFromString = DateTime.Parse((string)titleType["plain_text"]);
                    extractedTitle += GetHumanDateFormat(dateFromString);
                }
                else
                {
                    extractedTitle += titleType["plain_text"].ToString();
                }
            }

            return extractedTitle;
        }

        private HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.InernalInegrationToken.Trim());
            client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        
            return client;
        }

        internal async Task<JArray> CallApiForSearch(OrderedDictionary oldDatabaseId = null, string startCursor = null, string keyword = "", int numPage = 10, bool Force = false, string Value = "page")
        {
            UpdateProjectsMap();

            if (oldDatabaseId == null)
            {
                numPage = 100;
            }

            Dictionary<string, object> data = new Dictionary<string, object>
            {
                { "query", keyword },
                { "filter", new Dictionary<string, object>
                    {
                        { "value", Value},
                        { "property", "object" }
                    }
                },
                { "sort", new Dictionary<string, object>
                    {
                        { "direction", "ascending" },
                        { "timestamp", "last_edited_time" }
                    }
                },
                { "page_size", numPage}
            };

            if (!string.IsNullOrEmpty(startCursor))
            {
                data["start_cursor"] = startCursor;
            }


            // Send the POST request
            using (HttpClient client = CreateHttpClient())
            {
                var response = client.PostAsync("https://api.notion.com/v1/search", new StringContent(System.Text.Json.JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json")).Result;

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                    JArray allResults = new JArray();
                    allResults.Merge(jsonObject["results"]);

                    while ((bool)jsonObject["has_more"])
                    {
                        data["page_size"] = 100;
                        data["start_cursor"] = jsonObject["next_cursor"].ToString();
                        response = client.PostAsync("https://api.notion.com/v1/search", new StringContent(System.Text.Json.JsonSerializer.Serialize(data), System.Text.Encoding.UTF8, "application/json")).Result;
                        jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);

                        allResults.Merge(jsonObject["results"]);
                    }

                    if (Value.Contains("database")) return allResults;

                    if (allResults.Count != 1 || Force)
                    {
                        Dictionary<string, List<string>> resultDataDictionary = new Dictionary<string, List<string>>();
                        foreach (var result in allResults)
                        {
                            try
                            {
                                JObject properties = (JObject)result["properties"];
                                string title = null;
                                string icon = _context.CurrentPluginMetadata.IcoPath;

                                if (_settings.PagesIcons)
                                {
                                    icon = IconParse(result["icon"]);

                                }


                                foreach (var kvp in properties)
                                {
                                    var values = (JObject)kvp.Value;
                                    if (values["type"].ToString() == "title")
                                    {
                                        title = kvp.Key;
                                    }
                                }

                                // Extract the title from the response
                                string extractedTitle;
                                try
                                {
                                    extractedTitle = GetFullTitle(result["properties"][title]["title"]);
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    extractedTitle = string.Empty;
                                }
                                string DBName;
                                string Chain = string.Empty;

                                try
                                {
                                    DBName = databaseIdForName[result["parent"]["database_id"].ToString()];
                                }
                                catch
                                {
                                    DBName = string.Empty;
                                }

                                try
                                {
                                    if (result["parent"]["type"].ToString() == "block_id")
                                    {
                                        var page_id = GetPageIdByBlock(result["parent"]["block_id"].ToString());
                                        Chain = page_id;
                                    }
                                    else if (result["parent"]["type"].ToString() == "page_id")
                                        Chain = result["parent"]["page_id"].ToString();

                                }
                                catch (Exception ex)
                                {

                                    Chain = string.Empty;
                                    _context.API.LogException("BuildCache", "BuildChainError", ex, "GetPageByBlockId");
                                }


                                string idPage = result.Value<string>("id");

                                // Try to Extract the Relation Value from the response if it's exist
                                string relatedProject;
                                try
                                {
                                    var TargetDatabaseMap = Main.databaseId[DBName];
                                    // var projectRelation = result["properties"][TargetDatabaseMap.GetProperty("relation").EnumerateArray().First().GetString()]["relation"][0];
                                    var projectRelation = result["properties"][TargetDatabaseMap.GetProperty("relation").EnumerateObject().FirstOrDefault(x => x.Value.GetString() == _settings.RelationDatabasesIds[0]).Name]["relation"][0];
                                    if (projectRelation != null && projectRelation["id"] != null)
                                    {
                                        relatedProject = projectIdForName[projectRelation["id"].ToString()];
                                    }
                                    else
                                    {
                                        relatedProject = string.Empty;
                                    }
                                }
                                catch
                                {
                                    relatedProject = string.Empty;
                                }

                                List<string> resultDataList = new List<string>
                                {
                                    extractedTitle,
                                    relatedProject,
                                    DBName,
                                    icon,
                                    Chain
                                };

                                resultDataDictionary[idPage] = resultDataList;
                            }
                            catch (Exception ex)
                            {
                                _context.API.LogException(nameof(NotionDataParser), $"Error processing result: {result}", ex);
                            }
                        }

                        foreach (var key in resultDataDictionary.Keys)
                        {
                            if (oldDatabaseId == null)
                            {
                                oldDatabaseId = new OrderedDictionary();
                            }

                            if (!oldDatabaseId.Contains(key)) oldDatabaseId[key] = resultDataDictionary[key];
                            else
                            {
                                // Dictionary<string, List<string>> newDatabaseId = oldDatabaseId
                                // .Where(kv => kv.Key != key)
                                // .ToDictionary(kv => kv.Key, kv => kv.Value);
                                // newDatabaseId[key] = resultDataDictionary[key];
                                // oldDatabaseId = newDatabaseId;
                                oldDatabaseId.Remove(key);
                                oldDatabaseId.Add(key, resultDataDictionary[key]);
                            }

                        }


                        string jsonString = System.Text.Json.JsonSerializer.Serialize(oldDatabaseId, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(_settings.FullCachePath, jsonString);

                    }
                }
                else
                {
                    _context.API.LogWarn(nameof(NotionDataParser), response.ReasonPhrase + "\n"  + response.Content.ReadAsStringAsync().Result + " \n (" + _settings.InernalInegrationToken + ")", MethodBase.GetCurrentMethod().Name);
                    // Try To recache the whole shared pages in case of page deleted on notion by Notion UI
                    if (!string.IsNullOrEmpty(startCursor))
                        await CallApiForSearch(oldDatabaseId: null, startCursor: null, numPage: 100);
                }
                return new JArray();
            }
        }

        public JObject RetrievePageProperitesById(string pageId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                string url = "https://api.notion.com/v1/pages/";
                var response = client.GetAsync(url + pageId).Result;
                JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                return jsonObject;
            }
        }

        string GetPageIdByBlock(string BlockId)
        {
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                string url = "https://api.notion.com/v1/blocks/";
                var response = client.GetAsync(url + BlockId).Result;

                JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                while (jsonObject["parent"]["type"].ToString() != "page_id")
                {
                    response = client.GetAsync(url + jsonObject["parent"][jsonObject["parent"]["type"].ToString()].ToString()).Result;
                    jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                }

                return jsonObject["parent"]["page_id"].ToString();

            }
        }

        public async Task<Dictionary<string, JsonElement>> DatabaseCache()
        {
            JArray allDatabases = await CallApiForSearch(Value: "database");

            Dictionary<string, JsonElement> Databases = new Dictionary<string, JsonElement>();
            foreach (var DB in allDatabases)
            {
                try
                {
                    JObject properties = (JObject)DB["properties"];
                    string title = null;
                    Dictionary<string, string> Relation = new Dictionary<string, string>();
                    Dictionary<string, List<string>> MultiSelect = new Dictionary<string, List<string>>();
                    Dictionary<string, List<string>> SingleSelect = new Dictionary<string, List<string>>();
                    Dictionary<string, List<string>> Status = new Dictionary<string, List<string>>();
                    List<string> Date = new List<string>();
                    List<string> CheckBox = new List<string>();
                    List<string> urlMap = new List<string>();

                    string Icon = _context.CurrentPluginMetadata.IcoPath;

                    if (_settings.DatabaseIcons)
                    {
                        Icon = IconParse(DB["icon"]);
                    }


                    foreach (var kvp in properties)
                    {
                        try
                        {
                            var values = (JObject)kvp.Value;
                            if (values["type"].ToString() == "date")
                            {
                                Date.Add(kvp.Key);
                                continue;
                            }
                            if (values["type"].ToString() == "title")
                            {
                                title = kvp.Key;
                                continue;
                            }
                            if (values["type"].ToString() == "url")
                            {
                                urlMap.Add(kvp.Key);
                                continue;
                            }
                            if (values["type"].ToString() == "checkbox")
                            {
                                CheckBox.Add(kvp.Key);
                                continue;
                            }
                            else if (values["type"].ToString().Contains("relation") &&
                                // values["relation"]["database_id"].ToString() == _settings.RelationDatabaseId)
                                _settings.RelationDatabasesIds.Contains(values["relation"]["database_id"].ToString()))
                            {
                                Relation[kvp.Key.ToString()] = values["relation"]["database_id"].ToString();
                                continue;
                            }
                            else if (values["type"].ToString().Contains("multi_select"))
                            {
                                MultiSelect[kvp.Key.ToString()] = new List<string>();
                                if (properties[kvp.Key.ToString()]["multi_select"]["options"] is JArray optionsArray && optionsArray.Count > 0)
                                {
                                    foreach (var option in optionsArray)
                                    {
                                        string optionName = option["name"].ToString();
                                        MultiSelect[kvp.Key.ToString()].Add(optionName);
                                    }
                                }
                                continue;
                            }
                            else if (values["type"].ToString().Contains("select"))
                            {
                                SingleSelect[kvp.Key.ToString()] = new List<string>();
                                if (properties[kvp.Key.ToString()]["select"]["options"] is JArray optionsArray && optionsArray.Count > 0)
                                {
                                    foreach (var option in optionsArray)
                                    {
                                        string optionName = option["name"].ToString();
                                        SingleSelect[kvp.Key.ToString()].Add(optionName);
                                    }
                                }
                                continue;
                            }
                            else if (values["type"].ToString().Contains("status"))
                            {
                                Status[kvp.Key.ToString()] = new List<string>();
                                if (properties[kvp.Key.ToString()]["status"]["options"] is JArray optionsArray && optionsArray.Count > 0)
                                {
                                    foreach (var option in optionsArray)
                                    {
                                        string optionName = option["name"].ToString();
                                        Status[kvp.Key.ToString()].Add(optionName);
                                    }
                                }
                                continue;
                            }
                        }
                        catch
                        {
                            continue;
                        }
                    }

                    var jsonElement = System.Text.Json.JsonSerializer.Deserialize<JsonElement>(System.Text.Json.JsonSerializer.Serialize(new
                    {
                        title,
                        id = DB["id"].ToString(),
                        icon = Icon,
                        date = Date,
                        multi_select = MultiSelect,
                        select = SingleSelect,
                        relation = Relation,
                        urlMap = urlMap,
                        status = Status,
                        check_box = CheckBox,
                        url = DB["url"].ToString().Replace("https://", "notion://")
                    }));

                    Databases[GetFullTitle(DB["title"])] = jsonElement;
                    string jsonString = System.Text.Json.JsonSerializer.Serialize(Databases, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_settings.DatabaseCachePath, jsonString);
                }
                catch (Exception ex)
                {
                    _context.API.LogException(className: nameof(NotionDataParser), "An error occurred during Database Cache", ex);
                }
            }

            return Databases;
        }


        public string FileIconParse(dynamic dataIcon)
        {
            string iconUrl = dataIcon.file.url;
            string filename = System.IO.Path.GetFileName(iconUrl.Split('?')[0]);
            string filePath = System.IO.Path.Combine(_iconPath, filename);
            string fullFilePath = System.IO.Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, filePath);
            if (!File.Exists(fullFilePath))
            {
                FileSave(iconUrl, fullFilePath);
            }
            return filePath;
        }

        public string IconParse(dynamic dataIcon)
        {
            string icon = _defaultIconPath;
            if (dataIcon != null)
            {
                string dataIconType = dataIcon["type"];
                Console.WriteLine(dataIconType);
                if (dataIconType == "emoji")
                {
                    icon = EmojiIconParse(dataIcon);
                }
                else if (dataIconType == "external")
                {
                    icon = ExternalIconParse(dataIcon);
                }
                else if (dataIconType == "file")
                {
                    icon = FileIconParse(dataIcon);
                }
            }
            return icon;
        }

        public void FileSave(string url, string filePath)
        {
            if (!File.Exists(filePath))
            {
                Task.Run(async () =>
                {
                    await _context.API.HttpDownloadAsync(url, filePath);
                });
            }
        }

        static string EmojiIconParse(dynamic dataIcon)
        {
            string[] hexList = DecodeEmoji(dataIcon["emoji"]);
            string emojiCodePoints = string.Empty;
            int count = 0;
            foreach (var x in hexList)
            {
                count++;
                if (count > 1)
                {
                    emojiCodePoints += "_";
                }
                emojiCodePoints += x;
            }
            string icon = System.IO.Path.Combine(_emojiIconPath, $"{emojiCodePoints}.png");
            return icon;
        }

        public string ExternalIconParse(dynamic dataIcon)
        {
            string iconUrl = dataIcon.external.url;
            iconUrl = iconUrl.StartsWith("/images")
            ? $"https://www.notion.so{iconUrl}"
            : iconUrl;
            string filename = System.IO.Path.GetFileName(iconUrl);
            string filePath = System.IO.Path.Combine(_iconPath, filename);
            string fullFilePath = System.IO.Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, filePath);

            if (!File.Exists(fullFilePath))
            {
                FileSave(iconUrl, fullFilePath);
            }
            return filePath;
        }

        static string[] DecodeEmoji(dynamic emoji)
        {
            if (!string.IsNullOrEmpty(Convert.ToString(emoji)))
            {
                byte[] b = Encoding.UTF32.GetBytes(Convert.ToString(emoji));
                int count = b.Length / 4;

                if (count > 10)
                {
                    return null;
                }

                int[] cp = new int[count];
                Buffer.BlockCopy(b, 0, cp, 0, b.Length);
                string[] hexList = cp.Select(x => x.ToString("X")).ToArray();
                return hexList;
            }
            return null;
        }
        public async Task GetStartCursour(int delay = 0, string manuanl_cursour = null)
        {
            string JsonFilePath = _settings.FullCachePath;
            try
            {
                File.SetLastWriteTime(JsonFilePath, DateTime.Now);
            }
            catch
            {

            }
            if (delay != 0)
            {
                await Task.Delay(delay);
            }
            string jsonContent = File.ReadAllText(JsonFilePath);
            string lastCursorKey = null;
            OrderedDictionary oldDatabaseId = null;
            try
            {
                oldDatabaseId = System.Text.Json.JsonSerializer.Deserialize<OrderedDictionary>(jsonContent);
                List<string> lastCursorKeys = new List<string>(oldDatabaseId.Keys.Cast<string>());
                // lastCursorKey = lastCursorKeys[manuanl_cursour == null ? lastCursorKeys.Count - 1 : lastCursorKeys.IndexOf(manuanl_cursour) - 1]  ;
                lastCursorKey = lastCursorKeys[lastCursorKeys.Count - 1];
            }
            catch
            {
                lastCursorKey = null;
            }

            _ = CallApiForSearch(startCursor: manuanl_cursour == null ? lastCursorKey : manuanl_cursour, oldDatabaseId: oldDatabaseId, Force: true);
        }

        public async Task<Dictionary<string, JsonElement>> QueryDB(string DB, string filterPayload, string filePath = null, string itemSubtitle = "relation", List<string> propNames = null)
        {
            UpdateProjectsMap();
            string url = $"https://api.notion.com/v1/databases/{DB}/query?";
            filterPayload = !string.IsNullOrEmpty(filterPayload) ? ConvertVariables(filterPayload) : filterPayload;
            Dictionary<string, object> _payload = new Dictionary<string, object>
            {
                {"page_size" ,100}
            };
            if (!string.IsNullOrEmpty(filterPayload))
            {
                _payload.Add("filter", JsonConvert.DeserializeObject<dynamic>(filterPayload));
            }



            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                var jsonPayload = JsonConvert.SerializeObject(_payload);
                var response = await client.PostAsync(url, new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json"));
                Dictionary<string, JsonElement> FilterResults = new Dictionary<string, JsonElement>();
                string jsonString = string.Empty;
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                    JArray allResults = new JArray();
                    allResults.Merge(jsonObject["results"]);

                    while ((bool)jsonObject["has_more"])
                    {
                        _payload["start_cursor"] = jsonObject["next_cursor"].ToString();
                        response = client.PostAsync(url, new StringContent(JsonConvert.SerializeObject(_payload), System.Text.Encoding.UTF8, "application/json")).Result;
                        jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                        allResults.Merge(jsonObject["results"]);
                    }

                    JObject pages = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var TargetDatabase = Main.databaseId.FirstOrDefault(x => x.Value.GetProperty("id").GetString() == DB);
                    var TargetDatabaseMap = TargetDatabase.Value;
                    foreach (var page in allResults)
                    {
                        string Tags = string.Empty;
                        string project_name;
                        string title = GetFullTitle(page["properties"][TargetDatabaseMap.GetProperty("title").GetString()]["title"]);

                        // Trim In case of Relation DB query to prevent any errors while parsing using GetData method
                        if (string.IsNullOrEmpty(filterPayload) && DB == _settings.RelationDatabaseId)
                        {
                            title = title.Trim();
                        }

                        try
                        {
                            // Tags = page["properties"][TargetDatabaseMap.GetProperty("multi_select").EnumerateObject().First().Name.ToString()]["multi_select"][0]["name"].ToString();
                            if (propNames != null) {
                            foreach (var selectProp in TargetDatabaseMap.GetProperty("select").EnumerateObject())
                            {
                                if (!propNames.Contains(selectProp.Name.ToString())) {
                                    continue;
                                }
                                try {
                                    string selectPropValue = page["properties"][selectProp.Name.ToString()]["select"]["name"].ToString();

                                    if (!String.IsNullOrEmpty(selectPropValue))
                                    {
                                        Tags = String.IsNullOrEmpty(Tags) ? selectPropValue : Tags + ", " + selectPropValue;
                                    }
                                    
                                } 
                                catch {
                                    continue;
                                }
                            }

                            foreach (var multi_selectProp in TargetDatabaseMap.GetProperty("multi_select").EnumerateObject())
                            {
                                if (!propNames.Contains(multi_selectProp.Name.ToString())) {
                                    continue;
                                }
                                try {
                                    string multi_selectPropValue = page["properties"][multi_selectProp.Name.ToString()]["multi_select"][0]["name"].ToString();

                                    if (!String.IsNullOrEmpty(multi_selectPropValue))
                                    {
                                        Tags = String.IsNullOrEmpty(Tags) ? multi_selectPropValue : Tags + ", " + multi_selectPropValue;
                                    }
                                    
                                } 
                                catch {
                                    continue;
                                }
                            }
                            }
                            //Tags = page["properties"][TargetDatabaseMap.GetProperty("select").EnumerateObject().First().Name.ToString()]["select"]["name"].ToString();
                        }
                        catch
                        {
                            Tags = "";
                        }

                        try
                        {
                            project_name = projectIdForName[page["properties"][TargetDatabaseMap.GetProperty("relation").EnumerateObject().FirstOrDefault(x => x.Value.GetString() == _settings.RelationDatabasesIds[0]).Name]["relation"][0]["id"].ToString()];
                        }
                        catch
                        {
                            project_name = "";
                        }

                        string subtitle = string.Empty;

                        if (!string.IsNullOrEmpty(Tags))
                        {
                            if (itemSubtitle.Contains("| tag"))
                                subtitle = " | " + Tags;
                            else if (itemSubtitle.Contains("tag |"))
                                subtitle = Tags + " | ";
                            else if (itemSubtitle.Contains("tag"))
                                subtitle = Tags;

                        }


                        if (!string.IsNullOrEmpty(project_name))
                        {
                            if (itemSubtitle.Contains("| relation"))
                                subtitle = subtitle + project_name;
                            else if (itemSubtitle.Contains("relation |"))
                                subtitle = project_name + subtitle;
                            else if (itemSubtitle.Contains("relation"))
                                subtitle = project_name;
                        }


                        string pageUrl = page["url"].ToString().Replace("https", "notion");
                        string id_page = page["id"].ToString();
                        string icon = IconParse(page["icon"]);
                        var data = new List<string> { title, subtitle, pageUrl, icon, project_name, TargetDatabase.Key };
                        JsonDocument document = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data));
                        JsonElement element = document.RootElement;
                        FilterResults[id_page] = element;
                    }

                    if (!string.IsNullOrEmpty(filePath))
                    {
                        jsonString = System.Text.Json.JsonSerializer.Serialize(FilterResults, new JsonSerializerOptions { WriteIndented = true });
                        File.WriteAllText(filePath, jsonString);
                    }

                    return FilterResults;
                }
                else if (response.ReasonPhrase == "Bad Request")
                {
                    if (!string.IsNullOrEmpty(filterPayload) && !(DB == _settings.RelationDatabaseId))
                    {
                        _context.API.ShowMsgError("Custom Filter Payload Error", "Please check out custom payload from settings panel");
                        _context.API.OpenSettingDialog();
                    }
                    return FilterResults;
                }
                else
                {
                    return FilterResults;
                }
            }
        }


        public string ConvertVariables(string input)
        {
            Regex curlyBracesRegex = new Regex(@"\{\{([^}]+)\}\}");
            MatchCollection bracketMatches = curlyBracesRegex.Matches(input);
            foreach (Match bracketMatch in bracketMatches)
            {
                string textInsideBrackets = bracketMatch.Groups[1].Value;
                string IsContainDate = DayToDate(textInsideBrackets);
                if (!string.IsNullOrEmpty(IsContainDate))
                {
                    var replacementText = Convert.ToDateTime(DayToDate(textInsideBrackets)).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
                    string newText = curlyBracesRegex.Replace(input, replacementText, 1);
                    input = newText;
                }
            }
            return input;
        }
        static string DayToDate(string input = "")
        {
            string parsedDate = string.Empty;
            var results = DateTimeRecognizer.RecognizeDateTime(input, "en-us");
            if (results.Any())
            {
                var jsonResults = System.Text.Json.JsonSerializer.SerializeToNode(results.First(), new JsonSerializerOptions { WriteIndented = true });
                parsedDate = Convert.ToString(jsonResults["Resolution"]["values"].AsArray().Last()["value"]);
                if (string.IsNullOrEmpty(parsedDate))
                {
                    parsedDate = Convert.ToString(jsonResults["Resolution"]["values"].AsArray().Last()["start"]);
                }

                return parsedDate;
            }
            else
            {
                return string.Empty;
            }

        }

        public JObject RetrievePageJsonObjectById(string id)
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                    string url = "https://api.notion.com/v1/pages/";
                    var response = client.GetAsync(url + id).Result;
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        return JObject.Parse(response.Content.ReadAsStringAsync().Result);
                    }

                    return null;
                }
            }
            catch
            {
                return null;
            }

        }

        public List<Result> PropertiesIntoResults(string query, JObject properties)
        {
            var propertiesResults = new List<Result>();
            foreach (var prop in properties)
            {
                var propType = prop.Value["type"].ToString();
                if (propType == "url")
                {
                    var _result = UrlResult(query, prop.Key, prop.Value["url"].ToString());
                    if (_result is not null)
                        propertiesResults.Add(_result);
                }
                else if (propType == "relation")
                {
                    var relations = prop.Value["relation"] as JArray;
                    if (relations is not null && relations.Count() > 0)
                    {

                        var _result = RelationResult(query, prop.Key, relations);
                        if (_result is not null)
                            propertiesResults.AddRange(_result);
                    }

                }
            }
            return propertiesResults;
        }
        public Result UrlResult(string query, string title, string link)
        {
            if (!String.IsNullOrEmpty(link.ToString()))
            {
                if (IsQueryMatchProp(query, title, link))
                {
                    return new Result
                    {
                        Title = title,
                        SubTitle = $"Click to open {link} in your browser",
                        IcoPath = "Images//embed.png",
                        Action = c =>
                        {
                            _context.API.OpenUrl(link);
                            return true;
                        }
                    };
                }

            }
            return null;
        }

        public List<Result> RelationResult(string query, string propTitle, JArray relations)
        {
            List<Result> relationResults = new List<Result>();
            foreach (var id in relations)
            {
                var IdString = id["id"].ToString();
                if (IsQueryMatchProp(query, propTitle,Main.searchResults[IdString][0].GetString()))
                // if (_context.API.FuzzySearch(query, Main.searchResults[IdString][0].GetString()).Score > 1 ||
                //     _context.API.FuzzySearch(query, propTitle).Score > 1 ||
                //     String.IsNullOrEmpty(query))
                {
                    relationResults.Add(new Result
                    {
                        Title = propTitle,
                        SubTitle = "Show properties of " + Main.searchResults[IdString][0].GetString(),
                        IcoPath = Main.searchResults[IdString][3].GetString(),
                        Action = c =>
                        {
                            Main.currPageProperties = null;
                            _context.API.ChangeQuery(_context.CurrentPluginMetadata.ActionKeyword + " $" + IdString + "$ ", true);
                            return false;
                        }
                    });
                }
            }


            return relationResults;
        }

        bool IsQueryMatchProp(string query, string propTitle, string propContent)
        {
            if (_context.API.FuzzySearch(query, propContent).Score > 1 ||
                    _context.API.FuzzySearch(query, propTitle).Score > 1 ||
                    String.IsNullOrEmpty(query))
            {
                return true;
            }
            return false;
        }

    }
}
