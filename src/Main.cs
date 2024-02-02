using System.Windows.Controls;

using System.Net.Http;
using System.IO;
using System;
using System.Text;
using System.Linq;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Flow.Launcher.Plugin.Notion.ViewModels;
using Flow.Launcher.Plugin.Notion.Views;
using Microsoft.Recognizers.Text.DateTime;
using System.Globalization;
using System.Threading.Tasks;
using System.Threading;
using Newtonsoft.Json.Linq;
using System.Net.NetworkInformation;
using File = System.IO.File;
using Newtonsoft.Json;
using System.Reflection;



namespace Flow.Launcher.Plugin.Notion
{
    public class Main : IAsyncPlugin, IContextMenu, ISettingProvider
    {
        DateTime refresh_search = DateTime.Now;
        public static int secondsThreshold = 30;

        private static string DatabaseCachePath;
        private static string RelationCachePath;
        private static string FullCachePath;

        public static string HiddenItemsPath;




        public static string today_tasks_cache_path = "C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\cache\\today_cache_results.json";
        static Dictionary<string, object> dataDict = new Dictionary<string, object>();

        private PluginInitContext Context;
        internal NotionBlockTypes? _notionBlockTypes;
        internal Toggl? _toggl;
        internal NotionDataParser? _NotionDataParser;

        private static SettingsViewModel? _viewModel;
        private Settings? _settings;

        internal static string CustomImagesDirectory;

        private bool RequestNewCache = false;

        public static List<string> HiddenItems = new List<string>();


        public static Dictionary<string, JsonElement> databaseId = LoadJsonData(RelationCachePath);
        public static Dictionary<string, JsonElement> ProjectsId = LoadJsonData(RelationCachePath);
        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            this._settings = context.API.LoadSettingJsonStorage<Settings>();

            DatabaseCachePath = System.IO.Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache", "database.json");
            _settings.DatabaseCachePath = DatabaseCachePath;
            RelationCachePath = System.IO.Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache", "relation.json");
            _settings.RelationCachePath = RelationCachePath;
            HiddenItemsPath = System.IO.Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache", "HiddenItems.json");
            HiddenItems = File.ReadAllLines(HiddenItemsPath).ToList<string>();

            FullCachePath = System.IO.Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache", "cache_search.json");
            _settings.FullCachePath = FullCachePath;

            try
            {
                this._notionBlockTypes = new NotionBlockTypes(this.Context);
                this._toggl = new Toggl(this.Context);
                this._NotionDataParser = new NotionDataParser(this.Context, _settings);
            }
            catch { }

            if (IsInternetConnected())
            {
                try
                {
                    if (!string.IsNullOrEmpty(_settings.InernalInegrationToken))
                    {
                        await this._NotionDataParser.DatabaseCache();
                        databaseId = LoadJsonData(DatabaseCachePath);
                        _settings.RelationDatabaseId = databaseId[_settings.RelationDatabase].GetProperty("id").ToString();

                        if (!string.IsNullOrEmpty(_settings.RelationDatabaseId))
                        {
                            await this._NotionDataParser.QueryDB(_settings.RelationDatabaseId, null, RelationCachePath);
                            ProjectsId = LoadJsonData(RelationCachePath);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Context.API.LogException(nameof(Main), "An error occurred while build database and relaiton cache", ex);
                }
            }
            else
            {
                databaseId = LoadJsonData(DatabaseCachePath);
                ProjectsId = LoadJsonData(RelationCachePath);
                Context.API.LogWarn(nameof(Main), "No internet Connection for Init cache using last cached data", MethodBase.GetCurrentMethod().Name);
            }



            Main._viewModel = new SettingsViewModel(this._settings);



            CustomImagesDirectory = System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Icons", "CustomIcons");
            if (!Directory.Exists(Main.CustomImagesDirectory))
                Directory.CreateDirectory(Main.CustomImagesDirectory);
            Context.API.VisibilityChanged += OnVisibilityChanged;


            try
            {
                // We should await for database to finish cache before Init FLow Launcher
                _ = Task.Run(async () =>
                {
                    await this._NotionDataParser.CallApiForSearch();
                    // await this._NotionDataParser.BuildCacheUsingHttp();
                    // Add other background tasks if needed
                });
            }
            catch
            {

            }
        }




        public static Dictionary<string, JsonElement> LoadJsonData(string filePath)
        {
            try
            {
                string json_data = System.IO.File.ReadAllText(filePath);
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json_data);
            }
            catch (Exception ex)
            {
                return new Dictionary<string, JsonElement>();
            }
        }


        public List<Result> LoadContextMenus(Result selected_result)
        {
            var resultlist = new List<Result>();
            var dict = selected_result.ContextData as Dictionary<string, object>;
            string Title;
            string Subtitle;
            if (dict.ContainsKey("CreateFirst") && (dict["CreateFirst"] is Dictionary<string, object> filtered_query))
            {
                Title = $"Add and Start {dict["Title"]}";
                Subtitle = filtered_query.ContainsKey("Project") ? filtered_query["Project"].ToString() : "";

            }
            else
            {
                Title = $"Start {dict["Title"]}";
                Subtitle = $"{dict["Project_name"]}";

            }
            if (dict.ContainsKey("PageId"))
            {
                var result_timer = new Result
                {
                    Title = Title,
                    SubTitle = Subtitle,


                    Action = c =>

                    {
                        Task.Run(async delegate
                        {

                            if (dict.ContainsKey("CreateFirst") && (dict["CreateFirst"] is Dictionary<string, object> data_Dict_context)) _ = subProcess(create: true, dict_arg: data_Dict_context, open: c.SpecialKeyState.CtrlPressed);
                            await this._toggl.StartTimer(desc: dict["Title"].ToString(), dict.ContainsKey("Tags") ? new List<string> { dict["Tags"].ToString() } : new List<string>(), projectName: Subtitle.ToString());
                        });
                        if (c.SpecialKeyState.CtrlPressed && !dict.ContainsKey("CreateFirst"))
                        {
                            OpenNotionPage(Convert.ToString(dict["Url"]));
                        }

                        if (c.SpecialKeyState.AltPressed)
                        {
                            return false;

                        }
                        else
                        {
                            return true;

                        }



                    },
                    IcoPath = "Images/item_timer_3d.png"
                };
                resultlist.Add(result_timer);

                if (dict["CreateFirst"] is bool)
                {
                    foreach (var PropertyEdit in _settings.Filters)
                    {
                        if (PropertyEdit.JsonType == JsonType.Property)
                        {
                            resultlist.Add(new Result
                            {
                                Title = $"{PropertyEdit.Title} {dict["Title"].ToString()}",
                                SubTitle = PropertyEdit.SubTitle,


                                Action = c =>

                                {
                                    _ = Task.Run(async delegate
                                    {
                                        var response = await DeleteTask(PageId: dict["PageId"].ToString(), payload: PropertyEdit.Json);
                                        if (response.IsSuccessStatusCode)
                                        {
                                            JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                                            JObject properties = (JObject)jsonObject["properties"];
                                            string title = "";
                                            foreach (var kvp in properties)
                                            {
                                                var values = (JObject)kvp.Value;
                                                if (values["type"].ToString() == "title")
                                                {
                                                    title = properties[kvp.Key]["title"][0]["text"]["content"].ToString();
                                                    break; // Assuming there is only one title property, break the loop once found
                                                }
                                            }

                                            /*if (payload == complete_payload)
                                            {
                                                Context.API.ShowMsg("Task Completion", $"{title} has been completed", iconPath: "C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\Images\\item_complete_3d.png");

                                            }
                                            else
                                            {
                                                Context.API.ShowMsg("Task Deletion", $"{title} has been deleted", iconPath: "C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\Images\\item_delete_3d.png");
                                                // Add logic for delete the key from cache
                                            }*/
                                        }
                                        else
                                        {

                                            /*if (payload == complete_payload)
                                            {
                                                Context.API.ShowMsgError($"Task Completion Error: {response.StatusCode}", response.ReasonPhrase);

                                                //Context.API.ShowMsg("Task Completion", $"Unexpected Error");

                                            }
                                            else
                                            {
                                                Context.API.ShowMsgError($"Task Deletion Error: {response.StatusCode}", response.ReasonPhrase);

                                                //Context.API.ShowMsg("Task Deletion", $"Unexpected Error");

                                            }*/
                                        }
                                    });
                                    if (c.SpecialKeyState.CtrlPressed)
                                    {
                                        OpenNotionPage(Convert.ToString(dict["Url"]));

                                    }

                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        return false;

                                    }
                                    else
                                    {
                                        return true;

                                    }



                                },
                                IcoPath = PropertyEdit.IcoPath
                            });


                        }
                    }
                    if (!HiddenItems.Contains(dict["PageId"].ToString()))
                    {
                        var HideItem = new Result
                        {
                            Title = $"Hide {dict["Title"].ToString()}",
                            IcoPath = "Images/cat.webp",



                            Action = c =>

                            {
                                Task.Run(async delegate
                                {
                                    HideItems(new List<string>() {
                                    dict["PageId"].ToString(),
                                    });

                                });

                                if (c.SpecialKeyState.CtrlPressed)
                                {
                                    OpenNotionPage(Convert.ToString(dict["Url"]));

                                }

                                if (c.SpecialKeyState.AltPressed)
                                {
                                    return false;

                                }
                                else
                                {
                                    return true;

                                }


                            },
                        };
                        resultlist.Add(HideItem);
                    }
                    else
                    {
                        var UnHideItem = new Result
                        {
                            Title = $"UnHide {dict["Title"].ToString()}",
                            IcoPath = "Images/cat.webp",



                            Action = c =>

                            {
                                Task.Run(async delegate
                                {
                                    UnHideItems(new List<string>() {
                                    dict["PageId"].ToString(),
                                    });

                                });

                                if (c.SpecialKeyState.CtrlPressed)
                                {
                                    OpenNotionPage(Convert.ToString(dict["Url"]));

                                }

                                if (c.SpecialKeyState.AltPressed)
                                {
                                    return false;

                                }
                                else
                                {
                                    return true;

                                }


                            },
                        };
                        resultlist.Add(UnHideItem);
                    }



                    if (dict.ContainsKey("HideAll") && dict["HideAll"] is List<string> CurrentQueryItems && CurrentQueryItems.Count > 1)
                    {
                        if (!CurrentQueryItems.All(_currenItem => HiddenItems.Contains(_currenItem)))
                        {
                            var HideAll = new Result
                            {
                                Title = $"Hide All Current Query ({CurrentQueryItems.Count})",
                                IcoPath = "Images/cat.webp",



                                Action = c =>

                                {
                                    Task.Run(async delegate
                                    {
                                        HideItems(CurrentQueryItems);

                                    });
                                    Context.API.ShowMsg("Hidden Items", $"{CurrentQueryItems.Count} Item(s) Hidden");



                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        return false;

                                    }
                                    else
                                    {
                                        return true;

                                    }


                                },
                            };
                            resultlist.Add(HideAll);
                        }
                        else
                        {
                            var HideAll = new Result
                            {
                                Title = $"Unhide All Current Query ({CurrentQueryItems.Count})",
                                IcoPath = "Images/cat.webp",



                                Action = c =>

                                {
                                    Task.Run(async delegate
                                    {
                                        UnHideItems(CurrentQueryItems);

                                    });
                                    Context.API.ShowMsg("Unhide Items", $"{CurrentQueryItems.Count} Item(s) Unhidden");



                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        return false;

                                    }
                                    else
                                    {
                                        return true;

                                    }


                                },
                            };
                            resultlist.Add(HideAll);
                        }
                    }
                }



            }
            return resultlist;
        }

        public void OnVisibilityChanged(object _, VisibilityChangedEventArgs e)
        {
            if (e.IsVisible)
            {
                //Context.API.ShowMsg("Your token", _settings.DefaultDatabase);

                DateTime fileInfo = new FileInfo("C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\cache\\cache_search.json").LastWriteTime;
                double minutesDifference = (DateTime.Now - fileInfo).TotalSeconds;
                if (minutesDifference > secondsThreshold)
                {
                    /*     Context.API.ShowMsg("Fire", "done");*/

                    fileInfo = DateTime.Now;
                    Task.Run(async () =>
                    {
                        // await this._NotionDataParser.CallApiForSearch();
                        await this._NotionDataParser.GetStartCursour();
                        // await this._NotionDataParser.BuildCacheUsingHttp();
                        // Add other background tasks if needed
                    });
                    /* subProcess(refresh: true);*/
                }

                foreach (var path in _settings.Filters)
                {
                    if (path.JsonType == JsonType.Filter && path.Enabled && path.Cachable)
                    {
                        _ = Task.Run(async () =>
                        {

                            await this._NotionDataParser.QueryDB(filePath: System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{path.Title}.json"), DB: databaseId[path.Database].GetProperty("id").ToString(), filterPayload: path.Json);

                        });
                    }
                }






            }
        }


        string TagName = string.Empty;
        string ProjectName = string.Empty;
        string DateName = string.Empty;




        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            List<Result> resultList = new List<Result>();

            if (string.IsNullOrEmpty(_settings.InernalInegrationToken))
            {
                var result = new Result
                {
                    Title = "No Api",
                    SubTitle = "Click to open settings and set up your settings",
                    IcoPath = "Images/app.png",
                    Score = 100,
                    Action = c =>
                    {
                        Context.API.OpenSettingDialog();
                        return false;
                    },

                };
                RequestNewCache = true;
                resultList.Add(result);
                return resultList;
            }
            else if (RequestNewCache)
            {
                if (!IsInternetConnected())
                {
                    var result = new Result
                    {
                        Title = "Internet Connection Error",
                        SubTitle = "Please check your internet connection, before build up a cache.",
                        IcoPath = "Images/app.png",
                        Score = 100,
                        Action = c =>
                        {
                            Context.API.OpenSettingDialog();
                            return false;
                        },

                    };
                    resultList.Add(result);
                    return resultList;
                }

                await this._NotionDataParser.DatabaseCache();
                databaseId = LoadJsonData(DatabaseCachePath);

                RequestNewCache = false;
                // if (string.IsNullOrEmpty(_settings.RelationDatabaseId)) 
                // {
                //     var result = new Result
                //     {
                //         Title = "Relation Database Error",
                //         SubTitle = "Please select a database for relations, before build up a cache.",
                //         IcoPath = "Images/app.png",
                //         Score = 100,
                //         Action = c =>
                //         {
                //             Context.API.OpenSettingDialog();
                //             return false;
                //         },

                //     };
                //     resultList.Add(result);
                //     return resultList;
                // }
                // await this._NotionDataParser.QueryDB(_settings.RelationDatabaseId, null, RelationCachePath);
                // ProjectsId = LoadJsonData(RelationCachePath);

            }




            if (string.IsNullOrEmpty(query.Search))
            {
                this._notionBlockTypes._enabled = new Dictionary<int, Func<string, int?, Dictionary<string, object>>>();
                this._notionBlockTypes.additional_options = new Dictionary<int, object>();
                this._notionBlockTypes._default_serialize_fn = this._notionBlockTypes.paragraph;
                TagName = string.Empty;
                ProjectName = string.Empty;
                DateName = string.Empty;


            }

            Dictionary<string, JsonElement> searchResults = LoadJsonData(FullCachePath);
            HiddenItems = File.ReadAllLines(HiddenItemsPath).ToList<string>();

            if (!_settings.Hide)
            {
                foreach (var key in HiddenItems)
                {
                    if (searchResults.ContainsKey(key))
                    {
                        searchResults.Remove(key);
                    }
                    else
                    {
                        List<string> filed = File.ReadAllLines(HiddenItemsPath).ToList<string>();
                        filed.Remove(key);
                        File.WriteAllLines(HiddenItemsPath, filed);
                    }
                }
            }

            string query_string = query.Search;
            string editingPatternId = @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})";

            Match editingPatternIdMatch = Regex.Match(query_string, editingPatternId);
            bool editingMode;
            if (editingPatternIdMatch.Success)
            {
                editingMode = true;
            }
            else
            {
                editingMode = false;
            }

            string link;
            Dictionary<string, object> filtered_query = GetData(query.Search, defaultDB: _settings.DefaultDatabase);
            try
            {
                if (!filtered_query.ContainsKey("Name"))
                {
                    throw new InvalidOperationException("The 'Name' key is not present in filtered_query.");
                }
            }
            catch
            {
                filtered_query["Name"] = "";
            }
            if (editingMode)
            {
                filtered_query["Name"] = filtered_query["Name"].ToString().Replace(editingPatternIdMatch.Groups[1].Value, "").Trim();
            }
            try
            {
                link = $"\n{filtered_query["link"]}";
            }
            catch
            {
                link = "";
            }

            string DBSubtitle;
            try
            {
                DBSubtitle = $"{filtered_query["databaseId"]} selected as a database";
            }
            catch
            {
                DBSubtitle = "";
            }

            string PSubtitle, PTimer;
            try
            {
                PTimer = filtered_query["Project"].ToString();
                if (DBSubtitle == "" || editingMode)
                {
                    PSubtitle = $"{filtered_query["Project"].ToString()} selected as a Project";
                }
                else
                {
                    PSubtitle = $", {filtered_query["Project"].ToString()} selected as a Project";
                }
            }
            catch
            {
                PSubtitle = "";
                PTimer = "";
            }

            string tagSubtitle, tagTimer;
            try
            {
                tagSubtitle = $" :{filtered_query["tags"].ToString()} selected as a Tag";
                tagTimer = $" :{filtered_query["tags"].ToString()}";
            }
            catch
            {
                tagSubtitle = "";
                tagTimer = "";
            }

            var TimeValue = "";
            if (filtered_query.ContainsKey("Time"))
            {

                TimeValue = $" ({filtered_query["Time"]})";


            }
            else
            {
                TimeValue = "";
            }




            Dictionary<string, List<string>> userInputSearch = new Dictionary<string, List<string>>(); // Initialize the dictionary

            if (filtered_query.ContainsKey("filter"))
            {
                string splitQueryFilter = query.Search.Replace(filtered_query["filter"].ToString(), "").ToLower().Trim();

                foreach (var key in searchResults.Keys)
                {
                    if ($"${searchResults[key][1]}$" == filtered_query["filter"].ToString() ||
                        (databaseId.ContainsKey(filtered_query["filter"].ToString().Replace("$", "")) &&
                         databaseId[filtered_query["filter"].ToString().Replace("$", "")].GetProperty("id").ToString() == searchResults[key][2].ToString()))
                    {


                        if (Context.API.FuzzySearch(splitQueryFilter, Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(splitQueryFilter))

                        {
                            string formattedKeyFilter = char.ToUpper(searchResults[key][0].GetString()[0]) + searchResults[key][0].GetString().Substring(1);


                            // Key doesn't exist, create a new list with the values
                            userInputSearch[key] = new List<string>
                                {
                                    $"notion://www.notion.so/{key.Replace("-", "")}",

                                    formattedKeyFilter, // ID
                                    searchResults[key][1].GetString(), // Project of the item
                                    searchResults[key][3].GetString() // Icon_path

                                };

                        }
                    }
                }
            }
            else if (!filtered_query.ContainsKey("filter"))
            {
                foreach (var key in searchResults.Keys)
                {
                    if (Context.API.FuzzySearch(query.Search, Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(query.Search))
                    {

                        string formatted_key = char.ToUpper(searchResults[key][0].GetString()[0]) + searchResults[key][0].GetString().Substring(1);

                        userInputSearch[key] = new List<string>
                        {
                            $"notion://www.notion.so/{key.Replace("-","")}",

                            formatted_key, // Id
                            searchResults[key][1].GetString(), // Project of the item
                            searchResults[key][3].GetString() // Icon_path

                        };


                    }

                }
            }

            bool AdvancedFilterMode = false;

            foreach (var filter in _settings.Filters)
            {


                if (filter.JsonType == JsonType.Filter && filter.Enabled)
                {

                    if (query.Search.ToLower().StartsWith(filter.Title.ToLower()))
                    {
                        AdvancedFilterMode = true;
                        Dictionary<string, JsonElement> today_tasks = filter.Cachable ? LoadJsonData(filePath: System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{filter.Title}.json")) : await this._NotionDataParser.QueryDB(DB: databaseId[filter.Database].GetProperty("id").ToString(), filterPayload: filter.Json);
                        if (today_tasks.Count > 0)
                        {
                            foreach (var item in today_tasks)
                            {

                                if (Context.API.FuzzySearch(query.Search.Replace(filter.Title, string.Empty), item.Key).Score > 0 || string.IsNullOrEmpty(query.Search.Replace(filter.Title, string.Empty)))
                                {
                                    var result = new Result
                                    {
                                        Title = $"{item.Key}",
                                        SubTitle = $"{item.Value[0]}",
                                        AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${item.Value[3]}$",
                                        ContextData = new Dictionary<string, object>
                                {
                                    {"Title", $"{item.Key}" },
                                    { "PageId", $"{item.Value[3]}" },
                                    { "Url", $"{item.Value[1]}" },
                                { "Project_name", $"{item.Value[2]}" },
                                { "Tags", $"{item.Value[0]}" },
                                {"CreateFirst", false},
                            },

                                        Action = c =>
                                        {
                                            OpenNotionPage(Convert.ToString(item.Value[1]));


                                            return true;
                                        },
                                        IcoPath = item.Value[4].ToString()
                                    };

                                    resultList.Add(result);
                                }
                            }
                        }
                    }
                    else if (filter.Title.ToLower().Contains(query.Search.ToLower()) || string.IsNullOrEmpty(query.Search))
                    {
                        resultList.Add(new Result
                        {
                            Title = filter.Title,
                            SubTitle = filter.SubTitle,
                            IcoPath = filter.IcoPath,
                            Score = 100,
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {filter.Title}",
                            Action = c =>
                            {
                                Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {filter.Title}");
                                return false;
                            },

                        });
                    }

                }


            }




            string KeyForId = filtered_query["databaseId"].ToString();

            if (editingMode)
            {
                KeyForId = databaseId.FirstOrDefault(pair => pair.Value.GetProperty("id").ToString() == searchResults[editingPatternIdMatch.Groups[1].Value][2].ToString()).Key;
            }



            if (filtered_query.ContainsKey("Time") && string.IsNullOrEmpty(DateName) && !AdvancedFilterMode)
            {

                JsonElement MultiDateOptions = databaseId[KeyForId].GetProperty("date");
                if (!(MultiDateOptions.EnumerateArray().Count() > 1))
                {
                    DateName = MultiDateOptions.EnumerateArray().FirstOrDefault().ToString();
                }
                else
                {
                    foreach (var _dateName in MultiDateOptions.EnumerateArray())
                    {
                        var result = new Result
                        {
                            Title = _dateName.ToString(),
                            SubTitle = TimeValue,
                            Action = c =>
                            {
                                DateName = _dateName.ToString();
                                Context.API.ChangeQuery(query.ToString() + " ", requery: true);
                                return false;
                            },
                            IcoPath = "Images/database.png"
                        };
                        resultList.Add(result);

                    }
                    return resultList;
                }

            }



            if (query.Search.Contains("!"))
            {
                if (!filtered_query.ContainsKey("Project"))
                {
                    var splitQuery_3 = query_string.Split('!');
                    var userInput_3 = splitQuery_3[^1].Trim();

                    var filteredItems = ProjectsId.Keys
                        .Where(item => item.ToLower().Contains(userInput_3.ToLower()))
                        .ToList();

                    // if (filteredItems.Count != 1)

                    // foreach (var project in filteredItems)
                    // {

                    JsonElement MultiRelationOptions = databaseId[KeyForId].GetProperty("relation");



                    if (!(MultiRelationOptions.EnumerateArray().Count() > 1))
                    {
                        ProjectName = MultiRelationOptions.EnumerateArray().FirstOrDefault().ToString();
                    }

                    if (string.IsNullOrEmpty(ProjectName))
                    {
                        foreach (var _projectName in MultiRelationOptions.EnumerateArray())
                        {
                            if (Context.API.FuzzySearch(query.Search.Split('!')[^1].ToLower().Trim(), _projectName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput_3))
                            {

                                var result = new Result
                                {
                                    Title = _projectName.ToString(),
                                    SubTitle = $"",
                                    Action = c =>
                                    {
                                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {splitQuery_3[0].Trim()}{(splitQuery_3[0].Length > 0 ? " " : "")}!");
                                        ProjectName = _projectName.ToString();
                                        return false;
                                    },
                                    IcoPath = "Images/database.png"
                                };
                                resultList.Add(result);
                            }
                        }
                        return resultList;

                    }
                    else
                    {
                        foreach (var project in filteredItems)
                        {


                            var result = new Result
                            {
                                Title = project,
                                SubTitle = $"",
                                AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${project}$",
                                Score = -1,
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.CtrlPressed)
                                    {
                                        OpenNotionPage(Convert.ToString(ProjectsId[project][1]));
                                        return true;

                                    }
                                    Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {splitQuery_3[0].Trim()}{(splitQuery_3[0].Length > 0 ? " " : "")}!{project} ");
                                    return false;

                                },
                                IcoPath = "Images/database.png"
                            };
                            resultList.Add(result);


                        }
                    }
                }

            }
            else
            {
                ProjectName = null;
            }



            if (!string.IsNullOrEmpty(ProjectName) && filtered_query.ContainsKey("databaseId") && !databaseId[filtered_query["databaseId"].ToString()].GetProperty("relation").EnumerateArray().Any(x => x.GetString() == ProjectName))
            {
                JsonElement MultiRelationOptions = databaseId[KeyForId].GetProperty("relation");



                if (!(MultiRelationOptions.EnumerateArray().Count() > 1))
                {
                    ProjectName = MultiRelationOptions.EnumerateArray().FirstOrDefault().ToString();
                }
                else
                {
                    Context.API.ShowMsg("Bad request propability", "Please reselect the the relation property name");
                    foreach (var _projectName in MultiRelationOptions.EnumerateArray())
                    {

                        var result = new Result
                        {
                            Title = _projectName.ToString(),
                            SubTitle = $"",
                            Action = c =>
                            {
                                Context.API.ChangeQuery(query: query.ToString(), true);
                                ProjectName = _projectName.ToString();
                                return false;
                            },
                            IcoPath = "Images/database.png"
                        };
                        resultList.Add(result);

                    }
                    return resultList;


                }
            }








            if (userInputSearch.Count() > 0 && !AdvancedFilterMode)
            {
                foreach (var item in userInputSearch)
                {
                    var result = new Result
                    {
                        Title = $"{item.Value[1]}",
                        SubTitle = $"{item.Value[2]}",
                        AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${item.Key}$",
                        Score = 50,

                        ContextData = new Dictionary<string, object>
                            {
                                {"Title", $"{item.Value[1]}" },
                                { "PageId", $"{item.Key}" },
                                { "Url", $"{item.Value[0]}" },
                                { "Project_name", $"{item.Value[2]}" },
                                {"CreateFirst", false},
                                {"HideAll", userInputSearch.Keys.ToList<string>()}



                            },


                        Action = c =>
                        {
                            OpenNotionPage(Convert.ToString((item.Value[0])));
                            return true;
                        },
                        IcoPath = item.Value[3]
                    };

                    resultList.Add(result);
                }
            }
            List<string> filteredItems_db = null;
            var split_query_db = query_string.Split('@');
            if (split_query_db.Length == 2)
            {
                var user_input_db = split_query_db[1].Trim();
                filteredItems_db = databaseId.Keys
                .Where(item => item.ToString().ToLower().Contains(user_input_db.ToLower()))
                .ToList();
            }



            if (query.Search.Contains("@") && (filteredItems_db.Count > 1 || filteredItems_db == null))
            {
                var splitQuery = query_string.Split('@');

                foreach (var kv in databaseId)
                {
                    if (Context.API.FuzzySearch(splitQuery[^1].ToLower().Trim(), kv.Key.ToLower().Trim()).Score > 0 || string.IsNullOrEmpty(splitQuery[^1])) {
                        var result = new Result
                        {
                            Title = kv.Key,
                            SubTitle = $"",
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${kv.Key}$",
                            Action = c =>
                            {
                                if(c.SpecialKeyState.CtrlPressed){
                                OpenNotionPage(kv.Value.GetProperty("url").GetString());
                                return true;}
                                else 
                                {
                                    Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {splitQuery[0].Trim()}{(splitQuery[0].Length > 0 ? " " : "")}@{kv.Key} ");
                                    return false;
                                }

                            },
                            IcoPath = kv.Value.GetProperty("icon").GetString()
                        };
                        resultList.Add(result);
                    }
                }

            }

            else if (!query_string.ToLower().StartsWith("search") && query_string != "refresh" && !AdvancedFilterMode && (!query_string.Contains("$") || editingMode))
            {



                if (!(query_string.Contains("*") || query_string.Contains("^")))
                {


                    if (!editingMode)
                    {


                        if (userInputSearch.Count <= 0)
                        {


                            var result = new Result
                            {
                                Title = $"Create {filtered_query["Name"]}",
                                SubTitle = string.Concat(DBSubtitle, PSubtitle, tagSubtitle, link, TimeValue),
                                Score = 4000,
                                ContextData = new Dictionary<string, object>
                                {
                                    {"Title", filtered_query["Name"].ToString() },
                                    { "PageId", null },
                                    { "Url", null },
                                    { "Project_name", filtered_query.ContainsKey("Project") ? filtered_query["Project"]: "" },
                                    {"CreateFirst", filtered_query}


                                },
                                Action = c =>
                                {
                                    Context.API.HideMainWindow();





                                    _ = subProcess(create: true, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed);
                                    refresh_search = DateTime.Now;
                                    /*                                    Context.API.ShowMsg("Item added", filtered_query["Name"], Context.CurrentPluginMetadata.IcoPath);
                                    */

                                    // OpenNotionPage(item.Key,item.Value[1]);

                                    return true;
                                },
                                IcoPath = "Images/app.png"
                            };
                            resultList.Add(result);
                        }
                    }
                    else
                    {
                        string editing_title = "";
                        if (string.IsNullOrEmpty(filtered_query["Name"].ToString().Trim()))
                        {
                            editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0].ToString()}";
                        }
                        else if (!string.IsNullOrEmpty(filtered_query["Name"].ToString()))
                        {
                            editing_title = $"Renaming {filtered_query["Name"]}";
                        }

                        var result = new Result
                        {
                            Title = $"{editing_title}",
                            SubTitle = $"{PSubtitle}{tagSubtitle}{link}{TimeValue}",
                            Score = 99,
                            IcoPath = searchResults[editingPatternIdMatch.Groups[1].Value][3].ToString(),
                            ContextData =
                                    new Dictionary<string, object>
                                    {
                                            { "Title", filtered_query["Name"].ToString().Trim() },
                                            { "tags", new List<string> { tagSubtitle } },
                                            { "Project_name", new List<string> { PSubtitle } },
                                            { "id", editingPatternIdMatch.Groups[1].Value },
                                            { "edit", true },
                                            {"CreateFirst", false},

                                    },

                            Action = c =>
                            {

                                _ = subProcess(edit: true, pageId: editingPatternIdMatch.Groups[1].Value, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed);

                                refresh_search = DateTime.Now;
                                /*new List<object> { editingPatternIdMatch.Groups[1].Value, filtered_query };*/
                                return true;
                            },
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {query.Search} {editing_title.Replace("Edit ", "")}",
                            TitleHighlightData = new List<int> { 8, 9, 10, 11, 12 },


                        };
                        resultList.Add(result);

                    }
                }
                else
                {
                    List<List<string>> contentList = filtered_query["content"] as List<List<string>>;

                    foreach (int block in Enumerable.Range(0, contentList.Count).Reverse())
                    {

                        string[] lines = filtered_query.ContainsKey("content") && filtered_query["content"] is List<List<string>> Index_one
                        ? Index_one[block][1].Split("\n")
                        : new string[0];

                        string[] linesWithoutFirst = lines.Skip(Math.Max(0, lines.Length - 2)).ToArray();
                        string resultString = string.Join(Environment.NewLine, linesWithoutFirst);

                        string subtitleForBlock = (linesWithoutFirst.Length <= 1) ?
                        $"{DBSubtitle}{PSubtitle}{tagSubtitle}{link}{TimeValue}" :
                        "";


                        if (this._notionBlockTypes.additional_options.ContainsKey(block) &&
                            this._notionBlockTypes.additional_options[block] != null &&
                            this._notionBlockTypes.additional_options[block] is Func<string, int?, Dictionary<string, object>> options &&
                            options("", block).Count != 0 && !query.Search.EndsWith($"*{resultString}")
                            )
                        {

                            foreach (var key in options("", block))
                            {
                                if (Context.API.FuzzySearch(resultString, key.Key).Score > 0 || string.IsNullOrEmpty(resultString))
                                {
                                    var result = new Result
                                    {
                                        Title = key.Key,
                                        Score = 4000,
                                        Action = c =>
                                        {
                                            this._notionBlockTypes.additional_options[block] = key.Key.ToString();
                                            Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {(string.IsNullOrEmpty(resultString) ? query.Search : query.Search.Replace(resultString, ""))}", true);
                                            return false;
                                        },
                                        IcoPath = "Images/app.png"
                                    };

                                    resultList.Add(result);
                                }
                            }
                        }
                        else if (!editingMode)
                        {
                            var result = new Result
                            {
                                Title = resultString,
                                SubTitle = subtitleForBlock,
                                Score = 4000,
                                TitleToolTip = "Hold Alt key to paste the clipboard",
                                ContextData = new Dictionary<string, object>
                                {
                                    {"Title", filtered_query["Name"].ToString() },
                                    { "PageId", null },
                                    { "Url", null },
                                    { "Project_name", filtered_query.ContainsKey("Project") ? filtered_query["Project"]: "" },
                                    {"CreateFirst", filtered_query}


                                },
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        Context.API.ChangeQuery($"{query.RawQuery}" + "{clipboard}", true);
                                        return false;
                                    }
                                    Context.API.HideMainWindow();

                                    _ = subProcess(create: true, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed);
                                    refresh_search = DateTime.Now;
                                    /*Context.API.ShowMsg("Item added", filtered_query["Name"], Context.CurrentPluginMetadata.IcoPath);*/


                                    // OpenNotionPage(item.Key,item.Value[1]);
                                    return true;
                                },

                                IcoPath = "Images/app.png"
                            };




                            resultList.AddRange(ProcessQueryResults(query, resultString, result, block));
                        }
                        else
                        {

                            string editing_title = "";
                            if (string.IsNullOrEmpty(filtered_query["Name"].ToString()))
                            {
                                editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0].ToString()}";
                            }
                            else if (!string.IsNullOrEmpty(filtered_query["Name"].ToString()))
                            {
                                editing_title = $"Renaming {filtered_query["Name"]}";
                            }

                            var result = new Result
                            {
                                Title = $"{resultString}",
                                SubTitle = $"{PSubtitle}{tagSubtitle}{link}{TimeValue}",
                                Score = 99,
                                ContextData = new Dictionary<string, object>
                                {
                                    { "desc", filtered_query["Name"].ToString().Trim() },
                                    { "tags", new List<string> { tagSubtitle } },
                                    { "project", new List<string> { PSubtitle } },
                                    { "query", filtered_query },
                                    { "id", editingPatternIdMatch.Groups[1].Value },
                                    { "edit", true },
                                    {"CreateFirst", false},

                                },
                                TitleToolTip = "Hold Alt key to paste the clipboard",
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        Context.API.ChangeQuery($"{query.RawQuery}" + "{clipboard}", true);
                                        return false;
                                    }
                                    Context.API.HideMainWindow();
                                    _ = subProcess(edit: true, pageId: editingPatternIdMatch.Groups[1].Value, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed);
                                    refresh_search = DateTime.Now;
                                    new List<object> { editingPatternIdMatch.Groups[1].Value, filtered_query };
                                    return true;
                                },
                                AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${editingPatternIdMatch.Groups[1].Value}$ *",

                                IcoPath = "Images/app.png"
                            };






                            resultList.AddRange(ProcessQueryResults(query, resultString, result, block));
                        }
                    }
                }
            }



            if (query.Search.Contains("#"))
            {
                var userInput = query.Search.Split('#')[^1].Trim();
                JsonElement MultiSelectOptions = databaseId[KeyForId].GetProperty("multi_select");
                try
                {
                    if (!(MultiSelectOptions.EnumerateObject().Count() > 1))
                    {
                        TagName = MultiSelectOptions.EnumerateObject().First().Name;
                    }

                    if (string.IsNullOrEmpty(TagName))
                    {
                        foreach (var _tagName in MultiSelectOptions.EnumerateObject())
                        {
                            var result = new Result
                            {
                                Title = _tagName.Name,
                                SubTitle = $"",
                                Action = c =>
                                {
                                    Context.API.ChangeQuery(query: query.ToString(), true);
                                    TagName = _tagName.Name;
                                    return false;
                                },
                                IcoPath = "Images/database.png"
                            };
                            resultList.Add(result);
                        }
                        return resultList;

                    }
                    else
                    {
                        foreach (var _tagName in MultiSelectOptions.GetProperty(TagName).EnumerateArray())
                        {
                            if (Context.API.FuzzySearch(userInput, _tagName.ToString()).Score > 1 || string.IsNullOrEmpty(userInput))
                            {
                                var result = new Result
                                {
                                    Title = _tagName.ToString(),
                                    SubTitle = $"",
                                    Action = c =>
                                    {
                                        // TagName = _tagName.Name;
                                        return false;
                                    },
                                    IcoPath = "Images/database.png"
                                };
                                resultList.Add(result);
                            }
                        }
                    }
                }
                catch { }


            }
            else
            {
                TagName = null;
            }
            List<Result> ProcessQueryResults(Query query, string resultString, Result modifiedResult, int block)
            {

                List<Result> result = new List<Result>();
                bool executeElseBlock = true;

                if (!query.Search.EndsWith($"^{resultString}"))
                {
                    executeElseBlock = false;
                    List<Result> blockChildResults = this._notionBlockTypes.SetBlockChild(query.Search, resultString, block);

                    if (blockChildResults.Count != 0)
                    {

                        result.AddRange(blockChildResults);

                        // Append logic of Multiple blocks type
                    }
                    else
                    {
                        executeElseBlock = true;
                    }
                }

                if (executeElseBlock)
                {
                    modifiedResult.IcoPath = $"Images/{(this._notionBlockTypes._enabled.ContainsKey(block) ? this._notionBlockTypes._enabled[block].Method.Name : this._notionBlockTypes._default_serialize_fn.Method.Name)}.png";
                    modifiedResult.Score = modifiedResult.Score * (block != 0 ? block * 2 : 1);
                    modifiedResult.Title = modifiedResult.Title.Trim();
                    result.AddRange(new List<Result> { modifiedResult });
                }

                return result;
            }

            return resultList;
        }




        async Task subProcess(bool refresh = false, Dictionary<string, object> dict_arg = null, string pageId = null, bool create = false, bool edit = false, bool open = false)
        {


            if (create)
            {
                await Task.Run(async () =>
                {
                    await CreatePage(dict_arg, open: open);

                });
                await Task.Run(async () =>
                {
                    await this._NotionDataParser.GetStartCursour(delay: 14000);
                });
            }
            if (edit)
            {
                await Task.Run(async () =>
                {
                    await EditTask(pageId: pageId, filteredQueryEditing: dict_arg, open: open);
                    // await Build_up_search_chache.GetStartCursour(manuanl_cursour: pageId);


                });
            }
            if (refresh)
            {
                DateTime fileInfo = new FileInfo("C:\\Users\\mohammed\\AppData\\Roaming\\FlowLauncher\\Plugins\\Flow.Launcher.Plugin.Search\\cache\\cache_search.json").LastWriteTime;
                double minutesDifference = (DateTime.Now - fileInfo).TotalSeconds;


                await Task.Run(async () =>
                {
                    //await this._NotionDataParser.QueryDB(filePath: today_tasks_cache_path, DB: databaseId["Tasks"][0].ToString(), filterPayload: );

                    if (minutesDifference > secondsThreshold)
                    {
                        /*     Context.API.ShowMsg("Fire", "done");*/

                        fileInfo = DateTime.Now;
                        await this._NotionDataParser.GetStartCursour();

                        /* subProcess(refresh: true);*/
                    }

                });
            }











        }









        public Control CreateSettingPanel()
        {
            return new NotionSettings(Context, Main._viewModel!);
        }



        public void Dispose()
        {
            Context.API.VisibilityChanged -= OnVisibilityChanged;

        }


        /*public string GetTranslatedPluginTitle()
        {
            return Context.API.GetTranslation("plugin_helloworldcsharp_plugin_name");
        }

        public string GetTranslatedPluginDescription()
        {
            return Context.API.GetTranslation("plugin_helloworldcsharp_plugin_description");
        }*/



















        Dictionary<string, object> GetData(string inputString, string defaultDB = "", bool TimeSkip = false, bool ManualDBRunning = false)
        {

            Dictionary<string, object> dataDict = new Dictionary<string, object>();
            string pattern = @"(\$[a-zA-Z\s\.\-\#\|\(\)ا-ي]*\$)|(@\s?[a-zA-Z0-9]*)|(!\s?[a-zA-Z0:9\._-]*)|(#\s?[a-zA-Z0:9]*)|((?:\*|\^)+\s?[\\""\{\}\<\>\!\[\]\@\`\(\)\#\%\+\-\,\?=/\\\da-zA-Z\s\'_.ا-ي\,\&\;\:]*)|(\[\s?[/\#\-\:a-zA-Z0-9/.&=_?]*]?)|\s?([\-\|\:\da-zA-Z\s\'_.ا-ي]*)";

            var match = Regex.Matches(inputString, pattern);

            /*foreach (Match matc in match)
            {
                Console.WriteLine(matc);

            }*/
            var dataList = match.Cast<Match>().SelectMany(m => m.Groups.Cast<Group>().Skip(1)).Select(g => g.Value.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();

            foreach (var type in dataList)
            {
                if (type.StartsWith("$") && type.EndsWith("$"))
                {
                    dataDict["filter"] = type.Trim();
                    break;
                }
                if (type.StartsWith("!") && !string.IsNullOrEmpty(ProjectName))
                {
                    var splitQuery = type.Split('!', 2);
                    var userInput = splitQuery[1].Trim();
                    if (splitQuery.Length == 2)
                    {
                        var filteredItems = ProjectsId.Keys.Where(item => item.ToLower().Contains(userInput.ToLower())).ToList();
                        if (filteredItems.Count == 1)
                        {
                            dataDict["Project"] = string.Join("", filteredItems);

                        }
                    }
                }
                if (type.StartsWith("@"))
                {

                    var splitQuery = type.Split('@', 2);
                    var userInput = splitQuery[1].Trim();

                    if (splitQuery.Length == 2)
                    {

                        var filteredItems = databaseId.Keys.Where(item => item.ToLower().Contains(userInput.ToLower())).ToList();

                        if (filteredItems.Count == 1)

                        {

                            dataDict["databaseId"] = string.Join("", filteredItems);
                        }
                    }
                }

                // Condition for 'in '
                if (type.StartsWith("in "))
                {
                    dataDict["Name_dir"] = type;
                }

                // Condition for 'on '
                if (type.StartsWith("on "))
                {
                    dataDict["Name_dir"] = type;
                }

                if (type.StartsWith("["))
                {
                    var splitQuery = type.Split('[', StringSplitOptions.None);

                    if (type.Contains("]"))
                    {
                        splitQuery = splitQuery[1].Split(']', StringSplitOptions.None);
                        var userInput = splitQuery[0].Trim();
                        dataDict["link"] = userInput;
                    }
                    else
                    {
                        var userInput = splitQuery[1].Trim();
                        dataDict["link"] = userInput;
                    }
                }


                // Condition for #
                if (type.Contains("#") && !string.IsNullOrEmpty(TagName))
                {
                    try
                    {
                        if (!dataDict.ContainsKey("databaseId") && !string.IsNullOrEmpty(defaultDB))
                        {
                            dataDict["databaseId"] = defaultDB;
                        }

                        var splitQuery = type.Split('#', 2);

                        // Assuming databaseId[dictionaryKey] returns a JsonElement
                        var databaseElement = databaseId[dataDict["databaseId"].ToString()];


                        if (splitQuery.Length == 2)
                        {
                            //var tagsArray = databaseElement[3];
                            var userInput = splitQuery[1].Trim();
                            var tagsArray = databaseElement.GetProperty("multi_select").GetProperty(TagName);

                            var availableTags = tagsArray.EnumerateArray().Select(item => item.GetString()).ToList();

                            var filteredTags = availableTags.Where(tag => tag != null && tag.ToLower().Contains(userInput.ToLower())).ToList();

                            if (filteredTags.Count > 0)
                            {
                                if (!dataDict.ContainsKey("tags"))
                                {
                                    // Initialize 'tags' as a list of strings
                                    var filteredTagss = availableTags.Where(tag => tag != null && tag.ToLower().Contains(userInput.ToLower())).ToList();

                                    dataDict["tags"] = (string.Join(",", filteredTags));
                                }
                                else
                                {
                                    dataDict["tags"] = $"{dataDict["tags"]},{string.Join(",", filteredTags)}";
                                }
                            }


                        }
                    }
                    catch
                    {

                    }
                }


                if (!(type.StartsWith("!") || type.StartsWith("@") || type.StartsWith("in") || type.StartsWith("on") || type.StartsWith("#") || type.StartsWith("*") || type.StartsWith("^") || type.StartsWith("[") || (type.StartsWith("$") && type.EndsWith("$"))))
                {
                    if (!type.Contains($"$ {type}") && !type.Contains($"{type}$"))
                    {
                        
                        
                        dataDict["Name"] = type.Trim();
                    }
                }



                /*  if (type.StartsWith("^") || type.StartsWith("*"))
                  {
                      var typeFormatted = type.Remove(0, 1).Trim();
                      var formattedString = Regex.Replace(typeFormatted, @"\\n", "\n");
                      dataDict["content"] = formattedString;
                  }*/


                // Condition for *
                if (type.StartsWith("^") || type.StartsWith("*"))
                {
                    var typeFormatted = type.Remove(0, 1);
                    var formattedString = Regex.Replace(typeFormatted, @"\\n", "\n");
                    if (!dataDict.ContainsKey("content"))
                    {
                        dataDict["content"] = new List<List<string>>();
                    }


                    ((List<List<string>>)dataDict["content"]).Add(new List<string>
                    {
                    "paragraph", formattedString
                    });
                }








            }


            if (!dataDict.ContainsKey("Project") && inputString.Contains("!") && !string.IsNullOrEmpty(ProjectName))
            {
                string ProjectPattern = @"(!\s?[\sa-zA-Z0-9\#]*)";
                var ProjectMatch = Regex.Matches(inputString, ProjectPattern);
                if (ProjectMatch.Count > 0)
                {
                    var splitQuery = ProjectMatch[0].Value.Split('!');
                    var userInput = splitQuery[1].Trim();

                    foreach (var item in ProjectsId.Keys)
                    {
                        if (Context.API.FuzzySearch(item, userInput).Score > 0)
                        {
                            dataDict["Project"] = item;
                            string TransformName = Regex.Replace(userInput, item, "", RegexOptions.IgnoreCase).Trim();
                            if (GetData(Regex.Replace(inputString.Trim(), $@"\s?!\s?{item}", "", RegexOptions.IgnoreCase).Trim(), defaultDB: _settings.DefaultDatabase, TimeSkip: true).TryGetValue("Name", out object Name))
                            {
                                dataDict["Name"] = Name.ToString();
                            }
                            else
                            {
                                dataDict["Name"] = TransformName;
                            }

                        }
                    }
                }


            }
            if (!ManualDBRunning){

            if (!dataDict.ContainsKey("databaseId") && inputString.Contains("@"))
            {
                string Pattern = @"(@\s?[\sa-zA-Z0-9]*)";
                var DatabaseMatch = Regex.Matches(inputString, Pattern);
                if (DatabaseMatch.Count > 0)
                {
                    var splitQuery = DatabaseMatch[0].Value.Split('@');
                    var userInput = splitQuery[1].Trim();
                    foreach (var item in databaseId.Keys)
                    {
                        if (Context.API.FuzzySearch(item, userInput).Score > 0)
                        {
                            dataDict["databaseId"] = item.Trim();
                            string TransformName = Regex.Replace(userInput, item, "", RegexOptions.IgnoreCase).Trim();
                            if (GetData(Regex.Replace(inputString.Trim(), $@"\s?\@\s?{item}", "", RegexOptions.IgnoreCase).Trim(), defaultDB: _settings.DefaultDatabase, TimeSkip: true, ManualDBRunning: true).TryGetValue("Name", out object Name))
                            {
                                dataDict["Name"] = Name.ToString();
                            }
                            else
                            {
                                dataDict["Name"] = TransformName;
                            }

                        }
                    }
                }

            }}




            if (!TimeSkip)
            {

                if (!dataDict.ContainsKey("Time"))
                {
                    string[] array_returned;

                    try
                    {
                        if (dataDict.ContainsKey("Name_dir") && dataDict.ContainsKey("Name"))
                        {
                            array_returned = next_occurrence_of_day($" {dataDict["Name_dir"]}");
                        }
                        else if (dataDict.ContainsKey("Name_dir") && !dataDict.ContainsKey("Name"))
                        {
                            array_returned = next_occurrence_of_day($" {dataDict["Name_dir"]}");
                            if (!string.IsNullOrEmpty(array_returned[0]))
                            {
                                dataDict["Name"] = array_returned[1];

                            }
                            else
                            {
                                dataDict["Name"] = dataDict["Name_dir"];
                            }


                        }
                        else
                        {

                            array_returned = next_occurrence_of_day(dataDict["Name"].ToString());
                            if (!string.IsNullOrEmpty(array_returned[0]))
                            {
                                dataDict["Name"] = array_returned[1];
                            }

                        }
                        if (!string.IsNullOrEmpty(array_returned[0]))
                        {
                            dataDict["Name"] = dataDict["Name"].ToString().Trim();
                            dataDict["Time"] = array_returned[2].ToString();
                            dataDict["parsedDate"] = array_returned[0].ToString(); // Convert later to dateTime

                        }
                    }
                    catch
                    {

                    }

                }
            }


            
            if (!dataDict.ContainsKey("databaseId") && !string.IsNullOrEmpty(defaultDB))
            {
                dataDict["databaseId"] = defaultDB;
            }

            return dataDict;
        }
        public string[] next_occurrence_of_day(string input = "")
        {
            string parsedDate = string.Empty;
            string parsedText = string.Empty;

            var results = DateTimeRecognizer.RecognizeDateTime(input, "en-us");

            if (results.Any())
            {
                Console.WriteLine($"I found the following date and time entities ({results.Count():d}):");

                // Serialize results to JSON
                var jsonResults = System.Text.Json.JsonSerializer.SerializeToNode(results.First(), new JsonSerializerOptions { WriteIndented = true });
                parsedDate = Convert.ToString(jsonResults["Resolution"]["values"].AsArray().Last()["value"]);
                if (string.IsNullOrEmpty(parsedDate))
                {
                    parsedDate = Convert.ToString(jsonResults["Resolution"]["values"].AsArray().Last()["start"]);

                }
                parsedText = jsonResults["Text"].ToString();

                Console.WriteLine(jsonResults);
                string returned_name = input.Replace($"on {parsedText}", "");
                if (returned_name == input)
                {
                    returned_name = input.Replace($"in {parsedText}", "");
                }
                if (returned_name == input)
                {
                    returned_name = input.Replace($"{parsedText}", "").Trim();
                }
                returned_name = returned_name.Replace("  ", " ");

                DateTime parsedclock = Convert.ToDateTime(parsedDate);
                string dayName = parsedclock.ToString("ddd", CultureInfo.InvariantCulture);
                if (parsedclock.TimeOfDay != TimeSpan.Zero)
                {
                    dayName = $"{dayName} | {parsedclock.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
                }
                else
                {
                    string daySuffix = GetDaySuffix(parsedclock.Day);
                    dayName = $"{dayName} | {parsedclock.Day + daySuffix} {parsedclock.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";

                }

                return new string[] { parsedDate, returned_name, dayName };

            }
            else
            {
                return new string[] { string.Empty, string.Empty, string.Empty, string.Empty };
            }
            static string GetDaySuffix(int day)
            {
                if (day >= 11 && day <= 13)
                {
                    return "th";
                }

                switch (day % 10)
                {
                    case 1:
                        return "st";
                    case 2:
                        return "nd";
                    case 3:
                        return "rd";
                    default:
                        return "th";
                }
            }
        }



        void OpenNotionPage(string url)
        {
            if (_settings.UseBrowser)
            {
                Context.API.OpenUrl(new Uri(url.Replace("notion://", "https://")));

            }
            else
            {
                Context.API.OpenUrl(new Uri(url));

            }
        }



        (Dictionary<string, Dictionary<string, object>>, Dictionary<string, List<Dictionary<string, object>>>, string) FormatData(Dictionary<string, object> filtered_data_arg, string default_DB = null, string DbNameInCache = null)
        {

            string DATABASE_ID = null;
            Dictionary<string, List<Dictionary<string, object>>> children = new Dictionary<string, List<Dictionary<string, object>>> {
                { "children", new List<Dictionary<string, object>>() }};


            dataDict = filtered_data_arg;

            if (DbNameInCache != null)
            {
                dataDict["databaseId"] = DbNameInCache;
            }
            try
            {
                if (!dataDict.ContainsKey("databaseId"))
                {
                    /*dataDict["databaseId"] = default_DB*/
                    ;
                }
                if (dataDict.ContainsKey("databaseId"))
                {
                    DATABASE_ID = Convert.ToString(databaseId[dataDict["databaseId"].ToString()].GetProperty("id").ToString());
                }


            }
            catch
            {

            }

            var data = new Dictionary<string, Dictionary<string, object>> { };

            if (!string.IsNullOrWhiteSpace(dataDict["Name"].ToString()))
            {

                data[$"{databaseId[dataDict["databaseId"].ToString()].GetProperty("title").ToString()}"] = new Dictionary<string, object>
                {
                    { "title", new List<Dictionary<string, object>>
                        {
                            new Dictionary<string, object>
                            {
                                { "text", new Dictionary<string, object>
                                    {
                                        { "content", dataDict["Name"] }
                                    }
                                }
                            }
                        }
                    }
                };


            };

            if (dataDict.ContainsKey("parsedDate"))
            {
                string parsed_result_string;
                DateTime parsed_result = Convert.ToDateTime(dataDict["parsedDate"]);
                if (parsed_result.TimeOfDay != TimeSpan.Zero)
                {
                    parsed_result = parsed_result.ToUniversalTime();
                    parsed_result_string = parsed_result.ToString("yyyy-MM-ddTHH:mm:ss");
                }
                else
                {
                    //parsed_result = parsed_result.ToUniversalTime();
                    parsed_result_string = parsed_result.ToString("yyyy-MM-dd");
                }

                if (!data.ContainsKey(DateName))
                {
                    data.Add(DateName, new Dictionary<string, object>());
                }
                data[DateName].Add("date", new Dictionary<string, object> { { "start", parsed_result_string }, { "end", null } });
            }


            if (dataDict.ContainsKey("tags"))
            {
                var tags_string = dataDict["tags"];
                var tags = tags_string.ToString().Split(",");
                var tag_options = new List<Dictionary<string, string>> { };
                foreach (var tag in tags)
                {
                    tag_options.Add(new Dictionary<string, string> { { "name", Convert.ToString(tag) } });
                }

                if (!data.ContainsKey(TagName))
                {
                    data.Add(TagName, new Dictionary<string, object>());
                }
                data[TagName].Add("multi_select", tag_options);
            }

            if (dataDict.ContainsKey("link"))
            {
                data[Convert.ToString(databaseId[dataDict["databaseId"].ToString()].GetProperty("url").ToString())] = new Dictionary<string, object> { { "url", dataDict["link"] } };
            }


            if (dataDict.ContainsKey("Project"))
            {
                var ProjectRelationID = ProjectsId[Convert.ToString(dataDict["Project"]).Trim()][3];
                if (!data.ContainsKey("Project"))
                {
                    data.Add(ProjectName, new Dictionary<string, object>());
                }
                data[ProjectName].Add("relation", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id", ProjectRelationID } } });
            }



            if (dataDict.ContainsKey("content"))
            {
                var contentItem = (List<List<string>>)dataDict["content"];
                foreach (int block in Enumerable.Range(0, contentItem.Count))
                {

                    if (this._notionBlockTypes.additional_options.ContainsKey(block))
                    {
                        if (!(this._notionBlockTypes.additional_options[block] is string))
                        {
                            this._notionBlockTypes.additional_options[block] = "";
                        }
                    }
                    else
                    {
                        this._notionBlockTypes.additional_options[block] = "";
                    }

                    if (this._notionBlockTypes._enabled.ContainsKey(block))
                    {

                        children["children"].Add(this._notionBlockTypes._enabled[block](contentItem[block][1], block));
                    }
                    else
                    {
                        // default value for Blocks.
                        children["children"].Add(this._notionBlockTypes._default_serialize_fn(contentItem[block][1], block));
                    }
                    /*if (this._notionBlockTypes._enabled.ContainsKey(block)){
                        if (this._notionBlockTypes._enabled[block] == "bookmark")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateBookmarkChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "embed")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateEmbedChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "image")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateImageChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "video")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateVideoChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "link_preview")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateLinkPreviewChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "quote")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateQuoteChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "bulleted_list")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateBulletedListChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "numbered_list")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateNumberedListChildren(contentItem[block][1]));
                        }
                        else if (this._notionBlockTypes._enabled[block] == "to_do")
                        {
                            children["children"].Add(this._notionBlockTypes.CreateNToDoChildren(contentItem[block][1]));
                        }
                        else
                        {
                            children["children"].Add(this._notionBlockTypes.CreateParagraphChildren(contentItem[block][1]));

                        }
                    }
                    else
                    {
                        children["children"].Add(this._notionBlockTypes.CreateParagraphChildren(contentItem[block][1]));
                    }*/
                }
                return (data, children, DATABASE_ID);


            }
            else
            {
                children = null;
                return (data, children, DATABASE_ID);


            }
        }

        private async Task CreatePage(Dictionary<string, object> Datadict, Dictionary<string, List<Dictionary<string, object>>> children = null, string DatabaseId = null, bool open = false)
        {
            try
            {
                var (data_return, children_return, DATABASE_ID) = FormatData(Datadict);
                var data = data_return;
                children = children_return;

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

                    string createUrl = "https://api.notion.com/v1/pages";

                    Dictionary<string, object> payload = new Dictionary<string, object>
                {

                    { "parent", new Dictionary<string, object> { { "database_id", DATABASE_ID } } },
                    { "properties", data }
                };

                    if (children != null)
                    {

                        payload["children"] = children["children"];
                    }

                    string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });

                    StringContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                    Console.WriteLine(payloadJson);

                    HttpResponseMessage response = await client.PostAsync(createUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);

                        Context.API.ShowMsg($"A new item added into ({dataDict["databaseId"]})",
                                            $"{dataDict["Name"]}\n{((dataDict.ContainsKey("Project")) ? $"<{dataDict["Project"]}>" : "")}", iconPath: Context.CurrentPluginMetadata.IcoPath);

                        Console.WriteLine("Page created successfully.");

                        if (open)
                        {
                            string created_PageID = jsonObject["id"].ToString().Replace("-", "");
                            string notionUrl = $"notion://www.notion.so/{created_PageID}";
                            try
                            {
                                OpenNotionPage(notionUrl);

                                /*ProcessStartInfo psi = new ProcessStartInfo
                                {
                                    FileName = notionUrl,
                                    UseShellExecute = true
                                };

                                Process.Start(psi);*/
                            }
                            catch
                            {

                            }
                        }
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Error: {response.StatusCode}", response.ReasonPhrase);

                        Console.WriteLine($"Error: {response.StatusCode} - {response.ReasonPhrase}");
                    }
                }
            }
            catch
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                }
                else
                {
                    Context.API.ShowMsgError($"Internet Connection Error", "Please check your internet connection.");

                }

            }
        }

        static bool IsInternetConnected()
        {
            try
            {
                // Ping a well-known server, such as Google's public DNS (8.8.8.8)
                Ping ping = new Ping();
                PingReply reply = ping.Send("8.8.8.8", 500); // Timeout set to 2000 milliseconds (2 seconds)

                return (reply != null && reply.Status == IPStatus.Success);
            }
            catch (PingException)
            {
                // An exception occurred during the ping attempt
                return false;
            }
        }

        async Task EditTask(bool open, string pageId, Dictionary<string, object> filteredQueryEditing, List<string> fromContext = null)
        {

            Dictionary<string, JsonElement> searchResults = LoadJsonData(FullCachePath);
            try
            {
                var (data, children, DatabaseId) = FormatData(filteredQueryEditing, DbNameInCache: databaseId.FirstOrDefault(kv => Convert.ToString(kv.Value.GetProperty("id").ToString()) == Convert.ToString(searchResults[pageId][2])).Key);
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = null;
                    string EditUrl;
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

                    Dictionary<string, object> payload = new Dictionary<string, object> { };

                    if (children != null)
                    {
                        EditUrl = $"https://api.notion.com/v1/blocks/{pageId}/children";
                        string payloadJson = System.Text.Json.JsonSerializer.Serialize(children, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        StringContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                        response = await client.PatchAsync(EditUrl, content);
                        // Console.WriteLine(payloadJson);


                    }


                    if (data.Count != 0)
                    {
                        {
                            payload["properties"] = data;
                            EditUrl = $"https://api.notion.com/v1/pages/{pageId}";
                            string payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                            StringContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                            response = await client.PatchAsync(EditUrl, content);



                        }
                    }
                    if (response != null && response.IsSuccessStatusCode)
                    {

                        Context.API.ShowMsg($"An Existing item Edited", " ", iconPath: Context.CurrentPluginMetadata.IcoPath);



                        if (open)
                        {
                            string created_PageID = pageId.Replace("-", "");
                            string notionUrl = $"notion://www.notion.so/{created_PageID}";
                            try
                            {
                                OpenNotionPage(notionUrl);


                                /*ProcessStartInfo psi = new ProcessStartInfo
                                {
                                    FileName = notionUrl,
                                    UseShellExecute = true
                                };

                                Process.Start(psi);*/
                            }
                            catch
                            {

                            }
                        }


                        _ = Task.Run(async () =>
                        {
                            await this._NotionDataParser.GetStartCursour(delay: 14000, manuanl_cursour: pageId);

                        });






                    }
                    else
                    {
                        Context.API.ShowMsgError($"Error: {response.StatusCode}", response.ReasonPhrase);
                    }



                }
            }
            catch
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                }
                else
                {
                    Context.API.ShowMsgError($"Internet Connection Error", "Please check your internet connection.");

                }
            }


        }
        object complete_payload = new { properties = new { Status = new { status = new { name = "✅" } } } };
        object del_payload = new { archived = true };


        void HideItems(List<string> ItemsId)
        {
            /*foreach (var item in ItemsId)
            {
                if (!HiddenItems.Contains(item))
                    File.AppendAllLinesAsync(HiddenItemsPath, new string[] {item});
            }*/

            File.AppendAllLines(HiddenItemsPath, ItemsId);

        }
        void UnHideItems(List<string> ItemId)
        {
            HiddenItems.RemoveAll(_item => ItemId.Contains(_item));
            File.WriteAllLines(HiddenItemsPath, HiddenItems);
        }
        async Task<HttpResponseMessage> DeleteTask(string PageId, string payload, List<string> fromContext = null)
        {

            using (HttpClient client = new HttpClient())
            {




                StringContent Archive = new StringContent(JsonConvert.SerializeObject(JsonConvert.DeserializeObject<dynamic>(payload)), Encoding.UTF8, "application/json");
                string delete_url = $"https://api.notion.com/v1/pages/{PageId}";
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                HttpResponseMessage response = await client.PatchAsync(delete_url, Archive);
                return response;

            }
        }


    }
}