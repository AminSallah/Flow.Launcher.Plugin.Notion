using System.Windows.Controls;
using System.Text.Json.Nodes;
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
using System.Runtime.CompilerServices;
using Microsoft.Recognizers.Text;
using Flow.Launcher.Plugin.SharedModels;

namespace Flow.Launcher.Plugin.Notion
{
    public class Main : IAsyncPlugin, IContextMenu, ISettingProvider, IAsyncReloadable
    {
        DateTime refresh_search = DateTime.Now;
        public static ApiCacheManager _apiCacheManager;
        public static int secondsThreshold = 30;
        private static string DatabaseCachePath;
        private static string RelationCachePath;
        private static string FullCachePath;
        public static string HiddenItemsPath;
        public static string ImagesPath;
        static Dictionary<string, object> dataDict = new Dictionary<string, object>();
        private PluginInitContext Context;
        internal NotionBlockTypes? _notionBlockTypes;
        internal NotionDataParser? _notionDataParser;
        private static SettingsViewModel? _viewModel;
        private Settings? _settings;
        internal static string CustomImagesDirectory;
        private bool RequestNewCache = false;
        private bool ShowTags = false;

        public static List<string> HiddenItems = new List<string>();
        public static Dictionary<string, JsonElement> databaseId = LoadJsonData(RelationCachePath);
        public static Dictionary<string, JsonElement> ProjectsId = LoadJsonData(RelationCachePath);

        public Dictionary<string, JsonElement> searchResults = LoadJsonData(FullCachePath);

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            ImagesPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images");
            this._settings = context.API.LoadSettingJsonStorage<Settings>();

            string cacheDirectory = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache");
            string icons = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Icons", "icons");
            DirExist(cacheDirectory);
            DirExist(icons);

            DatabaseCachePath = System.IO.Path.Combine(cacheDirectory, "database.json");
            _settings.DatabaseCachePath = DatabaseCachePath;
            PathExist(_settings.DatabaseCachePath);
            RelationCachePath = System.IO.Path.Combine(cacheDirectory, "relation.json");
            _settings.RelationCachePath = RelationCachePath;
            PathExist(_settings.RelationCachePath);
            HiddenItemsPath = System.IO.Path.Combine(cacheDirectory, "HiddenItems.txt");
            PathExist(HiddenItemsPath);
            FullCachePath = System.IO.Path.Combine(cacheDirectory, "search.json");
            _settings.FullCachePath = FullCachePath;
            PathExist(_settings.FullCachePath);
            try
            {
                this._notionBlockTypes = new NotionBlockTypes(this.Context);
                this._notionDataParser = new NotionDataParser(this.Context, _settings);
                _apiCacheManager = new ApiCacheManager(context);
            }
            catch { }

            if (IsInternetConnected())
            {
                try
                {
                    if (!string.IsNullOrEmpty(_settings.InernalInegrationToken))
                    {
                        databaseId = await this._notionDataParser.DatabaseCache();

                        //_settings.RelationDatabaseId = databaseId[_settings.RelationDatabase].GetProperty("id").ToString();

                        if (!string.IsNullOrEmpty(_settings.RelationDatabaseId))
                        {
                            ProjectsId = await this._notionDataParser.QueryDB(_settings.RelationDatabaseId, null, RelationCachePath);
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

            if (databaseId.Count == 0)
                RequestNewCache = true;

            Main._viewModel = new SettingsViewModel(this._settings);

            CustomImagesDirectory = System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Icons", "CustomIcons");
            DirExist(CustomImagesDirectory);
            Context.API.VisibilityChanged += OnVisibilityChanged;


            try
            {
                _ = Task.Run(async () =>
                {
                    await this._notionDataParser.CallApiForSearch();
                    // await this._notionDataParser.BuildCacheUsingHttp();
                });
            }
            catch
            {

            }
        }
        void PathExist(string path)
        {
            if (!Path.Exists(path))
            {
                File.Create(path);
            }

        }
        void DirExist(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }
        }

        public static Dictionary<string, JsonElement> LoadJsonData(string filePath)
        {
            try
            {
                string json_data = System.IO.File.ReadAllText(filePath);
                return System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json_data);
            }
            catch
            {
                return new Dictionary<string, JsonElement>();
            }
        }


        public List<Result> LoadContextMenus(Result selected_result)
        {
            var resultlist = new List<Result>();
            var dict = selected_result.ContextData as Dictionary<string, object>;

            if (dict.ContainsKey("PageId"))
            {
                if (dict["CreateFirst"] is bool)
                {
                    foreach (var PropertyEdit in _settings.Filters)
                    {
                        if (PropertyEdit.Enabled && PropertyEdit.JsonType == JsonType.Property &&
                            (PropertyEdit.Databases.Contains(searchResults[dict["PageId"].ToString()][2].GetString()) || PropertyEdit.Databases.Count == 0))
                        {
                            resultlist.Add(new Result
                            {
                                Title = $"{PropertyEdit.Title} {dict["Title"]}",
                                SubTitle = PropertyEdit.SubTitle,
                                Action = c =>
                                {
                                    _ = Task.Run(async delegate
                                    {
                                        var response = await EditPropertyFromContext(PageId: dict["PageId"].ToString(), payload: PropertyEdit.Json);
                                        if (response.IsSuccessStatusCode)
                                        {
                                            JObject EditedObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                                            JObject properties = (JObject)EditedObject["properties"];
                                            string title = "";
                                            foreach (var kvp in properties)
                                            {
                                                var values = (JObject)kvp.Value;
                                                if (values["type"].ToString() == "title")
                                                {
                                                    title = properties[kvp.Key]["title"][0]["text"]["content"].ToString();
                                                    break;
                                                }
                                            }

                                            if (Convert.ToBoolean(EditedObject["archived"]))
                                            {
                                                Context.API.ShowMsg("Page Deletion", $"{title} has been deleted", iconPath: Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Images", "item_delete.png"));
                                                searchResults.Remove(dict["PageId"].ToString());
                                                string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions { WriteIndented = true });
                                                File.WriteAllText(_settings.FullCachePath, jsonString);
                                            }
                                            else
                                            {
                                                Context.API.ShowMsg("Edit Page Success", $"{title} has been Edited", iconPath: Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Images", "item_complete.png"));
                                            }
                                        }
                                        else
                                        {
                                            if (response.ReasonPhrase == "Bad Request")
                                            {
                                                Context.API.ShowMsgError("Custom Property Payload Error", "Please check out custom payload from settings panel");
                                                Context.API.OpenSettingDialog();
                                            }
                                            else if (response != null)
                                            {
                                                Context.API.ShowMsgError($"Page Edit Error: {response.StatusCode}", response.ReasonPhrase);
                                            }
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
                            Title = $"Hide {dict["Title"]}",
                            Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ued1a"),
                            Action = c =>
                            {
                                Task.Run(async delegate
                                {
                                    HideItems(new List<string>() {
                                    dict["PageId"].ToString(),
                                    });
                                });
                                Context.API.ShowMsg("Hide Item", $"{dict["Title"]} has been hidden.");
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
                            Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ued1a"),
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
                                Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ued1a"),
                                Action = c =>
                                {
                                    Task.Run(async delegate
                                    {
                                        HideItems(CurrentQueryItems);
                                    });
                                    Context.API.ShowMsg("Hide Items", $"{CurrentQueryItems.Count} Items have been hidden.");
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
                                Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ued1a"),
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

        Dictionary<string, Task> BGTasks = new Dictionary<string, Task>();
        public async void OnVisibilityChanged(object _, VisibilityChangedEventArgs e)
        {
            if (e.IsVisible)
            {
                if (_settings.FailedRequests && IsInternetConnected())
                {
                    _ = Task.Run(async () =>
                    {
                        await RetryCachedFunctions();
                    });
                }
                DateTime fileInfo = new FileInfo(_settings.FullCachePath).LastWriteTime;
                double minutesDifference = (DateTime.Now - fileInfo).TotalSeconds;
                if (minutesDifference > secondsThreshold)
                {
                    fileInfo = DateTime.Now;
                    _ = Task.Run(async () =>
                    {
                        // await this._notionDataParser.CallApiForSearch();
                        await this._notionDataParser.GetStartCursour();
                    });
                }

                foreach (var path in _settings.Filters)
                {
                    if (path.JsonType == JsonType.Filter && path.Enabled && path.CacheType != 0)
                    {
                        BGTasks[path.Title] = Task.Run(async () =>
                        {
                            await this._notionDataParser.QueryDB(filePath: System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{path.Title}.json"), DB: databaseId[path.Databases[0]].GetProperty("id").ToString(), filterPayload: path.Json);
                        });
                    }
                }
            }
        }


        string TagName = string.Empty;          // Used to store (map) Multiselect Property Name.
        string ProjectName = string.Empty;      // Used to store (map) Relation Property Name.
        string DateName = string.Empty;         // Used to store (map) Date Property Name.
        bool timeForce = false;                 // Used to Check whether database changed after choose date property or not.
        string UrlMap = string.Empty;           // Used to store (map) Url Property Name.


        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            List<Result> resultList = new List<Result>();
            bool IsWritingBlock = query.Search.Contains("*") || query.Search.Contains("^");

            if (string.IsNullOrEmpty(_settings.InernalInegrationToken))
            {
                var result = new Result
                {
                    Title = "No Internal Integration Token Provided",
                    SubTitle = "Click to open settings and set up your settings",
                    IcoPath = "Images/error.png",
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
                        IcoPath = "Images/error.png",
                        Score = 100,
                        Action = c =>
                        {
                            // Context.API.OpenSettingDialog();
                            return false;
                        },
                    };
                    resultList.Add(result);
                    return resultList;
                }

                await this._notionDataParser.DatabaseCache();
                databaseId = LoadJsonData(DatabaseCachePath);
                RequestNewCache = false;
            }

            if (databaseId.Count() == 0)
            {
                await this._notionDataParser.DatabaseCache();
                databaseId = LoadJsonData(DatabaseCachePath);
            }

            if (string.IsNullOrEmpty(_settings.DefaultDatabase) || databaseId.Count() == 0)
            {
                resultList.Add(new Result
                {
                    Title = "No databases linked with Internal Inegration token.",
                    SubTitle = "Please ensure at least one database is shared with token.",
                    IcoPath = "Images/error.png"
                });
                return resultList;
            }
            
            searchResults = LoadJsonData(FullCachePath);
            
            if (searchResults == null || searchResults.Count() == 0)
            {
                resultList.Add(new Result
                {
                    Title = "No pages linked with Internal Inegration token.",
                    SubTitle = "Please ensure at least two pages are shared with token.",
                    IcoPath = "Images/error.png"
                });
                return resultList;
            }

            if (string.IsNullOrEmpty(query.Search))
            {
                this._notionBlockTypes._enabled = new Dictionary<int, Func<string, int?, Dictionary<string, object>>>();
                this._notionBlockTypes.additional_options = new Dictionary<int, object>();
                this._notionBlockTypes._default_serialize_fn = this._notionBlockTypes.paragraph;
                DateName = string.Empty;
            }

            HiddenItems = File.ReadAllLines(HiddenItemsPath).ToList<string>();

            Dictionary<string, object> filtered_query = GetData(query.Search, defaultDB: _settings.DefaultDatabase);

            string editingPatternId = @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})";

            Match editingPatternIdMatch = Regex.Match(query.Search, editingPatternId);
            bool editingMode;
            if (editingPatternIdMatch.Success)
            {
                editingMode = true;
                if (!string.IsNullOrEmpty(searchResults[editingPatternIdMatch.Groups[1].Value][2].GetString()))
                    filtered_query["databaseId"] = searchResults[editingPatternIdMatch.Groups[1].Value][2].GetString();
            }
            else
            {
                editingMode = false;
            }

            string link;

            if (!filtered_query.ContainsKey("Name"))
            {
                filtered_query["Name"] = string.Empty;
            }

            if (editingMode)
            {
                filtered_query["Name"] = filtered_query["Name"].ToString().Replace(editingPatternIdMatch.Groups[1].Value, "").Trim();
            }

            if (filtered_query.ContainsKey("link"))
            {
                link = $"\n{filtered_query["link"]}";
            }
            else
            {
                link = string.Empty;
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

            string PSubtitle = string.Empty;

            if (filtered_query.ContainsKey("Project"))
            {
                if (DBSubtitle == "")
                {
                    PSubtitle = $"{filtered_query["Project"]} selected as a Project";
                }
                else
                {
                    DBSubtitle = DBSubtitle.Replace(" selected as a database", " / ");
                    PSubtitle = $"{filtered_query["Project"]}";
                }
            }

            string tagSubtitle = string.Empty;
            if (filtered_query.ContainsKey("tags"))
            {
                if (((List<string>)filtered_query["tags"]).Count == 1)
                {
                    tagSubtitle = $" :{((List<string>)filtered_query["tags"])[0]} selected as a Tag";
                }
                else
                {
                    tagSubtitle = $" :{string.Join(",", (List<string>)filtered_query["tags"])}";
                }
            }

            var TimeValue = string.Empty;
            if (filtered_query.ContainsKey("Time"))
            {
                TimeValue = $" ({filtered_query["Time"]})";
            }


            Dictionary<string, List<string>> userInputSearch = new Dictionary<string, List<string>>();

            if (filtered_query.ContainsKey("filter"))
            {
                string splitQueryFilter = query.Search.Replace(filtered_query["filter"].ToString(), "").ToLower().Trim();

                foreach (var key in searchResults.Keys)
                {
                    if ($"${searchResults[key][1]}$" == filtered_query["filter"].ToString() ||
                            (databaseId.ContainsKey(filtered_query["filter"].ToString().Replace("$", "")) &&
                                filtered_query["filter"].ToString().Replace("$", "") == searchResults[key][2].ToString()))
                    {
                        if (Context.API.FuzzySearch(RefineQueryText(splitQueryFilter, splitQueryFilter, filtered_query), Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(splitQueryFilter))
                        {
                            userInputSearch[key] = new List<string>
                                {
                                    $"notion://www.notion.so/{key.Replace("-", "")}", // Url
                                    searchResults[key][0].GetString(), // Name
                                    searchResults[key][1].GetString(), // Project of the item
                                    searchResults[key][3].GetString(), // Icon_path
                                    BuildPathChain(key)

                                };
                        }
                    }
                }
            }
            else if (!filtered_query.ContainsKey("filter"))
            {
                foreach (var key in searchResults.Keys)
                {
                    if (Context.API.FuzzySearch(RefineQueryText(query.Search.Trim(), query.Search.Trim(), filtered_query), Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(query.Search))
                    {
                        userInputSearch[key] = new List<string>
                        {
                            $"notion://www.notion.so/{key.Replace("-","")}", // Url
                            searchResults[key][0].GetString(), // Name
                            searchResults[key][1].GetString(), // Project of the item
                            searchResults[key][3].GetString(), // Icon_path
                            BuildPathChain(key)

                        };
                    }
                }
            }


            if (!_settings.Hide)
            {
                foreach (var key in HiddenItems)
                {
                    if (userInputSearch.ContainsKey(key))
                    {
                        userInputSearch.Remove(key);
                    }
                    else if (!searchResults.ContainsKey(key))
                    {
                        HiddenItems.Remove(key);
                        File.WriteAllLines(HiddenItemsPath, HiddenItems);
                    }
                }
            }

            bool AdvancedFilterMode = false;
            bool CreateMode = true;

            foreach (var filter in _settings.Filters)
            {
                if (filter.JsonType == JsonType.Filter && filter.Enabled)
                {
                    if (query.Search.ToLower().StartsWith(filter.Title.ToLower()))
                    {
                        AdvancedFilterMode = true;
                        Dictionary<string, JsonElement> FilterData = new Dictionary<string, JsonElement>();
                        if (filter.CacheType != 0)
                        {
                            if (!BGTasks[filter.Title].IsCompleted)
                                if (filter.CacheType == CacheTypes.BuildAndWait)
                                    await BGTasks[filter.Title];
                                else if (filter.CacheType == CacheTypes.BuildWithTimeout)
                                    // Give filter time to complete if not; load old cache
                                    await Task.WhenAny(BGTasks[filter.Title], Task.Delay(filter.Timeout));

                            FilterData = LoadJsonData(Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{filter.Title}.json"));
                        }
                        else
                        {
                            FilterData = await this._notionDataParser.QueryDB(DB: databaseId[filter.Databases[0]].GetProperty("id").ToString(), filterPayload: filter.Json);
                        }

                        if (FilterData.Count > 0)
                        {
                            foreach (var item in FilterData)
                            {
                                if (Context.API.FuzzySearch(query.Search.Replace(filter.Title, string.Empty, StringComparison.CurrentCultureIgnoreCase).ToLower(),
                                    item.Value[0].GetString().ToLower()).Score > 0 ||
                                        string.IsNullOrEmpty(query.Search.Replace(filter.Title, string.Empty, StringComparison.CurrentCultureIgnoreCase)))
                                {
                                    var result = new Result
                                    {
                                        Title = $"{item.Value[0]}",
                                        SubTitle = $"{item.Value[1]}",
                                        AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${item.Key}$",
                                        ContextData = new Dictionary<string, object>
                                        {
                                            {"Title", $"{item.Value[0]}" },
                                            { "PageId", $"{item.Key}" },
                                            { "Url", $"{item.Value[2]}" },
                                            { "DBName", $"{item.Value[5]}"},
                                            { "Project_name", $"{item.Value[3]}"},
                                            { "Tags", $"{item.Value[1]}" },
                                            { "CreateFirst", false},
                                            { "HideAll", FilterData.Keys.ToList<string>()}
                                        },
                                        Action = c =>
                                        {
                                            OpenNotionPage(Convert.ToString(item.Value[2]));
                                            return true;
                                        },
                                        IcoPath = item.Value[4].ToString()
                                    };
                                    resultList.Add(result);
                                }
                            }

                            return resultList;
                        }
                    }
                    else if (filter.Title.ToLower().Contains(query.Search.ToLower()) || string.IsNullOrEmpty(query.Search))
                    {
                        resultList.Add(new Result
                        {
                            Title = filter.Title,
                            SubTitle = filter.SubTitle + (filter.CacheType != CacheTypes.Disabled && filter.Count ? $" ({LoadJsonData(Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{filter.Title}.json")).Count})" : ""),
                            IcoPath = filter.IcoPath,
                            Score = 100,
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {filter.Title}",
                            Action = c =>
                            {
                                Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {filter.Title} ");
                                return false;
                            },
                        });

                        CreateMode = false;
                    }
                }
            }

            string KeyForId = filtered_query["databaseId"].ToString();

            if (editingMode)
            {
                KeyForId = searchResults[editingPatternIdMatch.Groups[1].Value][2].GetString();
            }

            if (query.Search.Contains("!") && !IsWritingBlock && !Escaped(query.Search, "!"))
            {
                if (ProjectsId.Count == 0)
                {
                    ProjectsId = LoadJsonData(_settings.RelationCachePath);
                    if (ProjectsId.Count == 0)
                    {
                        ProjectsId = await this._notionDataParser.QueryDB(_settings.RelationDatabaseId, null, RelationCachePath);
                    }
                }
                if (!string.IsNullOrEmpty(_settings.RelationDatabaseId))
                {
                    if (!filtered_query.ContainsKey("Project"))
                    {
                        var splitQuery = query.Search.Split('!');
                        var userInput = splitQuery[^1].Trim();
                        JsonElement MultiRelationOptions;
                        try
                        {
                            MultiRelationOptions = databaseId[KeyForId].GetProperty("relation");
                        }
                        catch (KeyNotFoundException)
                        {
                            var ErrorResult = new Result
                            {
                                Title = "Relation property can not be assigned to pages",
                                SubTitle = "Notion only support assign relation properties to database items.",
                                Action = c =>
                                {
                                    return true;
                                },
                                IcoPath = "Images/error.png"
                            };
                            resultList.Add(ErrorResult);
                            return resultList;
                        }

                        if (MultiRelationOptions.EnumerateArray().Count() == 1)
                        {
                            ProjectName = MultiRelationOptions.EnumerateArray().FirstOrDefault().ToString();
                        }
                        else if (MultiRelationOptions.EnumerateArray().Count() == 0)
                        {
                            var ErrorResult = new Result
                            {
                                Title = "Database does not contain any relation properties",
                                SubTitle = "click to open database page to create a relation property",
                                Action = c =>
                                {
                                    OpenNotionPage(databaseId[KeyForId].GetProperty("url").GetString());
                                    return true;
                                },
                                IcoPath = "Images/error.png"
                            };
                            resultList.Add(ErrorResult);
                            return resultList;
                        }

                        if (string.IsNullOrEmpty(ProjectName))
                        {
                            foreach (var _projectName in MultiRelationOptions.EnumerateArray())
                            {
                                if (Context.API.FuzzySearch(query.Search.Split('!')[^1].ToLower().Trim(), _projectName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                                {

                                    var result = new Result
                                    {
                                        Title = _projectName.ToString(),
                                        SubTitle = $"",
                                        Action = c =>
                                        {
                                            Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword}{ConcatSplitedQuery(splitQuery, "!")}!", true);
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
                            foreach (var project in ProjectsId)
                            {
                                if (Context.API.FuzzySearch(splitQuery[^1].ToLower(), project.Value[0].GetString().ToLower()).Score > 1 || string.IsNullOrEmpty(splitQuery[^1]))
                                {
                                    var result = new Result
                                    {
                                        Title = project.Value[0].GetString(),
                                        SubTitle = $"",
                                        AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${project.Value[0]}$",
                                        Score = -1,
                                        Action = c =>
                                        {
                                            if (c.SpecialKeyState.CtrlPressed)
                                            {
                                                OpenNotionPage(project.Value[2].ToString());
                                                return true;
                                            }

                                            Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword}{ConcatSplitedQuery(splitQuery, "!")}!{project.Value[0]} ");
                                            return false;
                                        },
                                        IcoPath = project.Value[4].ToString()
                                    };
                                    resultList.Add(result);
                                }
                            }
                            return resultList;
                        }
                    }
                }
                else
                {
                    resultList.Add(
                        new Result
                        {
                            Title = "No Relation Database Selected",
                            SubTitle = "Click to open settings dialog to select relation database",
                            Action = c =>
                            {
                                Context.API.OpenSettingDialog();
                                return true;
                            },
                            IcoPath = "Images/error.png"
                        }
                    );
                    return resultList;
                }
            }
            else if (!IsWritingBlock)
            {
                ProjectName = null;
            }

            bool BadRequestPropability(string mapField, string property, bool EnumerateArray = true)
            {
                if (!string.IsNullOrEmpty(mapField) &&
                    filtered_query.ContainsKey("databaseId"))
                    if (EnumerateArray)
                        return !databaseId[filtered_query["databaseId"].ToString()].GetProperty(property)
                                .EnumerateArray().Any(x => x.GetString() == mapField);
                    else
                        return !databaseId[filtered_query["databaseId"].ToString()].GetProperty(property)
                                .EnumerateObject().Any(x => x.Name == mapField);
                else
                    return false;
            }

            if (BadRequestPropability(ProjectName, "relation"))
            {
                JsonElement MultiRelationOptions = databaseId[KeyForId].GetProperty("relation");

                if (!(MultiRelationOptions.EnumerateArray().Count() > 1))
                {
                    ProjectName = MultiRelationOptions.EnumerateArray().FirstOrDefault().ToString();
                }
                else
                {
                    string[] splitQuery = query.Search.Split('@', 2, options: StringSplitOptions.None);
                    splitQuery = splitQuery[^1].Split(" ", 2, StringSplitOptions.None);
                    string userInput;
                    if (splitQuery.Length != 2)
                        userInput = string.Empty;
                    else
                    {
                        userInput = splitQuery[1];
                    }

                    if (string.IsNullOrEmpty(userInput))
                    {
                        Context.API.ShowMsg("Bad request propability", "Please reselect the the relation property name");
                    }
                    foreach (var _projectName in MultiRelationOptions.EnumerateArray())
                    {
                        if (Context.API.FuzzySearch(userInput.ToLower(), _projectName.GetString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                        {
                            var result = new Result
                            {
                                Title = _projectName.ToString(),
                                SubTitle = $"",
                                Action = c =>
                                {
                                    Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " + query.Search.Split("@")[0] + "@" + filtered_query["databaseId"].ToString() + " ", true);
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
            }

            if (!query.Search.EndsWith("\\#") && query.Search.EndsWith("#"))
                ShowTags = true;
            if (query.Search.Contains("#") && !IsWritingBlock && !Escaped(query.Search, "#"))
            {
                var splitQuery = query.Search.Split('#');
                var userInput = splitQuery[^1].Trim();
                JsonElement MultiSelectOptions;
                try
                {
                    MultiSelectOptions = databaseId[KeyForId].GetProperty("multi_select");
                }
                catch (KeyNotFoundException)
                {
                    var ErrorResult = new Result
                    {
                        Title = "multi-selection property can not be assigned to pages",
                        SubTitle = "Notion only support assign multi-selection properties to database items.",
                        Action = c =>
                        {
                            return true;
                        },
                        IcoPath = "Images/error.png"
                    };
                    resultList.Add(ErrorResult);
                    return resultList;
                }
                try
                {
                    if (MultiSelectOptions.EnumerateObject().Count() == 1)
                    {
                        TagName = MultiSelectOptions.EnumerateObject().First().Name;
                    }
                    else if (MultiSelectOptions.EnumerateObject().Count() == 0)
                    {
                        var ErrorResult = new Result
                        {
                            Title = "Database does not contain any multi-selection properties",
                            SubTitle = "click to open database page to create a multi-selection property",
                            Action = c =>
                            {
                                OpenNotionPage(databaseId[KeyForId].GetProperty("url").GetString());
                                return true;
                            },
                            IcoPath = "Images/error.png"
                        };
                        resultList.Add(ErrorResult);
                        return resultList;
                    }
                    // True of this Indicate user is backspaced the # charachter
                    if (ShowTags && filtered_query.TryGetValue("tags", out object _tags) && _tags is List<string> Tags)
                    {
                        if (Tags.Contains(splitQuery[^1].Trim()) || !query.Search.EndsWith($"#{userInput}"))
                        {
                            ShowTags = false;

                        }
                        else if (userInput.Contains(Tags[0]))
                        {
                            ShowTags = false;
                        }
                    }
                    if (string.IsNullOrEmpty(TagName))
                    {
                        foreach (var _tagName in MultiSelectOptions.EnumerateObject())
                        {
                            if (Context.API.FuzzySearch(userInput.ToLower(), _tagName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                            {
                                var result = new Result
                                {
                                    Title = _tagName.Name,
                                    SubTitle = $"",
                                    Action = c =>
                                    {
                                        Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + ConcatSplitedQuery(splitQuery, "#") + "#", true);
                                        TagName = _tagName.Name;
                                        return false;
                                    },
                                    IcoPath = "Images/database.png"
                                };
                                resultList.Add(result);
                            }
                        }
                        return resultList;

                    }
                    else if (ShowTags)
                    {
                        foreach (var _tagName in MultiSelectOptions.GetProperty(TagName).EnumerateArray())
                        {
                            if (filtered_query.TryGetValue("tags", out object _assignedTags) &&
                                 _assignedTags is List<string> AssignedTags && AssignedTags.Contains(_tagName.ToString()))
                            {
                                // User It's already selected this tag no need to show it again.
                                continue;
                            }
                            if (Context.API.FuzzySearch(userInput, _tagName.ToString()).Score > 1 || string.IsNullOrEmpty(userInput))
                            {
                                var result = new Result
                                {
                                    Title = _tagName.ToString(),
                                    SubTitle = $"",
                                    Score = 50,
                                    Action = c =>
                                    {
                                        ShowTags = false;
                                        Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + ConcatSplitedQuery(splitQuery, "#") + "#" + _tagName.ToString() + " ", true);
                                        return false;
                                    },
                                    IcoPath = "Images/database.png"
                                };
                                resultList.Add(result);

                            }
                        }
                        return resultList;
                    }
                }
                catch { }
            }
            else
            {
                ShowTags = false;
                TagName = null;
            }

            if (BadRequestPropability(TagName, "multi_select", false))
            {
                TagName = null;
                // Context.API.ChangeQuery(query.RawQuery.Replace($"#{((List<string>)filtered_query["tags"])[0]}",""),true);
            }

            if (filtered_query.ContainsKey("Time") && (timeForce || string.IsNullOrEmpty(DateName)) && !AdvancedFilterMode)
            {
                JsonElement MultiDateOptions = databaseId[KeyForId].GetProperty("date");
                if (!(MultiDateOptions.EnumerateArray().Count() > 1))
                {
                    DateName = MultiDateOptions.EnumerateArray().FirstOrDefault().ToString();
                }
                else
                {
                    string[] splitQuery = query.Search.Split(filtered_query["TimeText"].ToString());
                    string userInput;
                    if (splitQuery.Length < 2)
                        userInput = string.Empty;
                    else
                    {
                        userInput = splitQuery[^1];
                    }
                    foreach (var _dateName in MultiDateOptions.EnumerateArray())
                    {
                        if (Context.API.FuzzySearch(userInput.ToLower(), _dateName.GetString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                        {
                            var result = new Result
                            {
                                Title = _dateName.ToString(),
                                SubTitle = TimeValue,
                                Action = c =>
                                {
                                    DateName = _dateName.ToString();
                                    timeForce = false;
                                    Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + ConcatSplitedQuery(splitQuery, filtered_query["TimeText"] as string) + filtered_query["TimeText"].ToString().Trim() + " ");
                                    // Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " +
                                    // (string.IsNullOrEmpty(splitQuery[^2].Trim().Replace(filtered_query["TimeText"].ToString(), "",
                                    //  culture: null, ignoreCase: true)) ? ""
                                    //  : splitQuery[^2].Trim() + " ") + filtered_query["TimeText"].ToString().Trim() + " ", requery: true);
                                    return false;
                                },
                                IcoPath = "Images/database.png"
                            };
                            resultList.Add(result);
                        }
                    }
                    var cancel = new Result
                    {
                        Title = "Cancel",
                        SubTitle = TimeValue,
                        Action = c =>
                        {
                            Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " +
                            (splitQuery[^2].Trim() + " " + "\\" + filtered_query["TimeText"].ToString().Trim()).Trim() + " ", requery: true);
                            return false;
                        },
                        Score = -1000,
                        IcoPath = "Images/error.png"
                    };
                    resultList.Add(cancel);

                    return resultList;
                }
            }

            if (userInputSearch.Count() > 0 && !AdvancedFilterMode)
            {
                foreach (var item in userInputSearch)
                {
                    var result = new Result
                    {
                        Title = $"{(string.IsNullOrWhiteSpace(item.Value[1]) ? "Untitled" : item.Value[1])}",
                        SubTitle = $"{item.Value[4]}",
                        AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${item.Key}$",
                        Score = 50,
                        ContextData = new Dictionary<string, object>
                            {
                                {"Title", $"{item.Value[1]}" },
                                { "PageId", $"{item.Key}" },
                                { "Url", $"{item.Value[0]}" },
                                { "DBName", item.Value[4]},
                                { "Project_name", $"{item.Value[2]}" },
                                { "CreateFirst", false},
                                { "HideAll", userInputSearch.Keys.ToList<string>()}
                            },
                        Action = c =>
                        {
                            OpenNotionPage(Convert.ToString(item.Value[0]));
                            return true;
                        },
                        IcoPath = item.Value[3]
                    };
                    resultList.Add(result);
                }
            }

            if (query.Search.Contains("@") && (bool)filtered_query["IsDefaultDB"] == true && !editingMode && !IsWritingBlock && !Escaped(query.Search, "@"))
            {
                var splitQuery = query.Search.Split('@');
                timeForce = true;
                foreach (var kv in databaseId)
                {
                    if (Context.API.FuzzySearch(splitQuery[^1].ToLower().Trim(), kv.Key.ToLower().Trim()).Score > 1 || string.IsNullOrEmpty(splitQuery[^1]))
                    {
                        var result = new Result
                        {
                            Title = kv.Key,
                            SubTitle = $"",
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} ${kv.Key}$",
                            TitleToolTip = "Hold Alt to select as default database\nHold Ctrl to open database page",
                            Action = c =>
                            {
                                if (c.SpecialKeyState.CtrlPressed)
                                {
                                    OpenNotionPage(kv.Value.GetProperty("url").GetString());
                                    return true;
                                }
                                else
                                {
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        _settings.DefaultDatabase = kv.Key;
                                        Context.API.ShowMsg("Changing Default Database", $"The database ({kv.Key}) has been successfully set as the default database.");
                                    }
                                    Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword}{ConcatSplitedQuery(splitQuery, "@")}@{kv.Key} ");
                                    return false;
                                }
                            },
                            IcoPath = kv.Value.GetProperty("icon").GetString()
                        };
                        resultList.Add(result);
                    }
                }
                return resultList;
            }

            if (query.Search.Contains("[") && filtered_query.ContainsKey("link") && string.IsNullOrEmpty(UrlMap) && !IsWritingBlock)
            {
                JsonElement.ArrayEnumerator UrlMapOptions = databaseId[filtered_query["databaseId"].ToString()].GetProperty("urlMap").EnumerateArray();
                if (UrlMapOptions.Count() > 1)
                {
                    var splitQuery = query.Search.Split("[");

                    foreach (var _urlOption in UrlMapOptions)
                    {
                        if (Context.API.FuzzySearch(splitQuery[^1].ToLower().Trim(), _urlOption.GetString().ToLower().Trim()).Score > 1 || string.IsNullOrEmpty(splitQuery[1]))
                        {
                            var result = new Result
                            {
                                Title = _urlOption.GetString(),
                                IcoPath = "Images/embed.png",
                                Action = c =>
                                {
                                    UrlMap = _urlOption.GetString();
                                    Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " + query.Search.Split("[")[0] + "[", true);
                                    return false;
                                }
                            };
                            resultList.Add(result);
                        }
                    }
                    return resultList;

                }
                else
                {
                    UrlMap = UrlMapOptions.First().GetString();
                }
            }
            else if (!query.Search.Contains("[") && !IsWritingBlock)
            {
                UrlMap = null;
            }


            // if (!query.Search.ToLower().StartsWith("search") && query.Search != "refresh" && !AdvancedFilterMode && (!query.Search.Contains("$") || editingMode))
            if (!AdvancedFilterMode && (!query.Search.Contains("$") || editingMode) && CreateMode)
            {
                if (!IsWritingBlock)
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
                                    { "CreateFirst", filtered_query}
                                },
                                Action = c =>
                                {
                                    Context.API.HideMainWindow();
                                    if (c.SpecialKeyState.ShiftPressed)
                                    {
                                        ToggleDefaultAfterCreateAction();
                                    }
                                    _ = subProcess(create: true, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed != _settings.PopUpPageAfterCreate);
                                    refresh_search = DateTime.Now;
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
                            editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0]}";
                        }
                        else if (!string.IsNullOrEmpty(filtered_query["Name"].ToString()))
                        {
                            editing_title = $"Renaming {filtered_query["Name"]}";
                        }
                        string SubTitle = string.IsNullOrEmpty(PSubtitle) ? $"{tagSubtitle}{link}{TimeValue}" : $"{DBSubtitle}{PSubtitle}{tagSubtitle}{link}{TimeValue}";
                        var result = new Result
                        {
                            Title = $"{editing_title}",
                            SubTitle = SubTitle,
                            Score = 99,
                            IcoPath = searchResults[editingPatternIdMatch.Groups[1].Value][3].ToString(),
                            ContextData =
                                    new Dictionary<string, object>
                                    {
                                        { "Title", string.IsNullOrEmpty(filtered_query["Name"].ToString().Trim()) ? searchResults[editingPatternIdMatch.Groups[1].Value][0].GetString() : filtered_query["Name"].ToString() },
                                        { "tags", new List<string> { tagSubtitle } },
                                        { "Project_name", filtered_query.ContainsKey("Project") ? filtered_query["Project"]: searchResults[editingPatternIdMatch.Groups[1].Value][1].GetString() },
                                        { "id", editingPatternIdMatch.Groups[1].Value },
                                        { "edit", true },
                                        { "CreateFirst", new object()}
                                    },
                            Action = c =>
                            {
                                _ = subProcess(edit: true, pageId: editingPatternIdMatch.Groups[1].Value, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed);
                                refresh_search = DateTime.Now;
                                return true;
                            },
                            AutoCompleteText = $"{Context.CurrentPluginMetadata.ActionKeyword} {query.Search} {editing_title.Replace("Edit ", "")}",
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
                        string.Concat(DBSubtitle, PSubtitle, tagSubtitle, link, TimeValue) :
                        "";
                        if (this._notionBlockTypes.additional_options.ContainsKey(block) &&
                            this._notionBlockTypes.additional_options[block] != null &&
                            this._notionBlockTypes.additional_options[block] is Func<string, int?, Dictionary<string, object>> options &&
                            options("", block).Count != 0 && !query.Search.EndsWith($"*{resultString}")
                            )
                        {
                            // Loop on Notion block type options to select desired option
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
                                            // Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword} {(string.IsNullOrEmpty(resultString) ? query.Search : query.Search.Replace(resultString, ""))}", true);
                                            Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword}{this.ConcatSplitedQuery(query.Search.Split("^"), "^")}^", true);
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
                                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword + " " + query.Search}" + "{clipboard}", true);
                                        return false;
                                    }
                                    Context.API.HideMainWindow();

                                    if (c.SpecialKeyState.ShiftPressed)
                                    {
                                        ToggleDefaultAfterCreateAction();
                                    }

                                    _ = subProcess(create: true, dict_arg: filtered_query, open: c.SpecialKeyState.CtrlPressed != _settings.PopUpPageAfterCreate);
                                    refresh_search = DateTime.Now;
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
                                editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0]}";
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
                                        Context.API.ChangeQuery($"{Context.CurrentPluginMetadata.ActionKeyword + " " + query.Search}" + "{clipboard}", true);
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
                    }
                    else
                    {
                        executeElseBlock = true;
                    }
                }

                if (executeElseBlock)
                {
                    modifiedResult.IcoPath = $"Images/{(this._notionBlockTypes._enabled.ContainsKey(block) ? this._notionBlockTypes._enabled[block].Method.Name : this._notionBlockTypes._default_serialize_fn.Method.Name)}.png";
                    // modifiedResult.Score = modifiedResult.Score * (block != 0 ? block * 2 : 1);
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

            }
            if (edit)
            {
                await Task.Run(async () =>
                {
                    await EditPageMainProperty(pageId: pageId, filteredQueryEditing: dict_arg, open: open);
                });
            }
            if (refresh)
            {
                DateTime fileInfo = new FileInfo(_settings.FullCachePath).LastWriteTime;
                double minutesDifference = (DateTime.Now - fileInfo).TotalSeconds;
                await Task.Run(async () =>
                {
                    if (minutesDifference > secondsThreshold)
                    {
                        fileInfo = DateTime.Now;
                        await this._notionDataParser.GetStartCursour();
                    }
                });
            }
        }
        void ToggleDefaultAfterCreateAction()
        {
            _settings.PopUpPageAfterCreate = !_settings.PopUpPageAfterCreate;
            if (!_settings.PopUpPageAfterCreate)
            {
                Context.API.ShowMsg("Manual Open", "Pages Created Won't Automatically Open by Default Upon Creation.");
            }
            else
            {
                Context.API.ShowMsg("Automatic Open", "Pages Created Will Automatically Open by Default Upon Creation.");
            }
        }


        private string ConcatSplitedQuery(string[] splitQuery, string Prefix)
        {
            string QueryName = string.Empty;
            if (splitQuery.Length > 2)
            {
                List<string> QueryList = splitQuery.ToList();
                QueryList.RemoveAt(QueryList.Count - 1);
                QueryName = " " + string.Join($"{Prefix}", QueryList);
                return QueryName;
            }
            else
            {
                QueryName = " " + splitQuery[^2];
            }
            return QueryName;
        }

        public Control CreateSettingPanel()
        {
            return new NotionSettings(Context, Main._viewModel!);
        }


        public void Dispose()
        {
            Context.API.VisibilityChanged -= OnVisibilityChanged;
        }

        public async Task ReloadDataAsync()
        {
            var projectsTask = _notionDataParser.QueryDB(_settings.RelationDatabaseId, null, _settings.RelationCachePath);
            var databaseIdTask = _notionDataParser.DatabaseCache();
            var callApiTask = _notionDataParser.CallApiForSearch();

            await Task.WhenAll(projectsTask, databaseIdTask, callApiTask);

            ProjectsId = await projectsTask;
            databaseId = await databaseIdTask;
        }



        bool Escaped(string FullString, string Prefix)
        {
            string EscapePattern = $@".*(?<!\\){Prefix}.*";
            Match EscapeMatch = Regex.Match(FullString, EscapePattern, RegexOptions.IgnoreCase);
            return !EscapeMatch.Success;
        }

        string RefineQueryText(string QueryText, string WantedToRefine, Dictionary<string, object> filteredQuery = null)
        {
            string RefineTitle = WantedToRefine;
            RefineTitle = RefineTitle.Replace("\\!", "!");
            RefineTitle = RefineTitle.Replace("\\@", "@");
            RefineTitle = RefineTitle.Replace("\\#", "#");
            RefineTitle = RefineTitle.Replace("\\[", "[");
            RefineTitle = RefineTitle.Replace("\\]", "]");
            if (string.IsNullOrEmpty(QueryText))
            {
                foreach (var filter in _settings.Filters)
                {
                    if (QueryText.StartsWith("\\" + filter.Title, StringComparison.CurrentCultureIgnoreCase) &&
                        filter.JsonType == JsonType.Filter && filter.Enabled)
                    {
                        RefineTitle = RefineTitle.Split("\\", 2)[^1];
                    }
                }
            }
            if (filteredQuery != null && filteredQuery.TryGetValue("TimeText", out object TimeText))
            {
                RefineTitle = RefineTitle.Replace($"\\{TimeText}", TimeText.ToString(), StringComparison.CurrentCultureIgnoreCase);
            }
            RefineTitle = RefineTitle.Replace($"\\", "", StringComparison.CurrentCultureIgnoreCase);

            return RefineTitle;
        }

        string BuildPathChain(string pageId)
        {
            string Chain = string.Empty;

            if (!string.IsNullOrEmpty(searchResults[pageId][2].GetString())) // Database
                Chain = searchResults[pageId][2].GetString();


            if (_settings.RelationSubtitle && !string.IsNullOrEmpty(searchResults[pageId][1].GetString())) // project
                Chain = (string.IsNullOrEmpty(Chain) ? "" : Chain + " / ") + searchResults[pageId][1].GetString();

            if (!string.IsNullOrEmpty(Chain))
                return Chain;

            while (!string.IsNullOrEmpty(searchResults[pageId][4].GetString()))
            {
                Chain = (string.IsNullOrEmpty(Chain) ? "" : Chain + " / ") + searchResults[searchResults[pageId][4].GetString()][0].GetString();
                pageId = searchResults[pageId][4].GetString();
            }

            if (!string.IsNullOrEmpty(searchResults[pageId][1].GetString())) // Is this another page has a relation property?
                Chain = searchResults[pageId][1].GetString() + (string.IsNullOrEmpty(Chain) ? "" : " / " + Chain);
            if (!string.IsNullOrEmpty(searchResults[pageId][2].GetString())) // Is this another page has a database?
                Chain = searchResults[pageId][2].GetString() + (string.IsNullOrEmpty(Chain) ? "" : " / " + Chain);

            return Chain;
        }

        Dictionary<string, object> GetData(string inputString, string defaultDB = "", bool TimeSkip = false, bool ManualTagsRunning = false, bool ManualDBRunning = false, bool ManualProjectRunning = false)
        {
            Dictionary<string, object> dataDict = new Dictionary<string, object>();

            if (!TimeSkip)
            {
                ModelResult modelResult;
                inputString = TextToDate(out modelResult, inputString);
                if (modelResult != null)
                {
                    dataDict["parsedDate"] = GetDateFromMethodResult(modelResult.Resolution["values"] as List<Dictionary<string, string>>);
                    dataDict["Time"] = HumanizedDate(dataDict["parsedDate"] as string);
                    dataDict["TimeText"] = modelResult.Text;
                    dataDict["Start"] = modelResult.Start;
                    dataDict["End"] = modelResult.End;
                }
            }

            string pattern = @"(\$[a-zA-Z\s\.\-\#\|\(\)ا-ي]*\$)|(@\s?[a-zA-Z0-9]*)|(!\s?[a-zA-Z0:9\._-]*)|((?:\*|\^)+\s?[\\""\{\}\<\>\!\[\]\@\`\(\)\#\%\+\-\,\?=/\\\da-zA-Z\s\'_.ا-ي\,\&\;\:]*)|(\[\s?[/\#\-\:a-zA-Z0-9/.&=_?]*]?)|\s?([^*^$\[\]]+)";
            var match = Regex.Matches(inputString, pattern);
            var dataList = match.Cast<Match>().SelectMany(m => m.Groups.Cast<Group>().Skip(1)).Select(g => g.Value.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            bool autoSelect = false;
            foreach (var type in dataList)
            {
                if (type.StartsWith("$") && type.EndsWith("$"))
                {
                    dataDict["filter"] = type.Trim();
                    break;
                }
                if (autoSelect)
                {
                    // if (type.StartsWith("!") && !string.IsNullOrEmpty(ProjectName))
                    // {
                    //     var splitQuery = type.Split('!', 2);
                    //     var userInput = splitQuery[1].Trim();
                    //     if (splitQuery.Length == 2)
                    //     {
                    //         var filteredItems = ProjectsId.Values.Where(item => item[0].GetString().ToLower().Contains(userInput.ToLower())).ToList();
                    //         if (filteredItems.Count == 1)
                    //         {
                    //             dataDict["Project"] = string.Join("", filteredItems);
                    //         }
                    //     }
                    // }
                    // if (type.StartsWith("@"))
                    // {
                    //     var splitQuery = type.Split('@', 2);
                    //     var userInput = splitQuery[1].Trim();
                    //     if (splitQuery.Length == 2)
                    //     {
                    //         var filteredItems = databaseId.Keys.Where(item => item.ToLower().Contains(userInput.ToLower())).ToList();
                    //         if (filteredItems.Count == 1)
                    //         {
                    //             dataDict["databaseId"] = string.Join("", filteredItems);
                    //         }
                    //     }
                    // }
                }

                // Condition for 'in '
                if (type.StartsWith("in ", ignoreCase: true, culture: null))
                {
                    dataDict["Name_dir"] = type;
                }

                // Condition for 'on '
                if (type.StartsWith("on ", ignoreCase: true, culture: null))
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

                if (!(type.StartsWith("#") || type.StartsWith("*") || type.StartsWith("^") || type.StartsWith("[") || (type.StartsWith("$") && type.EndsWith("$"))))
                {
                    if (!type.Contains($"$ {type}") && !type.Contains($"{type}$"))
                    {
                        dataDict["Name"] = RefineQueryText(inputString, type).Trim();
                    }
                }

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
            if (!ManualProjectRunning)
            {
                if (!dataDict.ContainsKey("Project") && inputString.Contains("!") && !string.IsNullOrEmpty(ProjectName))
                {
                    // string ProjectPattern = @"(!\s?.+)";
                    string ProjectPattern = @"((?<!\\)![^\\]*)";
                    var ProjectMatch = Regex.Match(inputString, ProjectPattern);
                    if (ProjectMatch.Success)
                    {
                        var splitQuery = ProjectMatch.Groups[0].Value.Split('!');
                        var userInput = splitQuery[1].Trim();
                        foreach (var _values in ProjectsId.Values)
                        {
                            string item = _values[0].GetString();
                            if (Context.API.FuzzySearch(item, userInput).Score > 0)
                            {
                                dataDict["Project"] = item;
                                string TransformName = Regex.Replace(userInput, item, "", RegexOptions.IgnoreCase).Trim();
                                string unRawInputString = Regex.Replace(inputString.Trim(), $@"\s?!\s?{item}", "", RegexOptions.IgnoreCase).Trim();
                                if (GetData(unRawInputString, defaultDB: _settings.DefaultDatabase, TimeSkip: true, ManualProjectRunning: true).TryGetValue("Name", out object Name))
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
                    else
                    {

                    }
                }
            }
            if (!ManualDBRunning)
            {
                if (!dataDict.ContainsKey("databaseId") && inputString.Contains("@"))
                {
                    // string Pattern = @"(@\s?.+)";
                    string Pattern = @"((?<!\\)@[^\\]*)";
                    var DatabaseMatch = Regex.Match(inputString, Pattern);
                    if (DatabaseMatch.Success)
                    {
                        var splitQuery = DatabaseMatch.Groups[0].Value.Split('@');
                        var userInput = splitQuery[1].Trim();
                        foreach (var item in databaseId.Keys)
                        {
                            if (Context.API.FuzzySearch(item, userInput).Score > 0)
                            {
                                dataDict["databaseId"] = item;
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
                }
            }

            if (!dataDict.ContainsKey("databaseId") && !string.IsNullOrEmpty(defaultDB))
            {
                dataDict["databaseId"] = defaultDB;
                dataDict["IsDefaultDB"] = true;
            }
            else
            {
                dataDict["IsDefaultDB"] = false;
            }

            if (!ManualTagsRunning)
            {
                if (inputString.Contains("#") && !string.IsNullOrEmpty(TagName))
                {
                    string TagsPattern = @"((?<!\\)#[^\\#]*)";
                    var ExtractedTags = Regex.Matches(inputString, TagsPattern);
                    List<string> selectedTags = new List<string>();

                    foreach (Match _tag in ExtractedTags)
                    {
                        try
                        {
                            var splitQuery = _tag.Value.Split('#');
                            var databaseElement = databaseId[dataDict["databaseId"].ToString()];
                            if (splitQuery.Length == 2)
                            {
                                var userInput = splitQuery[^1].Trim();
                                var tagsArray = databaseElement.GetProperty("multi_select").GetProperty(TagName);
                                var availableTags = tagsArray.EnumerateArray().Select(item => item.GetString()).ToList();
                                foreach (var _availableTag in availableTags)
                                {
                                    if (Context.API.FuzzySearch(_availableTag, userInput).Score > 0)
                                    {
                                        selectedTags.Add(_availableTag);
                                    }
                                }

                                if (selectedTags.Count != 0)
                                {
                                    dataDict["tags"] = selectedTags;
                                    if (GetData(Regex.Replace(inputString.Trim(), $@"\s?(?<!\\)#\s?({string.Join("|", selectedTags)})", "", RegexOptions.IgnoreCase).Trim(), defaultDB: _settings.DefaultDatabase, TimeSkip: true, ManualTagsRunning: true).TryGetValue("Name", out object Name))
                                    {
                                        dataDict["Name"] = Name.ToString();
                                    }

                                }
                            }
                        }
                        catch
                        {

                        }
                    }
                }
            }
            return dataDict;
        }

        string GetDateFromMethodResult(List<Dictionary<string, string>> ResoultionValues)
        {
            string parsedDate = Convert.ToString(ResoultionValues.Last()["value"]);
            if (string.IsNullOrEmpty(parsedDate))
            {
                parsedDate = Convert.ToString(ResoultionValues.Last()["start"]);
            }

            return parsedDate;
        }

        string HumanizedDate(string parsedDate)
        {
            DateTime parsedclock = Convert.ToDateTime(parsedDate);
            string HumanizedDate = parsedclock.ToString("ddd", CultureInfo.InvariantCulture);
            if (parsedclock.TimeOfDay != TimeSpan.Zero)
            {
                HumanizedDate = $"{HumanizedDate} | {parsedclock.ToString("h:mm tt", CultureInfo.InvariantCulture)}";
            }
            else
            {
                string daySuffix = GetDaySuffix(parsedclock.Day);
                HumanizedDate = $"{HumanizedDate} | {parsedclock.Day + daySuffix} {parsedclock.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
            }

            return HumanizedDate;
        }

        public string TextToDate(out ModelResult methodResult, string input = "")
        {
            methodResult = null;
            var results = DateTimeRecognizer.RecognizeDateTime(input, "en-us");
            bool IsItFilter = false;
            foreach (CustomPayload filter in _settings.Filters)
            {
                if (filter.Enabled && filter.JsonType == JsonType.Filter && input.StartsWith(filter.Title, StringComparison.OrdinalIgnoreCase))
                {
                    IsItFilter = true;
                }
            }

            string returnedName = input;
            if (results.Any())
            {
                foreach (ModelResult result in results)
                {
                    if (!IsItFilter &&
                    (input.StartsWith(result.Text,StringComparison.OrdinalIgnoreCase) ||
                    input.Substring(result.Start - 1, 1) != "\\") &&
                    !(input.Substring(0, result.Start).Contains("*") || input.Substring(0, result.Start).Contains("^")))
                    {
                        methodResult = result;
                        if (!input.StartsWith(result.Text,StringComparison.OrdinalIgnoreCase) &&
                            (input.Substring(result.Start - 3, 3) == "on " || input.Substring(result.Start - 3, 3) == "On "))
                        {
                            returnedName = returnedName.Remove(result.Start -3, result.End - result.Start + 4);

                        }
                        else
                        {
                            returnedName = returnedName.Remove(result.Start, result.End - result.Start + 1);
                        }
                        returnedName = returnedName.Replace("  ", " ");
                        return returnedName;
                    }
                }
            }
            return returnedName;
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

        (Dictionary<string, Dictionary<string, object>>, Dictionary<string, List<Dictionary<string, object>>>, string) FormatData(Dictionary<string, object> filtered_data_arg, string Mode = "Create", string DbNameInCache = "")
        {
            string DATABASE_ID = null;
            Dictionary<string, List<Dictionary<string, object>>> children = new Dictionary<string, List<Dictionary<string, object>>> {
                { "children", new List<Dictionary<string, object>>() }};

            dataDict = filtered_data_arg;

            if (dataDict.ContainsKey("databaseId") && Mode != "Edit")
            {
                DATABASE_ID = Convert.ToString(databaseId[dataDict["databaseId"].ToString()].GetProperty("id").ToString());
            }
            else
            {
                if (DbNameInCache != null)
                {
                    dataDict["databaseId"] = DbNameInCache;
                    DATABASE_ID = DbNameInCache;
                }
                else
                {
                    DATABASE_ID = null;
                }
            }

            var data = new Dictionary<string, Dictionary<string, object>> { };

            if (!string.IsNullOrWhiteSpace(dataDict["Name"].ToString()))
            {
                string titleMap;
                if (!string.IsNullOrEmpty(DATABASE_ID))
                {
                    titleMap = $"{databaseId[dataDict["databaseId"].ToString()].GetProperty("title").ToString()}";
                }
                else
                {
                    titleMap = "title";
                }
                data[titleMap] = new Dictionary<string, object>
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
            if (!string.IsNullOrEmpty(DATABASE_ID))
            {

                if (dataDict.ContainsKey("parsedDate") && databaseId[dataDict["databaseId"].ToString()].GetProperty("date").GetArrayLength() > 0)
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
                    var tag_options = new List<Dictionary<string, string>> { };
                    foreach (var tag in dataDict["tags"] as List<string>)
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
                    data[UrlMap] = new Dictionary<string, object> { { "url", dataDict["link"] } };
                }

                if (dataDict.ContainsKey("Project"))
                {
                    // var ProjectRelationID = ProjectsId[Convert.ToString(dataDict["Project"]).Trim()][3];
                    var ProjectRelationID = ProjectsId.FirstOrDefault(_project => _project.Value[0].GetString() == Convert.ToString(dataDict["Project"])).Key;
                    if (!data.ContainsKey("Project"))
                    {
                        data.Add(ProjectName, new Dictionary<string, object>());
                    }
                    data[ProjectName].Add("relation", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id", ProjectRelationID } } });
                }
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
                }
                return (data, children, DATABASE_ID);
            }
            else
            {
                children = null;
                return (data, children, DATABASE_ID);
            }
        }

        public async Task<HttpResponseMessage> CreatePage(Dictionary<string, object> Datadict, Dictionary<string, List<Dictionary<string, object>>> children = null, string DatabaseId = null, bool open = false)
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

                        string DBName = databaseId.FirstOrDefault(Database => Database.Key == Datadict["databaseId"].ToString()).Key;
                        string projectName = dataDict.ContainsKey("Project") ? $"<{dataDict["Project"]}>" : string.Empty;
                        string itemName = dataDict.ContainsKey("Name") ? dataDict["Name"].ToString() : string.Empty;



                        JsonArray jsonArray = new JsonArray
                        {
                            itemName,
                            dataDict.ContainsKey("Project") ? dataDict["Project"] : string.Empty ,
                            DBName,
                            "Images\\app.png"
                        };

                        searchResults[jsonObject["id"].ToString()] = JsonDocument.Parse(jsonArray.ToString()).RootElement;

                        string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        File.WriteAllText(_settings.FullCachePath, jsonString);

                        if (open)
                        {
                            Context.API.ShowMsg($"Opening the new item ({Datadict["databaseId"]})",
                                            $"{itemName}\n{projectName}",
                                            iconPath: Context.CurrentPluginMetadata.IcoPath);
                            string created_PageID = jsonObject["id"].ToString().Replace("-", "");
                            string notionUrl = $"notion://www.notion.so/{created_PageID}";
                            OpenNotionPage(notionUrl);
                        }
                        else
                        {
                            Context.API.ShowMsg($"Adding a new item ({Datadict["databaseId"]})",
                                            $"{itemName}\n{projectName}",
                                            iconPath: Context.CurrentPluginMetadata.IcoPath);
                        }
                        return response;
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Error: {response.StatusCode}", response.ReasonPhrase);
                        return response;
                    }
                }
            }
            catch
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                    HttpResponseMessage FakeRespone = new HttpResponseMessage();
                    FakeRespone.ReasonPhrase = "Bad Request";
                    return FakeRespone;
                }
                else
                {
                    if (_settings.FailedRequests)
                    {
                        await _apiCacheManager.CacheFunction(nameof(CreatePage), new List<object> { Datadict, null, DatabaseId, open });
                        Context.API.ShowMsgError($"Internet Connection Error", "The request has been saved by the cache manager and will be processed once an internet connection is available.");
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Internet Connection Error", "Please check your internet connection.");
                    }
                }
                return null;
            }
        }

        static bool IsInternetConnected()
        {
            try
            {
                Ping ping = new Ping();
                PingReply reply = ping.Send("8.8.8.8", 500);

                return (reply != null && reply.Status == IPStatus.Success);
            }
            catch (PingException)
            {
                return false;
            }
        }

        async Task<HttpResponseMessage> EditPageMainProperty(bool open, string pageId, Dictionary<string, object> filteredQueryEditing, List<string> fromContext = null)
        {
            try
            {
                var (data, children, DatabaseId) = FormatData(filteredQueryEditing, Mode: "Edit", DbNameInCache: searchResults[pageId][2].GetString());
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
                            }
                            catch
                            {

                            }
                        }

                        if (data.Count != 0)
                        {
                            var jsonArray = searchResults[pageId].EnumerateArray().ToList();
                            if (filteredQueryEditing.ContainsKey("Name") && !string.IsNullOrEmpty(filteredQueryEditing["Name"].ToString()))
                            {
                                jsonArray[0] = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(filteredQueryEditing["Name"])).RootElement; ;
                            }
                            if (filteredQueryEditing.TryGetValue("Project", out var Project) && !string.IsNullOrEmpty(Project.ToString()))
                            {
                                jsonArray[1] = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(Project)).RootElement; ;
                            }

                            var newArray = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(jsonArray)).RootElement;
                            searchResults[pageId] = newArray;

                            string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                            File.WriteAllText(_settings.FullCachePath, jsonString);
                        }

                        // _ = Task.Run(async () =>
                        // {
                        //     await this._notionDataParser.GetStartCursour(delay: 14000, manuanl_cursour: pageId);
                        // });
                        return response;
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Error: {response.StatusCode}", response.ReasonPhrase);
                        return response;
                    }
                }
            }
            catch
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                    HttpResponseMessage FakeRespone = new HttpResponseMessage();
                    FakeRespone.ReasonPhrase = "Bad Request";
                    return FakeRespone;
                }
                else
                {
                    if (_settings.FailedRequests)
                    {
                        await _apiCacheManager.CacheFunction(nameof(EditPageMainProperty), new List<object> { open, pageId, filteredQueryEditing, fromContext });
                        Context.API.ShowMsgError($"Internet Connection Error", "The request has been saved by the cache manager and will be processed once an internet connection is available.");
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Internet Connection Error", "Please check your internet connection.");
                    }
                }
                return null;
            }


        }


        public async Task<HttpResponseMessage> EditPropertyFromContext(string PageId, string payload, List<string> fromContext = null)
        {
            HttpResponseMessage response = new HttpResponseMessage();
            try
            {
                payload = _notionDataParser.ConvertVariables(payload);

                using (HttpClient client = new HttpClient())
                {
                    StringContent Payload = new StringContent(JsonConvert.SerializeObject(JsonConvert.DeserializeObject<dynamic>(payload)), Encoding.UTF8, "application/json");
                    string delete_url = $"https://api.notion.com/v1/pages/{PageId}";
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");
                    response = await client.PatchAsync(delete_url, Payload);
                    return response;
                }
            }
            catch (Newtonsoft.Json.JsonReaderException)
            {
                response.ReasonPhrase = "Bad Request";
                return response;
            }
            catch
            {
                if (!IsInternetConnected())
                {
                    if (_settings.FailedRequests)
                    {
                        await _apiCacheManager.CacheFunction(nameof(EditPropertyFromContext), new List<object> { PageId, payload, fromContext });
                        Context.API.ShowMsgError($"Internet Connection Error", "The request has been saved by the cache manager and will be processed once an internet connection is available.");
                    }
                    else
                    {
                        Context.API.ShowMsgError($"Internet Connection Error", "Please check your internet connection.");

                    }
                }
                return response;
            }
        }


        private async Task RetryCachedFunctions()
        {
            List<CachedFunction> cachedFunctionsCopy = _apiCacheManager.cachedFunctions.ToList();

            foreach (var cachedFunction in cachedFunctionsCopy)
            {
                var methodInfo = typeof(Main).GetMethod(cachedFunction.FunctionName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (methodInfo != null)
                {
                    try
                    {
                        var convertedArguments = cachedFunction.Arguments.Select(arg => ConvertToExpectedType(arg)).ToList();
                        var task = (Task<HttpResponseMessage>)methodInfo.Invoke(this, convertedArguments.ToArray());
                        var response = await task;


                        if (response.IsSuccessStatusCode || response.ReasonPhrase == "Bad Request")
                        {
                            _apiCacheManager.cachedFunctions.Remove(cachedFunction);
                        }

                    }
                    catch (Exception ex)
                    {
                        Context.API.LogException("Main", "An error at retry on failed api requests", ex, "RetryCachedFunctions");
                    }
                }
            }
            _apiCacheManager.SaveCacheToFile();

        }

        private object ConvertToExpectedType(object arg)
        {
            if (arg is JObject jObject)
            {
                return jObject.ToObject<Dictionary<string, object>>();
            }

            return arg;
        }
    }




    public class CachedFunction
    {
        public string FunctionName { get; set; }
        public List<object> Arguments { get; set; }
    }

    public class ApiCacheManager
    {
        public List<CachedFunction> cachedFunctions;
        private PluginInitContext _context;

        public ApiCacheManager(PluginInitContext context)
        {
            this._context = context;
            InitializeCachedFunctions();
        }
        async Task InitializeCachedFunctions()
        {
            cachedFunctions = await GetCachedFunctions();
        }
        public async Task CacheFunction(string functionName, List<object> arguments)
        {
            var existingFunction = cachedFunctions.FirstOrDefault(f => f.FunctionName == functionName && ArgumentsMatch(f.Arguments, arguments));

            if (existingFunction == null)
            {
                cachedFunctions.Add(new CachedFunction
                {
                    FunctionName = functionName,
                    Arguments = arguments
                });

                await SaveCacheToFile();
            }
        }
        private bool ArgumentsMatch(List<object> args1, List<object> args2)
        {
            if (args1.Count != args2.Count)
            {
                return false;
            }

            for (int i = 0; i < args1.Count; i++)
            {
                if (!args1[i].Equals(args2[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public async Task SaveCacheToFile()
        {
            string cacheJson = JsonConvert.SerializeObject(new { CachedFunctions = cachedFunctions }, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "FailedRequests.json"), cacheJson);
        }
        public async Task<List<CachedFunction>> GetCachedFunctions()
        {
            try
            {
                string cacheJson = await File.ReadAllTextAsync(Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "FailedRequests.json"));
                var cachedData = JsonConvert.DeserializeObject<dynamic>(cacheJson);
                return JsonConvert.DeserializeObject<List<CachedFunction>>(cachedData.CachedFunctions.ToString());
            }
            catch
            {
                return new List<CachedFunction>();
            }
        }
    }
}