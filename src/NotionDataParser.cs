using System.Text.RegularExpressions;
using System.Linq;
using System.Dynamic;
using System.Text.Json;
using Microsoft.Recognizers.Text.DateTime;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using System.Text;
using System.Collections.Specialized;
using Newtonsoft.Json;
using System.Reflection;

namespace Flow.Launcher.Plugin.Notion
{

    internal class NotionDataParser

    {

        static Dictionary<string, string> projectIdForName
        {

            get
            {
                try
                {
                    return Main.ProjectsId.ToDictionary(
                                        kv => kv.Value[3].ToString(),
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
        private static string _emojiIconPath = "icons\\emojis";
        private string _iconPath;
        private PluginInitContext _context { get; set; }
        private Settings _settings { get; set; }

        internal NotionDataParser(PluginInitContext context, Settings settings)
        {
            this._context = context;
            this._settings = settings;
            _iconPath = Path.Combine("icons", "icons");
        }

        internal async Task<JArray> CallApiForSearch(OrderedDictionary oldDatabaseId = null, string startCursor = null, string keyword = "", int numPage = 100, bool Force = false, string Value = "page")
        {

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
            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
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
                                    extractedTitle = result["properties"][title]["title"][0]["text"]["content"].ToString();
                                }
                                catch (ArgumentOutOfRangeException)
                                {
                                    extractedTitle = string.Empty;
                                }
                                string idDatabase;

                                // Try to Extract the Database Id from the response if it's exist
                                try
                                {
                                    idDatabase = result["parent"]["database_id"].ToString();
                                }
                                catch
                                {
                                    idDatabase = string.Empty;
                                }
                                string idPage = result.Value<string>("id");

                                // Try to Extract the Relation Value from the response if it's exist
                                string relatedProject;
                                try
                                {
                                    var TargetDatabaseMap = Main.databaseId.FirstOrDefault(x => x.Value.GetProperty("id").GetString() == idDatabase).Value;
                                    var projectRelation = result["properties"][TargetDatabaseMap.GetProperty("relation").EnumerateArray().First().GetString()]["relation"][0];
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
                                    idDatabase,
                                    icon
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
                    _context.API.LogWarn(nameof(NotionDataParser), response.ReasonPhrase, MethodBase.GetCurrentMethod().Name);
                }
                return new JArray();
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
                    List<string> Relation = new List<string>();
                    Dictionary<string, List<string>> MultiSelect = new Dictionary<string, List<string>>();
                    List<string> Date = new List<string>();
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
                            else if (values["type"].ToString().Contains("relation") && values["relation"]["database_id"].ToString() == _settings.RelationDatabaseId)
                            {
                                Relation.Add(kvp.Key.ToString());
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
                        relation = Relation,
                        urlMap = urlMap,
                        url = DB["url"].ToString().Replace("https://", "notion://")
                    }));

                    Databases[DB["title"][0]["text"]["content"].ToString()] = jsonElement;
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

            _ = CallApiForSearch(startCursor: manuanl_cursour == null ? lastCursorKey : manuanl_cursour, oldDatabaseId: oldDatabaseId, Force: manuanl_cursour != null ? true : false);
        }

        public async Task<Dictionary<string, JsonElement>> QueryDB(string DB, string filterPayload, string filePath = null)
        {
            string url = $"https://api.notion.com/v1/databases/{DB}/query?";
            filterPayload = !string.IsNullOrEmpty(filterPayload) ? ConvertVariables(filterPayload) : filterPayload;
            object payload = new ExpandoObject();
            if (!string.IsNullOrEmpty(filterPayload))
            {
                payload = new
                {
                    filter = JsonConvert.DeserializeObject<dynamic>(filterPayload),
                    page_size = 100
                };
            }
            else
            {

                payload = new
                {
                    page_size = 100
                };

            }

            using (HttpClient client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                var jsonPayload = JsonConvert.SerializeObject(payload);
                var response = await client.PostAsync(url, new StringContent(jsonPayload, System.Text.Encoding.UTF8, "application/json"));
                Dictionary<string, JsonElement> FilterResults = new Dictionary<string, JsonElement>();
                string jsonString = string.Empty;
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    JObject pages = JObject.Parse(await response.Content.ReadAsStringAsync().ConfigureAwait(false));
                    var TargetDatabaseMap = Main.databaseId.FirstOrDefault(x => x.Value.GetProperty("id").GetString() == DB).Value;
                    foreach (var page in pages["results"])
                    {
                        string Tags = null;
                        string project_name;

                        string Name = page["properties"][TargetDatabaseMap.GetProperty("title").GetString()]["title"][0]["text"]["content"].ToString();
                        
                        // Trim In case of Relation DB query to prevent any errors while parsing using GetData method
                        if (string.IsNullOrEmpty(filterPayload) && DB == _settings.RelationDatabaseId)
                        {
                            Name = Name.Trim();
                        }

                        try
                        {
                            Tags = page["properties"][TargetDatabaseMap.GetProperty("multi_select").EnumerateObject().First().Name.ToString()]["multi_select"][0]["name"].ToString();
                        }
                        catch
                        {
                            Tags = "";
                        }

                        try
                        {
                            project_name = projectIdForName[page["properties"][TargetDatabaseMap.GetProperty("relation").EnumerateArray().First().GetString()]["relation"][0]["id"].ToString()];

                        }
                        catch
                        {
                            project_name = "";
                        }

                        string pageUrl = page["url"].ToString().Replace("https", "notion");
                        string id_page = page["id"].ToString();
                        string icon = IconParse(page["icon"]);
                        var data = new List<string> { Tags, pageUrl, project_name, id_page, icon };
                        JsonDocument document = JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(data));
                        JsonElement element = document.RootElement;
                        FilterResults[Name] = element;
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
                    var replacementText = Convert.ToDateTime(DayToDate(textInsideBrackets)).ToString("yyyy-MM-ddTHH:mm:ssZ");
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
    }
}







