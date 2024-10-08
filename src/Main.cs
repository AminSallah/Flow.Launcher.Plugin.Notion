﻿using System.Windows.Controls;
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
using System.Windows.Documents;
using System.Windows.Data;
using System.Windows;

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
        public static string cacheDirectory;
        public static string icons;
        public static string HiddenItemsPath;
        public static string ImagesPath;
        private PluginInitContext Context;
        internal NotionBlockTypes? _notionBlockTypes;
        internal NotionDataParser? _notionDataParser;
        private static SettingsViewModel? _viewModel;
        private Settings? _settings;
        internal static string CustomImagesDirectory;
        private bool RequestNewCache = false;
        private bool ShowTags = false;
        private bool CachedPoolEmpty = true;
        private bool pluginInit = false;
        private bool BatchDeleting = false;
        public static List<string> HiddenItems = new List<string>();
        public static Dictionary<string, JsonElement> databaseId = LoadJsonData(DatabaseCachePath);
        public static Dictionary<string, JsonElement> ProjectsId = LoadJsonData(RelationCachePath);
        public static Dictionary<string, JsonElement> searchResults = LoadJsonData(FullCachePath);

        public async Task InitAsync(PluginInitContext context)
        {
            Context = context;
            ImagesPath = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Images");
            this._settings = context.API.LoadSettingJsonStorage<Settings>();
            cacheDirectory = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "cache");
            icons = Path.Combine(context.CurrentPluginMetadata.PluginDirectory, "Icons", "icons");
            DatabaseCachePath = System.IO.Path.Combine(cacheDirectory, "database.json");
            _settings.DatabaseCachePath = DatabaseCachePath;
            HiddenItemsPath = System.IO.Path.Combine(cacheDirectory, "HiddenItems.txt");
            FullCachePath = System.IO.Path.Combine(cacheDirectory, "search.json");
            _settings.FullCachePath = FullCachePath;
            CustomImagesDirectory = System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Icons", "CustomIcons");
            CreatePathsIfNeeded();

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

                        if (_settings.RelationDatabasesIds.Count != 0)
                        {
                            foreach (string databaseId in _settings.RelationDatabasesIds)
                            {
                                _ = Task.Run(async () =>
                                {
                                    await this._notionDataParser.QueryDB(databaseId, null, Path.Combine(cacheDirectory, $"{databaseId}.json"));
                                });
                            }
                            if (_settings.RelationDatabasesIds.Count > 0)
                            {
                                ProjectsId = LoadJsonData(Path.Combine(cacheDirectory, $"{_settings.RelationDatabasesIds[0]}.json"));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    databaseId = LoadJsonData(DatabaseCachePath);
                    if (_settings.RelationDatabasesIds.Count > 0)
                    {
                        ProjectsId = LoadJsonData(Path.Combine(cacheDirectory, $"{_settings.RelationDatabasesIds[0]}.json"));
                    }
                    Context.API.LogException(nameof(Main), "An error occurred while build database and relaiton cache", ex);
                }
            }
            else
            {
                databaseId = LoadJsonData(DatabaseCachePath);
                if (_settings.RelationDatabasesIds.Count > 0)
                {
                    ProjectsId = LoadJsonData(Path.Combine(cacheDirectory, $"{_settings.RelationDatabasesIds[0]}.json"));
                }
                Context.API.LogWarn(nameof(Main), "No internet Connection for Init cache using last cached data", MethodBase.GetCurrentMethod().Name);
            }

            if (databaseId.Count == 0)
                RequestNewCache = true;

            Main._viewModel = new SettingsViewModel(this._settings);

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

        void CreatePathsIfNeeded()
        {
            DirExist(cacheDirectory);
            DirExist(icons);
            PathExist(_settings.DatabaseCachePath);
            PathExist(HiddenItemsPath);
            PathExist(_settings.FullCachePath);
            DirExist(CustomImagesDirectory);
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

            if (dict.ContainsKey("RefreshId"))
            {
                var databaseName = dict["DatabaseTitle"].ToString();
                var RefreshRequest = new Result
                {
                    Title = $"Refresh ({databaseName}) items",
                    Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ue895"),
                    Action = c =>
                    {
                        if (IsInternetConnected())
                        {
                            Context.API.ShowMsg("Relation Database", $"Please wait while refresh ({databaseName}) cache.");
                            _ = Task.Run(async () =>
                            {
                                var targetDatabaseId = dict["RefreshId"].ToString();
                                await this._notionDataParser.QueryDB(targetDatabaseId, null, Path.Combine(cacheDirectory, $"{targetDatabaseId}.json"));
                                Context.API.ShowMsg("Relation Database", $"{databaseName} items successfully updated.");
                            });
                        }
                        else
                        {
                            Context.API.ShowMsgError("Internet Connection Error", $"Please check your internet connection.");
                        }

                        return false;
                    },
                };
                resultlist.Add(RefreshRequest);
                return resultlist;

            }
            if (dict.ContainsKey("CustomPayload"))
            {
                if (dict["CustomPayload"] is CustomPayload filter)
                {
                    resultlist.Add(new Result
                    {
                        Title = $"Disable ({filter.Title}) filter.",
                        Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe7e8"),
                        Action = c =>
                        {
                            filter.Enabled = false;
                            return true;
                        }
                    });


                    resultlist.Add(new Result
                    {
                        Title = $"Set " + filter.Title + " as Primary Search Anchor.",
                        Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe773"),
                        Action = c =>
                        {
                            _settings.SearchBase = filter.Title;
                            return true;
                        }
                    });
                }
                else
                {
                    resultlist.Add(new Result
                    {
                        Title = $"Set " + "All pages" + " as Primary Search Anchor.",
                        Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\xe773"),
                        Action = c =>
                        {
                            _settings.SearchBase = "All pages";
                            return true;
                        }
                    });
                }
                return resultlist;
            }

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
                                            string databaseIdResponse = string.Empty;
                                            foreach (var kvp in properties)
                                            {
                                                var values = (JObject)kvp.Value;
                                                if (values["type"].ToString() == "title")
                                                {
                                                    title = _notionDataParser.GetFullTitle(properties[kvp.Key]["title"]);
                                                    break;
                                                }
                                            }
                                            try
                                            {
                                                databaseIdResponse = EditedObject["parent"]["database_id"].ToString();
                                            }
                                            catch { }

                                            if (Convert.ToBoolean(EditedObject["archived"]))
                                            {
                                                Context.API.ShowMsg("Page Deletion", $"{title} has been deleted", iconPath: Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "Images", "item_delete.png"));
                                                searchResults.Remove(dict["PageId"].ToString());
                                                string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions { WriteIndented = true });
                                                File.WriteAllText(_settings.FullCachePath, jsonString);

                                                if (_settings.RelationDatabasesIds.Contains(databaseIdResponse))
                                                {
                                                    string targetRelationDatabasePath = Path.Combine(cacheDirectory, databaseIdResponse + ".json");
                                                    ProjectsId = LoadJsonData(targetRelationDatabasePath);
                                                    ProjectsId.Remove(dict["PageId"].ToString());
                                                    string newProjectsIdJson = System.Text.Json.JsonSerializer.Serialize(ProjectsId, new JsonSerializerOptions { WriteIndented = true });
                                                    File.WriteAllText(targetRelationDatabasePath, newProjectsIdJson);
                                                }
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

                        async void BatchDelete(List<string> CurrentQueryItems)
                        {
                            BatchDeleting = true;
                            searchResults = LoadJsonData(FullCachePath);
                            int Success = CurrentQueryItems.Count;
                            foreach (string Id in CurrentQueryItems)
                            {
                                var response = await EditPropertyFromContext(PageId: Id, payload: """{"archived" : true}""");
                                if (!response.IsSuccessStatusCode)
                                {
                                    Success--;
                                }
                                else
                                {
                                    searchResults.Remove(Id);
                                }

                                string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions { WriteIndented = true });
                                File.WriteAllText(_settings.FullCachePath, jsonString);

                                BatchDeleting = false;

                            }
                            if (Success > 0)
                                Context.API.ShowMsg("Batch Page Deletion", Success.ToString() + (Success > 1 ? " Pages have " : " Page has ") + "been deleted successfully");
                        }

                        var DeleteAll = new Result
                        {
                            Title = $"Delete All Current Query ({CurrentQueryItems.Count})",
                            IcoPath = "Images//database_default.png",
                            Action = c =>
                            {
                                var result = System.Windows.MessageBox.Show(
                                    "Are you sure you want to delete " + CurrentQueryItems.Count + (CurrentQueryItems.Count > 1 ? " pages?" : " page?"),
                                    "Batch pages deletion",
                                    MessageBoxButton.YesNo, MessageBoxImage.Warning);
                                if (result == MessageBoxResult.Yes)
                                {
                                    Task.Run(async delegate
                                    {
                                        BatchDelete(CurrentQueryItems);
                                    });
                                }

                                return true;

                            },
                        };
                        resultlist.Add(DeleteAll);
                    }
                }
            }


            return resultlist;
        }

        Dictionary<string, Task> BGTasks = new Dictionary<string, Task>();
        public async void OnVisibilityChanged(object _, VisibilityChangedEventArgs e)
        {
            if (!BatchDeleting)
            {
                if (!e.IsVisible)
                {
                    pluginInit = false;
                }
                if (e.IsVisible)
                {
                    if (_settings.FailedRequests && IsInternetConnected())
                    {
                        if (searchResults.Count() == 0)
                        {
                            searchResults = LoadJsonData(FullCachePath);
                        }
                        if (CachedPoolEmpty) {
                            _ = Task.Run(async () =>
                            {
                                CachedPoolEmpty = false;
                                await RetryCachedFunctions();
                                CachedPoolEmpty = true;
                            });
                        }
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
                                await this._notionDataParser.QueryDB(filePath: System.IO.Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{path.Title}.json"), DB: databaseId[path.Databases[0]].GetProperty("id").ToString(), filterPayload: path.Json, itemSubtitle: path.ItemSubTitle, propNames: path.PropertiesNames);
                                if (pluginInit)
                                {
                                    Context.API.ReQuery(false);
                                }
                            });
                        }
                    }
                }
            }
        }


        // string TagName = string.Empty;          // Used to store (map) Multiselect Property Name.
        // bool MultiSelectionType = true;          // Used to select selection type.
        // PropertySelectionType SelectionType = PropertySelectionType.MultiSelectionType;          // Used to select selection type.

        Dictionary<int, string> SelectionNameMap = new Dictionary<int, string>();          // Used to store (map) Multiselect Property Name.
        Dictionary<int, PropertySelectionType> SelectionTypeMap = new Dictionary<int, PropertySelectionType>();          // Used to store (map) Multiselect Property Name.
        Dictionary<int, string> ProjectName = new Dictionary<int, string>();      // Used to store (map) Relation Property Name.
        string DateName = string.Empty;         // Used to store (map) Date Property Name.
        bool timeForce = false;                 // Used to Check whether database changed after choose date property or not.
        string UrlMap = string.Empty;           // Used to store (map) Url Property Name.

        public static JToken currPageProperties = null;

        string GetRelationId(string ProjectName, string KeyForId)
        {
            try
            {
                string relationId = databaseId[KeyForId].GetProperty("relation").GetProperty(ProjectName).GetString();
                return relationId;
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }
        string GetRelationFilePath(string ProjectName, string KeyForId)
        {
            try
            {
                string relationPath = databaseId[KeyForId].GetProperty("relation").GetProperty(ProjectName).GetString();
                relationPath = Path.Combine(cacheDirectory, relationPath + ".json");
                return relationPath;
            }
            catch (KeyNotFoundException)
            {
                return null;
            }
        }

        public string PluginActionKeyword()
        {
            string currKeyword = Context.CurrentPluginMetadata.ActionKeyword;
            if (currKeyword == "*")
            {
                return String.Empty;
            }
            return currKeyword + " ";
        }
        public async Task<List<Result>> QueryAsync(Query query, CancellationToken token)
        {
            List<Result> resultList = new List<Result>();
            if (BatchDeleting)
            {
                resultList.Add(new Result
                {
                    Title = "Plugin is deleting some pages",
                    SubTitle = "Please wait while the plugin finishes deleting pages",
                    Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ue946"),

                });
                return resultList;
            }
            if (!pluginInit)
            {
                pluginInit = true;
            }
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


            string editingPatternId = @"([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})";

            Match editingPatternIdMatch = Regex.Match(query.Search, editingPatternId);
            bool editingMode;
            Dictionary<string, object> filteredQuery;
            if (editingPatternIdMatch.Success)
            {
                filteredQuery = GetData(query.Search.Replace($"${editingPatternIdMatch.Groups[1].Value}$", ""), defaultDB: searchResults[editingPatternIdMatch.Groups[1].Value][2].GetString());
                editingMode = true;
            }
            else
            {
                filteredQuery = GetData(query.Search, defaultDB: _settings.DefaultDatabase);
                editingMode = false;
            }

            if (filteredQuery.ContainsKey("link") && filteredQuery["link"] is string _link && _link.Contains("#") && !_link.Contains("\\#"))
            {
                
                Context.API.ChangeQuery(query.RawQuery.Replace(_link, _link.Replace("#", "\\#")),true);
            }

            string link;

            if (!filteredQuery.ContainsKey("Name"))
            {
                filteredQuery["Name"] = string.Empty;
            }

            if (editingMode)
            {
                filteredQuery["Name"] = filteredQuery["Name"].ToString().Replace(editingPatternIdMatch.Groups[1].Value, "").Trim();
            }

            

            string DBSubtitle = string.Empty;
            string PSubtitle = string.Empty;

            if (filteredQuery.ContainsKey("Relations"))
            {
                Dictionary<int, String> Projects = filteredQuery["Relations"] as Dictionary<int, String>;
                DBSubtitle = filteredQuery.TryGetValue("databaseId", out object databaseName) ? (string)databaseName  : string.Empty;
                if (Projects.Count != 0)
                {
                    DBSubtitle = DBSubtitle + " / ";

                    if (Projects.Count == 1)
                    {
                        PSubtitle = Projects.First().Value;
                    }
                    else
                    {
                        PSubtitle = Projects.First().Value + " +" + (Projects.Count - 1).ToString();
                    }
                }
            }

            string tagSubtitle = string.Empty;
            if (filteredQuery.ContainsKey("tags"))
            {
                Dictionary<int, String> SelectedTags = filteredQuery["tags"] as Dictionary<int, String>;
                if (SelectedTags.Count == 1)
                {
                    tagSubtitle = $" [{SelectedTags[1]}]";
                }
                else if (SelectedTags.Count != 0)
                {

                    tagSubtitle = $" [{string.Join(", ", SelectedTags.Values)}]";
                }
            }

            var TimeValue = string.Empty;
            if (filteredQuery.ContainsKey("Time"))
            {
                TimeValue = $" ({filteredQuery["Time"]})";
            }

            if (filteredQuery.ContainsKey("link"))
            {
                filteredQuery["link"] = (filteredQuery["link"] as string).Replace("\\", "");
                link = $"{filteredQuery["link"]}";

                if (!IsWritingBlock && (
                    (!string.IsNullOrEmpty(TimeValue) || !string.IsNullOrEmpty(tagSubtitle) || !string.IsNullOrEmpty(PSubtitle))
                    || !editingMode)
                    )
                {
                    link = "\n" + link;
                } else if (IsWritingBlock) {
                    if ((!string.IsNullOrEmpty(TimeValue) || !string.IsNullOrEmpty(tagSubtitle) || !string.IsNullOrEmpty(PSubtitle)) || !editingMode)
                    {
                        link = " 🔗";
                    }
                   
                }
            }
            else
            {
                link = string.Empty;
            }


            Dictionary<string, List<string>> userInputSearch = new Dictionary<string, List<string>>();

            bool filterMode = false;
            bool nonDefaultBase = false;
            if (filteredQuery.ContainsKey("filter"))
            {
                string splitQueryFilter = query.Search.Replace(filteredQuery["filter"].ToString(), "").ToLower().Trim();
                filterMode = true;
                foreach (var key in searchResults.Keys)
                {
                    if ($"${searchResults[key][1]}$" == filteredQuery["filter"].ToString() ||
                            (databaseId.ContainsKey(filteredQuery["filter"].ToString().Replace("$", "")) &&
                                filteredQuery["filter"].ToString().Replace("$", "") == searchResults[key][2].ToString()))
                    {
                        if (Context.API.FuzzySearch(RefineQueryText(splitQueryFilter, splitQueryFilter, filteredQuery), Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(splitQueryFilter.Replace("\\", "")))
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
            else if (!filteredQuery.ContainsKey("filter"))
            {
                Dictionary<string, JsonElement> TargetBase;
                if (!query.Search.ToLower().StartsWith("all"))
                {
                    if (_settings.SearchBase != "All pages")
                    {
                        TargetBase = LoadJsonData(Path.Combine(cacheDirectory, _settings.SearchBase + ".json"));
                        nonDefaultBase = true;
                    }
                    else if (_settings.SearchBase == "Disabled")
                    {
                        TargetBase = new Dictionary<string, JsonElement>();
                    }
                    else
                    {
                        TargetBase = searchResults;
                    }

                }
                else
                {
                    TargetBase = searchResults;
                }
                foreach (var key in TargetBase.Keys)
                {
                    string QueryText = (query.Search.ToLower().StartsWith("all") && !nonDefaultBase && _settings.SearchBase != "All pages") ? query.Search.Substring(3).Trim() : query.Search.Trim();
                    if (Context.API.FuzzySearch(RefineQueryText(QueryText, QueryText, filteredQuery), Convert.ToString(TargetBase[key][0])).Score > 0 || string.IsNullOrEmpty(QueryText))

                    // if (Context.API.FuzzySearch(query.Search.Replace("\\", "").Trim(), Convert.ToString(searchResults[key][0])).Score > 0 || string.IsNullOrEmpty(query.Search.Replace("\\", "").Trim()))

                    {
                        userInputSearch[key] = new List<string>
                            {
                                $"notion://www.notion.so/{key.Replace("-","")}", // Url
                                TargetBase[key][0].GetString(), // Name
                                TargetBase[key][1].GetString(), // Project of the item
                                TargetBase[key][3].GetString(), // Icon_path
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
                        if (userInputSearch.ContainsKey(key))
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
                if (filter.JsonType == JsonType.Filter && filter.Enabled && filter.Title != _settings.SearchBase)
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

                        Dictionary<string, JsonElement> FilterDataForQuery = new Dictionary<string, JsonElement>();
                        if (FilterData.Count > 0)
                        {
                            foreach (var item in FilterData.Reverse())
                            {
                                if (Context.API.FuzzySearch(query.Search.Replace(filter.Title, string.Empty, StringComparison.CurrentCultureIgnoreCase).ToLower(),
                                    item.Value[0].GetString().ToLower()).Score > 0 ||
                                        string.IsNullOrEmpty(query.Search.Replace(filter.Title, string.Empty, StringComparison.CurrentCultureIgnoreCase)))
                                {
                                    FilterDataForQuery[item.Key] = item.Value;
                                }
                            }
                            foreach (var item in FilterDataForQuery)
                            {   
                                var result = new Result
                                {
                                    Title = $"{item.Value[0]}",
                                    SubTitle = $"{item.Value[1]}",
                                    AutoCompleteText = $"{PluginActionKeyword()}${item.Key}$ ",
                                    ContextData = new Dictionary<string, object>
                                        {
                                            {"Title", $"{item.Value[0]}" },
                                            { "PageId", $"{item.Key}" },
                                            { "Url", $"{item.Value[2]}" },
                                            { "DBName", $"{item.Value[5]}"},
                                            { "Project_name", $"{item.Value[4]}"},
                                            { "Tags", $"{item.Value[1]}" },
                                            { "CreateFirst", false},
                                            { "HideAll", FilterDataForQuery.Keys.ToList<string>()}
                                        },
                                    Action = c =>
                                    {
                                        OpenNotionPage(Convert.ToString(item.Value[2]));
                                        return true;
                                    },
                                    IcoPath = item.Value[3].ToString()
                                };
                                resultList.Add(result);

                            }

                            return resultList;
                        }
                    }
                    else if (filter.Title.ToLower().Contains(query.Search.ToLower()) || string.IsNullOrEmpty(query.Search))
                    {
                        string filterSubtitle = filter.SubTitle;
                        if ( filterSubtitle.Contains("{{") && filterSubtitle.Contains("}}"))
                        {
                            var startIndex = filterSubtitle.IndexOf("{{") + 2;
                            var endIndex = filterSubtitle.IndexOf("}}");
                            var dateString = filterSubtitle.Substring(startIndex, endIndex - startIndex);
                            
                            // Parse the extracted string into a DateTime object
                            DateTime dateTime = DateTime.ParseExact(dateString, "yyyy-M-d", CultureInfo.InvariantCulture);;
                            DateTime now = DateTime.Now;
                    
                            // Calculate the difference in days
                            TimeSpan difference = dateTime - now;
                            var daysBetween = difference.Days;
                            
                            // Replace the original curly bracket section with the days difference
                            filterSubtitle = filterSubtitle.Substring(0, startIndex - 2) + daysBetween + " days" + filterSubtitle.Substring(endIndex + 2);
                        }
                        resultList.Add(new Result
                        {
                            Title = filter.Title,
                            SubTitle = filterSubtitle + (filter.CacheType != CacheTypes.Disabled && filter.Count ? $" ({LoadJsonData(Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{filter.Title}.json")).Count})" : ""),
                            IcoPath = filter.IcoPath,
                            Score = 100,
                            AutoCompleteText = $"{PluginActionKeyword()}{filter.Title}",
                            ContextData = new Dictionary<string, object>
                            {
                                {"CustomPayload", filter }
                            },
                            Action = c =>
                            {
                                Context.API.ChangeQuery($"{PluginActionKeyword()}{filter.Title} ");
                                return false;
                            },
                        });

                        if (nonDefaultBase)
                        {
                            resultList.Add(new Result
                            {
                                Title = "All pages",
                                SubTitle = "Display all shared pages with integration" + $" ({searchResults.Count()})",
                                Glyph = new GlyphInfo(FontFamily: "/Resources/#Segoe Fluent Icons", Glyph: "\ue7c3"),
                                Score = 10000,
                                AutoCompleteText = $"{PluginActionKeyword()}All",
                                ContextData = new Dictionary<string, object>
                                {
                                    {"CustomPayload", null }
                                },
                                Action = c =>
                                {
                                    Context.API.ChangeQuery($"{PluginActionKeyword()}All ");
                                    return false;
                                },
                            });
                            nonDefaultBase = false;
                        }



                        CreateMode = false;

                    }
                }
            }

            string KeyForId = null;
            if (editingMode)
            {
                KeyForId = searchResults[editingPatternIdMatch.Groups[1].Value][2].GetString();
            }
            else
            {
                KeyForId = filteredQuery["databaseId"].ToString();
            }

            int ProjectNumber = CountOfUnescaped(query.Search, "!");
            int SelectionNumber = CountOfUnescaped(query.Search, "#");

            if (query.Search.Contains("!") && !IsWritingBlock && !Escaped(query.Search, "!"))
            {

                
                if ( ProjectName.Count > ProjectNumber)
                {
                    ProjectName.Remove(ProjectNumber + 1);
                }
                if (_settings.RelationDatabasesIds.Count > 0)
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

                    if (MultiRelationOptions.EnumerateObject().Count() == 1)
                    {
                        ProjectName[ProjectNumber] = MultiRelationOptions.EnumerateObject().FirstOrDefault().Name;
                    }
                    else if (MultiRelationOptions.EnumerateObject().Count() == 0)
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

                    if (!ProjectName.ContainsKey(ProjectNumber))
                    {
                        foreach (var _projectName in MultiRelationOptions.EnumerateObject())
                        {
                            if (Context.API.FuzzySearch(query.Search.Split('!')[^1].ToLower().Trim(), _projectName.Name.ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                            {

                                var result = new Result
                                {
                                    Title = _projectName.Name,
                                    SubTitle = $"",
                                    Score = 10000,
                                    Action = c =>
                                    {
                                        Context.API.ChangeQuery($"{PluginActionKeyword()}{ConcatSplitedQuery(splitQuery, "!")}!", true);
                                        ProjectName[ProjectNumber] = _projectName.Name;
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
                        Dictionary<int, string> FilteredProjects = filteredQuery["Relations"] as Dictionary<int, string>;

                        if (!FilteredProjects.ContainsKey(ProjectNumber))
                        {
                            string targetProjectId = GetRelationId(ProjectName[ProjectNumber], KeyForId);
                            string ProjectsIdsPath = Path.Combine(cacheDirectory, targetProjectId + ".json");
                            if (!string.IsNullOrEmpty(ProjectsIdsPath))
                            {
                                ProjectsId = LoadJsonData(ProjectsIdsPath);
                                foreach (var project in ProjectsId)
                                {
                                    if (FilteredProjects.Values.Contains(project.Value[0].GetString()) &&
                                        ProjectName[FilteredProjects.FirstOrDefault(p => p.Value == project.Value[0].GetString()).Key] == ProjectName[ProjectNumber])
                                    {
                                        continue;
                                    }
                                    if (Context.API.FuzzySearch(splitQuery[^1].ToLower(), project.Value[0].GetString().ToLower()).Score > 1 || string.IsNullOrEmpty(splitQuery[^1]))
                                    {
                                        var result = new Result
                                        {
                                            Title = project.Value[0].GetString(),
                                            SubTitle = $"",
                                            AutoCompleteText = $"{PluginActionKeyword()}${project.Value[0]}$",
                                            Score = 10000,
                                            ContextData = new Dictionary<string, object>
                                            {
                                                {"RefreshId", targetProjectId },

                                                { "DatabaseTitle", project.Value[5].GetString()}
                                            },
                                            Action = c =>
                                            {
                                                if (c.SpecialKeyState.CtrlPressed)
                                                {
                                                    OpenNotionPage(project.Value[2].ToString());
                                                    return true;
                                                }

                                                Context.API.ChangeQuery($"{PluginActionKeyword()}{ConcatSplitedQuery(splitQuery, "!")}!{project.Value[0]} ");
                                                return false;
                                            },
                                            IcoPath = project.Value[3].ToString()
                                        };
                                        resultList.Add(result);
                                    }
                                }

                                return resultList;
                            }
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
                ProjectName = new Dictionary<int, string>();
            }

            bool BadRequestPropability(string mapField, string property, bool EnumerateArray = true)
            {
                if (!string.IsNullOrEmpty(mapField) &&
                    filteredQuery.ContainsKey("databaseId"))
                    if (EnumerateArray)
                        return !databaseId[filteredQuery["databaseId"].ToString()].GetProperty(property)
                                .EnumerateArray().Any(x => x.GetString() == mapField);
                    else
                        return !databaseId[filteredQuery["databaseId"].ToString()].GetProperty(property)
                                .EnumerateObject().Any(x => x.Name == mapField);
                else
                    return false;
            }

            if (ProjectName.ContainsKey(ProjectNumber) && BadRequestPropability(ProjectName[ProjectNumber], "relation", false))
            {
                Dictionary<string, string> MultiRelationOptions = new Dictionary<string, string>();
                if (databaseId[KeyForId].TryGetProperty("relation", out JsonElement relation))
                {
                    string RelatedPageDatabase = databaseId[_settings.DefaultDatabase].GetProperty("relation")
                                                                                .EnumerateObject()
                                                                                .FirstOrDefault(x => x.Name == ProjectName[ProjectNumber])
                                                                                .Value.GetString();

                    if (RelatedPageDatabase != null)
                    {
                        MultiRelationOptions = relation.EnumerateObject()
                                                        .Where(x => x.Value.GetString() == RelatedPageDatabase)
                                                        .ToDictionary(x => x.Name, x => x.Value.GetString());
                    }
                }

                if (MultiRelationOptions.Count == 0)
                {
                    var result = new Result
                    {
                        Title = $"{filteredQuery["databaseId"]} doesn't contain a relation property for selected page",
                        SubTitle = "click to open database page to create a relation property",
                        Action = c =>
                        {
                            OpenNotionPage(databaseId[KeyForId].GetProperty("url").GetString());
                            return true;
                        },
                        IcoPath = "Images/error.png"
                    };
                    resultList.Add(result);
                    return resultList;

                }
                else if (MultiRelationOptions.Count == 1)
                {
                    ProjectName[ProjectNumber] = MultiRelationOptions.FirstOrDefault().Key;
                    Context.API.ChangeQuery(query.RawQuery, true);
                }
                else
                {
                    string[] splitQuery = query.Search.Split($"@{filteredQuery["databaseId"]}", 2, options: StringSplitOptions.None);
                    string userInput;
                    if (splitQuery.Length < 2)
                        userInput = string.Empty;
                    else
                    {
                        userInput = splitQuery[1];
                    }

                    if (string.IsNullOrEmpty(userInput))
                    {
                        Context.API.ShowMsg("Bad request propability", "Please reselect the the relation property name");
                    }
                    foreach (var _projectName in MultiRelationOptions)
                    {
                        if (Context.API.FuzzySearch(userInput.ToLower(), _projectName.Key.ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                        {
                            var result = new Result
                            {
                                Title = _projectName.Key,
                                SubTitle = $"",
                                Action = c =>
                                {
                                    ProjectName[ProjectNumber] = _projectName.Key;
                                    Context.API.ChangeQuery(PluginActionKeyword() + splitQuery[0] + "@" + filteredQuery["databaseId"].ToString() + " ", true);
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
                if (!SelectionNameMap.ContainsKey(SelectionNumber))
                {
                    SelectionNameMap[SelectionNumber] = string.Empty;
                }
                if (!SelectionTypeMap.ContainsKey(SelectionNumber))
                {
                    SelectionNameMap[SelectionNumber] = null;
                }
                var splitQuery = query.Search.Split('#');
                var userInput = splitQuery[^1].Trim();
                JsonElement MultiSelectOptions;
                JsonElement SingleSelectOptions;
                JsonElement StatusOptions;
                JsonElement CheckBoxOptions;
                try
                {
                    MultiSelectOptions = databaseId[KeyForId].GetProperty("multi_select");
                    SingleSelectOptions = databaseId[KeyForId].GetProperty("select");
                    StatusOptions = databaseId[KeyForId].GetProperty("status");
                    CheckBoxOptions = databaseId[KeyForId].GetProperty("check_box");
                }
                catch (KeyNotFoundException)
                {
                    var ErrorResult = new Result
                    {
                        Title = "Selection properties can not be assigned to pages",
                        SubTitle = "Notion only support assign Selection properties to database items.",
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
                    int MultiSelectOptionsCount = MultiSelectOptions.EnumerateObject().Count();
                    int SingleSelectOptionsCount = StatusOptions.EnumerateObject().Count();
                    int StatusOptionsCount = StatusOptions.EnumerateObject().Count();
                    int CheckBoxOptionsCount = CheckBoxOptions.EnumerateArray().Count();

                    if (MultiSelectOptionsCount + StatusOptionsCount + SingleSelectOptionsCount + CheckBoxOptionsCount == 1)
                    {
                        if (MultiSelectOptionsCount == 1)
                        {
                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.MultiSelectionType;
                            SelectionNameMap[SelectionNumber] = MultiSelectOptions.EnumerateObject().First().Name;
                        }
                        else if (SingleSelectOptionsCount == 1)
                        {
                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.SingleSelectionType;
                            SelectionNameMap[SelectionNumber] = SingleSelectOptions.EnumerateObject().First().Name;
                        }
                        else if (CheckBoxOptionsCount == 1)
                        {
                            SelectionNameMap[SelectionNumber] = CheckBoxOptions.EnumerateObject().First().Name;
                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.CheckBox;

                        }
                        else
                        {
                            SelectionNameMap[SelectionNumber] = StatusOptions.EnumerateObject().First().Name;
                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.StatusSelectionType;
                        }
                    }
                    else if (MultiSelectOptionsCount == 0 && SingleSelectOptionsCount == 0 && StatusOptionsCount == 0 && CheckBoxOptionsCount == 0)
                    {
                        var ErrorResult = new Result
                        {
                            Title = "Database does not contain any selection properties",
                            SubTitle = "click to open database page to create a selection property",
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
                    if (ShowTags && filteredQuery.TryGetValue("tags", out object _tags) && _tags is Dictionary<int, string> Tags && Tags.Count > 0)
                    {
                        if (Tags.ContainsValue(splitQuery[^1].Trim()) || !query.Search.EndsWith($"#{userInput}"))
                        {
                            ShowTags = false;
                            SelectionNameMap[SelectionNumber + 1] = string.Empty;

                        }
                        else if (userInput.Contains(Tags[1]))
                        {
                            ShowTags = false;
                            SelectionNameMap[SelectionNumber + 1] = string.Empty;

                        }
                    }
                    if (string.IsNullOrEmpty(SelectionNameMap[SelectionNumber]))
                    {
                        if (MultiSelectOptionsCount > 0)
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
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#", true);
                                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.MultiSelectionType;
                                            SelectionNameMap[SelectionNumber] = _tagName.Name;
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);
                                }
                            }
                        }

                        if (SingleSelectOptionsCount > 0)
                        {
                            foreach (var _tagName in SingleSelectOptions.EnumerateObject())
                            {
                                if (Context.API.FuzzySearch(userInput.ToLower(), _tagName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                                {
                                    if (IsTagAssignedBefore(_tagName.Name, PropertySelectionType.SingleSelectionType))
                                    {
                                        continue;
                                    }
                                    var result = new Result
                                    {
                                        Title = _tagName.Name,
                                        SubTitle = $"",
                                        Action = c =>
                                        {
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#", true);
                                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.SingleSelectionType;
                                            SelectionNameMap[SelectionNumber] = _tagName.Name;
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);
                                }
                            }
                        }

                        if (StatusOptionsCount > 0)
                        {
                            foreach (var _tagName in StatusOptions.EnumerateObject())
                            {
                                if (Context.API.FuzzySearch(userInput.ToLower(), _tagName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                                {
                                    if (IsTagAssignedBefore(_tagName.Name, PropertySelectionType.StatusSelectionType))
                                    {
                                        continue;
                                    }
                                    var result = new Result
                                    {
                                        Title = _tagName.Name,
                                        SubTitle = $"",
                                        Action = c =>
                                        {
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#", true);
                                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.StatusSelectionType;
                                            SelectionNameMap[SelectionNumber] = _tagName.Name;
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);
                                }
                            }
                        }

                        bool IsTagAssignedBefore(string targetName, PropertySelectionType selectionType)
                        {
                            if (SelectionNameMap.ContainsValue(targetName) &&
                                    SelectionTypeMap[SelectionNameMap.First(x => x.Value == targetName).Key] == selectionType
                                         )
                            {
                                return true;
                            }
                            return false;
                        }

                        if (CheckBoxOptionsCount > 0)
                        {
                            foreach (var _tagName in CheckBoxOptions.EnumerateArray())
                            {
                                if (Context.API.FuzzySearch(userInput.ToLower(), _tagName.ToString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                                {
                                    if (IsTagAssignedBefore(_tagName.GetString(), PropertySelectionType.CheckBox))
                                    {
                                        continue;
                                    }

                                    var result = new Result
                                    {
                                        Title = _tagName.GetString(),
                                        SubTitle = $"",
                                        Action = c =>
                                        {
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#", true);
                                            SelectionTypeMap[SelectionNumber] = PropertySelectionType.CheckBox;
                                            SelectionNameMap[SelectionNumber] = _tagName.GetString();
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);
                                }
                            }
                        }

                        return resultList;

                    }
                    else if (ShowTags)
                    {

                        if (SelectionTypeMap[SelectionNumber] == PropertySelectionType.MultiSelectionType)
                        {
                            foreach (var _tagName in MultiSelectOptions.GetProperty(SelectionNameMap[SelectionNumber]).EnumerateArray())
                            {
                                if (filteredQuery.TryGetValue("tags", out object _assignedTags) &&
                                    _assignedTags is Dictionary<int, string> AssignedTags && AssignedTags.ContainsValue(_tagName.ToString()))
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
                                        Score = 10000,
                                        Action = c =>
                                        {
                                            ShowTags = false;
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#" + _tagName.ToString() + " ", true);
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);

                                }
                            }
                        }
                        else if (SelectionTypeMap[SelectionNumber] == PropertySelectionType.SingleSelectionType)
                        {
                            foreach (var _tagName in SingleSelectOptions.GetProperty(SelectionNameMap[SelectionNumber]).EnumerateArray())
                            {
                                if (filteredQuery.TryGetValue("tags", out object _assignedTags) &&
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
                                        Score = 10000,
                                        Action = c =>
                                        {
                                            ShowTags = false;
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#" + _tagName.ToString() + " ", true);
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);

                                }
                            }
                        }
                        else if (SelectionTypeMap[SelectionNumber] == PropertySelectionType.CheckBox)
                        {
                            string[] CheckBoxStatus = { "Check", "Uncheck" };
                            foreach (var state in CheckBoxStatus)
                            {
                                if (Context.API.FuzzySearch(userInput, state).Score > 1 || string.IsNullOrEmpty(userInput))
                                {
                                    var checkBox = new Result
                                    {
                                        Title = state,
                                        SubTitle = $"",
                                        Score = 10000,
                                        Action = c =>
                                        {
                                            ShowTags = false;
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#" + state + " ", true);
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(checkBox);
                                }
                            }
                        }
                        else
                        {
                            foreach (var _tagName in StatusOptions.GetProperty(SelectionNameMap[SelectionNumber]).EnumerateArray())
                            {
                                if (filteredQuery.TryGetValue("tags", out object _assignedTags) &&
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
                                        Score = 10000,
                                        Action = c =>
                                        {
                                            ShowTags = false;
                                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "#") + "#" + _tagName.ToString() + " ", true);
                                            return false;
                                        },
                                        IcoPath = "Images/database.png"
                                    };
                                    resultList.Add(result);
                                }
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

                if (!query.Search.Contains("#") || Escaped(query.Search, "#"))
                {
                    SelectionNameMap = new Dictionary<int, string>();
                    SelectionTypeMap = new Dictionary<int, PropertySelectionType>();
                }
            }

            // if (MultiSelectionType && BadRequestPropability(TagName, "multi_select", false))
            // {
            //     TagName = null;
            //     // Context.API.ChangeQuery(query.RawQuery.Replace($"#{((List<string>)filteredQuery["tags"])[0]}",""),true);
            // }

            if (!string.IsNullOrEmpty(DateName) && filteredQuery.ContainsKey("Time") && BadRequestPropability(DateName, "date", true))
            {
                JsonElement.ArrayEnumerator MultiDateOptions = databaseId[KeyForId].GetProperty("date").EnumerateArray();

                if (MultiDateOptions.Count() == 1)
                {
                    DateName = MultiDateOptions.First().GetString();
                }
                else if (MultiDateOptions.Count() == 0)
                {
                    var ErrorResult = new Result
                    {
                        Title = "Database does not contain any date properties",
                        SubTitle = "click to open database page to create a date property",
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
                else
                {
                    string[] splitQuery = query.Search.Split($"@{filteredQuery["databaseId"]}", 2, options: StringSplitOptions.None);
                    string userInput;
                    if (splitQuery.Length < 2)
                        userInput = string.Empty;
                    else
                    {
                        userInput = splitQuery[1];
                    }

                    if (string.IsNullOrEmpty(userInput))
                    {
                        Context.API.ShowMsg("Bad request propability", "Please reselect the the date property name");
                    }
                    foreach (var _datePropertyName in MultiDateOptions)
                    {
                        if (Context.API.FuzzySearch(userInput.ToLower(), _datePropertyName.GetString().ToLower()).Score > 1 || string.IsNullOrEmpty(userInput))
                        {
                            var result = new Result
                            {
                                Title = _datePropertyName.GetString(),
                                SubTitle = $"",
                                Action = c =>
                                {
                                    DateName = _datePropertyName.GetString();
                                    Context.API.ChangeQuery(PluginActionKeyword() + " " + splitQuery[0] + "@" + filteredQuery["databaseId"].ToString() + " ", true);
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

            if (filteredQuery.ContainsKey("Time") && string.IsNullOrEmpty(DateName) && !AdvancedFilterMode && !filterMode)
            {
                JsonElement MultiDateOptions = databaseId[KeyForId].GetProperty("date");
                if (!(MultiDateOptions.EnumerateArray().Count() > 1))
                {
                    DateName = MultiDateOptions.EnumerateArray().FirstOrDefault().ToString();
                }
                else
                {
                    string[] splitQuery = query.Search.Split(filteredQuery["TimeText"].ToString());
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
                                    Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, filteredQuery["TimeText"] as string) + filteredQuery["TimeText"].ToString().Trim() + " ");
                                    // Context.API.ChangeQuery(Context.CurrentPluginMetadata.ActionKeyword + " " +
                                    // (string.IsNullOrEmpty(splitQuery[^2].Trim().Replace(filteredQuery["TimeText"].ToString(), "",
                                    //  culture: null, ignoreCase: true)) ? ""
                                    //  : splitQuery[^2].Trim() + " ") + filteredQuery["TimeText"].ToString().Trim() + " ", requery: true);
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
                            Context.API.ReQuery(false);
                            string AfterTimeText = query.Search.Substring((int)filteredQuery["End"] + 1);
                            Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, filteredQuery["TimeText"].ToString()) +
                            "\\" + filteredQuery["TimeText"].ToString().Trim().Trim() + AfterTimeText, requery: true);
                            return false;
                        },
                        Score = -1000,
                        IcoPath = "Images/error.png"
                    };
                    resultList.Add(cancel);

                    return resultList;
                }
            }

            if (!filterMode && query.Search.Contains("@") && (bool)filteredQuery["IsDefaultDB"] && !editingMode && !IsWritingBlock && !Escaped(query.Search, "@") &&
                !(bool)filteredQuery["fakeDBKeywork"])
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
                            Score = 10000,
                            AutoCompleteText = $"{PluginActionKeyword()}${kv.Key}$",
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
                                    Context.API.ChangeQuery($"{PluginActionKeyword()}{ConcatSplitedQuery(splitQuery, "@")}@{kv.Key} ");
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

            if (!filterMode && query.Search.Contains("[") && !filteredQuery.ContainsKey("ContainUrlKeyword") && string.IsNullOrEmpty(UrlMap) && !IsWritingBlock && !Escaped(query.Search, "\\["))
            {
                JsonElement.ArrayEnumerator UrlMapOptions = new JsonElement.ArrayEnumerator();
                if (filteredQuery.ContainsKey("databaseId")) {
                    UrlMapOptions = databaseId[filteredQuery["databaseId"].ToString()].GetProperty("urlMap").EnumerateArray();
                }
                else
                {
                    var ErrorResult = new Result
                    {
                        Title = "Url properties can not be assigned to pages",
                        SubTitle = "Notion only support assign Url properties to database items.",
                        Action = c =>
                        {
                            return true;
                        },
                        IcoPath = "Images/error.png"
                    };
                    resultList.Add(ErrorResult);
                    return resultList;
                }
                if (UrlMapOptions.Count() > 1)
                {
                    var splitQuery = query.Search.Split("[");

                    foreach (var _urlOption in UrlMapOptions)
                    {
                        if (Context.API.FuzzySearch(splitQuery[^1].ToLower().Trim(), _urlOption.GetString().ToLower().Trim()).Score > 1 || string.IsNullOrEmpty(splitQuery[^1]))
                        {
                            var result = new Result
                            {
                                Title = _urlOption.GetString(),
                                Score = 10000,
                                IcoPath = "Images/embed.png",
                                Action = c =>
                                {
                                    UrlMap = _urlOption.GetString();
                                    Context.API.ChangeQuery(PluginActionKeyword() + ConcatSplitedQuery(splitQuery, "[") + "[", true);
                                    return false;
                                }
                            };
                            resultList.Add(result);
                        }
                    }
                    return resultList;

                }
                else if (UrlMapOptions.Count() == 0)
                {
                    var ErrorResult = new Result
                    {
                        Title = "Database does not contain any URL properties",
                        SubTitle = "click to open database page to create a URL property",
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
                else if (UrlMapOptions.Count() == 1)
                {
                    UrlMap = UrlMapOptions.First().GetString();
                }
            }
            else if (!IsWritingBlock && Escaped(query.Search, "\\["))
            {
                UrlMap = null;
            }

            if (userInputSearch.Count() > 0 && !AdvancedFilterMode)
            {
                foreach (var item in userInputSearch)
                {
                    var result = new Result
                    {
                        Title = $"{(string.IsNullOrWhiteSpace(item.Value[1]) ? "Untitled" : item.Value[1])}",
                        SubTitle = $"{item.Value[4]}",
                        AutoCompleteText = $"{PluginActionKeyword()}${item.Key}$ ",
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

            // if (!query.Search.ToLower().StartsWith("search") && query.Search != "refresh" && !AdvancedFilterMode && (!query.Search.Contains("$") || editingMode))

            if (!editingMode)
                currPageProperties = null;

            if (!AdvancedFilterMode && !filterMode && CreateMode)
            {
                if (!IsWritingBlock)
                {
                    if (!editingMode)
                    {
                        if (userInputSearch.Count <= 0)
                        {
                            var result = new Result
                            {
                                Title = $"Create {filteredQuery["Name"]}",
                                SubTitle = string.Concat(DBSubtitle, PSubtitle, tagSubtitle, TimeValue, link),
                                Score = -100,
                                ContextData = new Dictionary<string, object>
                                {
                                    {"Title", filteredQuery["Name"].ToString() },
                                    { "PageId", null },
                                    { "Url", null },
                                    { "Project_name", filteredQuery.ContainsKey("Project") ? filteredQuery["Project"]: "" },
                                    { "CreateFirst", filteredQuery}
                                },
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.ShiftPressed)
                                    {
                                        ToggleDefaultAfterCreateAction();
                                    }
                                    _ = subProcess(create: true, dict_arg: filteredQuery, open: c.SpecialKeyState.CtrlPressed != _settings.PopUpPageAfterCreate);
                                    refresh_search = DateTime.Now;
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        return false;
                                    }
                                    else
                                    {
                                        return true;
                                    }
                                },
                                IcoPath = "Images/app.png"
                            };
                            resultList.Add(result);
                        }
                    }
                    else
                    {
                        string editing_title = "";
                        if (string.IsNullOrEmpty(filteredQuery["Name"].ToString().Trim()))
                        {
                            editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0]}";
                        }
                        else if (!string.IsNullOrEmpty(filteredQuery["Name"].ToString()))
                        {
                            editing_title = $"Renaming {filteredQuery["Name"]}";
                        }
                        string SubTitle = string.IsNullOrEmpty(PSubtitle) ? $"{tagSubtitle}{TimeValue}{link}" : $"{DBSubtitle}{PSubtitle}{tagSubtitle}{TimeValue}{link}";
                        var result = new Result
                        {
                            Title = $"{editing_title}",
                            SubTitle = SubTitle,
                            Score = 10000,
                            IcoPath = searchResults[editingPatternIdMatch.Groups[1].Value][3].ToString(),
                            ContextData =
                                    new Dictionary<string, object>
                                    {
                                        { "Title", string.IsNullOrEmpty(filteredQuery["Name"].ToString().Trim()) ? searchResults[editingPatternIdMatch.Groups[1].Value][0].GetString() : filteredQuery["Name"].ToString() },
                                        { "tags", new List<string> { tagSubtitle } },
                                        { "Project_name", filteredQuery.ContainsKey("Project") ? filteredQuery["Project"]: searchResults[editingPatternIdMatch.Groups[1].Value][1].GetString() },
                                        { "id", editingPatternIdMatch.Groups[1].Value },
                                        { "edit", true },
                                        { "CreateFirst", new object()}
                                    },
                            Action = c =>
                            {
                                _ = subProcess(edit: true, pageId: editingPatternIdMatch.Groups[1].Value, dict_arg: filteredQuery, open: c.SpecialKeyState.CtrlPressed);
                                refresh_search = DateTime.Now;
                                return true;
                            },
                            AutoCompleteText = $"{PluginActionKeyword()}{query.Search} {editing_title.Replace("Edit ", "")}",
                        };


                        resultList.Add(result);
                        if (currPageProperties is null)
                        {
                            _ = Task.Run(async () =>
                            {
                                currPageProperties = _notionDataParser.RetrievePageJsonObjectById(editingPatternIdMatch.Groups[1].Value)["properties"];

                                if (currPageProperties is not null)
                                    Context.API.ReQuery(false);
                            });
                        }
                        else
                        {
                            try {
                            resultList.AddRange(_notionDataParser.PropertiesIntoResults(filteredQuery["Name"].ToString(), currPageProperties as JObject)); }
                            catch { }
                        }

                    }
                }
                else
                {
                    List<List<string>> contentList = filteredQuery["content"] as List<List<string>>;
                    foreach (int block in Enumerable.Range(0, contentList.Count).Reverse())
                    {
                        string[] lines = filteredQuery.ContainsKey("content") && filteredQuery["content"] is List<List<string>> Index_one
                        ? Index_one[block][1].Split("\n")
                        : new string[0];
                        string[] linesWithoutFirst = lines.Skip(Math.Max(0, lines.Length - 2)).ToArray();
                        string resultString = string.Join(Environment.NewLine, linesWithoutFirst);
                        string subtitleForBlock = (linesWithoutFirst.Length <= 1) ?
                        string.Concat(DBSubtitle, PSubtitle, tagSubtitle, TimeValue, link) :
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
                                            Context.API.ChangeQuery($"{PluginActionKeyword()}{this.ConcatSplitedQuery(query.Search.Split("^"), "^")}^", true);
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
                                    { "Title", filteredQuery["Name"].ToString() },
                                    { "PageId", null },
                                    { "Url", null },
                                    { "Project_name", filteredQuery.ContainsKey("Project") ? filteredQuery["Project"]: "" },
                                    { "CreateFirst", filteredQuery}
                                },
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        Context.API.ChangeQuery($"{PluginActionKeyword() + query.Search}" + "{clipboard}", true);
                                        return false;
                                    }

                                    if (c.SpecialKeyState.ShiftPressed)
                                    {
                                        ToggleDefaultAfterCreateAction();
                                    }

                                    _ = subProcess(create: true, dict_arg: filteredQuery, open: c.SpecialKeyState.CtrlPressed != _settings.PopUpPageAfterCreate);
                                    refresh_search = DateTime.Now;
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        return false;
                                    }
                                    else
                                    {
                                        return true;
                                    }
                                },
                                IcoPath = "Images/app.png"
                            };
                            resultList.AddRange(ProcessQueryResults(query, resultString, result, block));
                        }
                        else
                        {
                            string editing_title = "";
                            if (string.IsNullOrEmpty(filteredQuery["Name"].ToString()))
                            {
                                editing_title = $"Edit {searchResults[editingPatternIdMatch.Groups[1].Value][0]}";
                            }
                            else if (!string.IsNullOrEmpty(filteredQuery["Name"].ToString()))
                            {
                                editing_title = $"Renaming {filteredQuery["Name"]}";
                            }
                            var result = new Result
                            {
                                Title = $"{resultString}",
                                SubTitle = string.IsNullOrEmpty(DBSubtitle) ? subtitleForBlock : subtitleForBlock.Replace(DBSubtitle,""),
                                Score = 10000,
                                ContextData = new Dictionary<string, object>
                                {
                                    { "desc", filteredQuery["Name"].ToString().Trim() },
                                    { "tags", new List<string> { tagSubtitle } },
                                    { "project", new List<string> { PSubtitle } },
                                    { "query", filteredQuery },
                                    { "id", editingPatternIdMatch.Groups[1].Value },
                                    { "edit", true },
                                    {"CreateFirst", false},
                                },
                                TitleToolTip = "Hold Alt key to paste the clipboard",
                                Action = c =>
                                {
                                    if (c.SpecialKeyState.AltPressed)
                                    {
                                        Context.API.ChangeQuery($"{PluginActionKeyword() + query.Search}" + "{clipboard}", true);
                                        return false;
                                    }
                                    Context.API.HideMainWindow();
                                    _ = subProcess(edit: true, pageId: editingPatternIdMatch.Groups[1].Value, dict_arg: filteredQuery, open: c.SpecialKeyState.CtrlPressed);
                                    refresh_search = DateTime.Now;
                                    new List<object> { editingPatternIdMatch.Groups[1].Value, filteredQuery };
                                    return true;
                                },
                                AutoCompleteText = $"{PluginActionKeyword()}${editingPatternIdMatch.Groups[1].Value}$ *",
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
                QueryName = string.Join($"{Prefix}", QueryList);
                return QueryName;
            }
            else
            {
                QueryName = splitQuery[^2];
            }
            return QueryName;
        }

        public Control CreateSettingPanel()
        {
            return new NotionSettings(Context, Main._viewModel!, _notionDataParser);
        }


        public void Dispose()
        {
            Context.API.VisibilityChanged -= OnVisibilityChanged;
        }

        public async Task ReloadDataAsync()
        {
            CreatePathsIfNeeded();
            databaseId = await _notionDataParser.DatabaseCache();
            List<Task> relationTasks = new List<Task>();
            if (_settings.RelationDatabasesIds.Count != 0)
            {
                foreach (string databaseId in _settings.RelationDatabasesIds)
                {
                    relationTasks.Add(Task.Run(async () =>
                    {
                        await this._notionDataParser.QueryDB(databaseId, null, Path.Combine(cacheDirectory, $"{databaseId}.json"));
                    }));
                }
            }
            await Task.WhenAll(relationTasks);
            await _notionDataParser.CallApiForSearch();
        }



        bool Escaped(string FullString, string Prefix)
        {
            string EscapePattern = $@".*(?<!\\){Prefix}.*";
            Match EscapeMatch = Regex.Match(FullString, EscapePattern, RegexOptions.IgnoreCase);
            return !EscapeMatch.Success;
        }
        int CountOfUnescaped(string FullString, string Prefix)
        {
            string EscapePattern = $@"(?<!\\){Prefix}";
            var EscapeMatch = Regex.Matches(FullString, EscapePattern, RegexOptions.IgnoreCase);
            return EscapeMatch.Count;
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

            try {
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
            } catch {
                return string.Empty;
            }
        }

        Dictionary<string, object> GetData(string rawInputstring, string defaultDB = "", bool TimeSkip = false, bool ManualTagsRunning = false, bool ManualDBRunning = false, bool ManualProjectRunning = false)
        {
            string inputString = rawInputstring;
            Dictionary<string, object> dataDict = new Dictionary<string, object>();

            if (!TimeSkip && !string.IsNullOrEmpty(defaultDB))
            {
                ModelResult modelResult;
                string RefinedName = TextToDate(out modelResult, inputString, ((ManualTagsRunning || ManualProjectRunning) == false));
                if (modelResult != null)
                {
                    Dictionary<string, string> LastValue = ((List<Dictionary<string, string>>)modelResult.Resolution["values"]).Last();
                    if (LastValue.ContainsKey("start") || LastValue.ContainsKey("value"))
                    {
                        inputString = RefinedName;
                        dataDict["parsedDate"] = GetDateFromMethodResult(LastValue);
                        dataDict["Time"] = HumanizedDate(dataDict["parsedDate"] as List<string>);
                        dataDict["TimeText"] = modelResult.Text;
                        dataDict["Start"] = modelResult.Start;
                        dataDict["End"] = modelResult.End;
                    }
                }
            }

            if (!ManualDBRunning)
            {
                if (!dataDict.ContainsKey("databaseId") && inputString.Contains("@"))
                {
                    string Pattern = @"(?<!\\)@([^\\!]*)";

                    var DatabaseMatch = Regex.Match(inputString, Pattern);
                    if (DatabaseMatch.Success)
                    {
                        string userInput = DatabaseMatch.Groups[^1].Value.Trim();
                        foreach (var _item in databaseId.Keys)
                        {
                            if (!_item.Contains("@"))
                            {
                                userInput = userInput.Split("@")[0];
                            }
                            else
                            {
                                userInput = DatabaseMatch.Groups[^1].Value.Trim();
                            }
                            if (userInput.Contains(_item))
                            {

                                dataDict["databaseId"] = _item;
                                string item = Regex.Escape(_item);
                                string TransformName = Regex.Replace(userInput, item, "", RegexOptions.IgnoreCase).Trim();

                                string unRawInputString = string.Empty;
                                Match unRawInputStringMatch = Regex.Match(inputString, $@"\s?(?<!\\)@\s?{item}", RegexOptions.IgnoreCase);
                                if (unRawInputStringMatch.Success)
                                {
                                    // replace at the first occurrence of the match
                                    unRawInputString = inputString.Remove(unRawInputStringMatch.Index, unRawInputStringMatch.Length);
                                }
                                // unRawInputString = Regex.Replace(inputString.Trim(), $@"\s?\@\s?{item}", "", RegexOptions.IgnoreCase).Trim();
                                inputString = unRawInputString;
                                if (GetData(unRawInputString, defaultDB: defaultDB, TimeSkip: true, ManualDBRunning: true).TryGetValue("Name", out object Name))
                                {
                                    dataDict["Name"] = Name.ToString();
                                }
                                else
                                {
                                    dataDict["Name"] = TransformName;
                                }

                                if (_item.Contains("[") && Escaped(inputString, "\\["))
                                {
                                    dataDict["ContainUrlKeyword"] = true;
                                }

                            }
                        }
                    }
                }
            }

            string pattern = @"(\$.*\$)|((?:\*|\^)+\s?[^\*\^]*)|\s?([^*^]+)";
            var match = Regex.Matches(inputString, pattern);
            var dataList = match.Cast<Match>().SelectMany(m => m.Groups.Cast<Group>().Skip(1)).Select(g => g.Value.Trim()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            foreach (var type in dataList)
            {
                if (type.StartsWith("$") && type.EndsWith("$"))
                {
                    dataDict["filter"] = type.Trim();
                    break;
                }

                if (!(type.StartsWith("#") || type.StartsWith("*") || type.StartsWith("^") || (type.StartsWith("$") && type.EndsWith("$"))))
                {
                    if (!type.Contains($"$ {type}") && !type.Contains($"{type}$"))
                    {
                        string unRefinedName = type;
                        Match UrlMatch = Regex.Match(type, @"(?<!\\)\[([^\]]+)");
                        if (UrlMatch.Success && UrlMatch.Groups.Count == 2)
                        {
                            dataDict["link"] = UrlMatch.Groups[1].Value;

                            try
                            {
                                string escapedReplacement = Regex.Escape(UrlMatch.Groups[0].Value);
                                unRefinedName = Regex.Replace(unRefinedName, $@"\s?{escapedReplacement}\]?", "").Trim();
                            }
                            catch (RegexParseException) { }

                        }
                        unRefinedName = RefineQueryText(inputString, unRefinedName).Trim();
                        if (!string.IsNullOrEmpty(unRefinedName))
                            dataDict["Name"] = unRefinedName;
                    }
                }

                // Condition for *
                if (type.StartsWith("^") || type.StartsWith("*"))
                {
                    var typeFormatted = Regex.Unescape(type.Remove(0, 1));
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



            if (!dataDict.ContainsKey("databaseId") && !string.IsNullOrEmpty(defaultDB))
            {
                dataDict["databaseId"] = defaultDB;
                dataDict["IsDefaultDB"] = true;
            }
            else
            {
                dataDict["IsDefaultDB"] = false;
            }

            if (!ManualProjectRunning)
            {
                if (inputString.Contains("!"))
                {
                    string RelationsPattern = @"(?<!\\)!([^\\!]*)";
                    var ExtractedRelations = Regex.Matches(inputString, RelationsPattern);
                    Dictionary<int, string> selectedRelations = new Dictionary<int, string>();
                    List<string> selectedRelationRegex = new List<string>();
                    int number = 1;
                    dataDict["Relations"] = new Dictionary<int, string>();
                    if (ProjectName.ContainsKey(number) && !string.IsNullOrEmpty(ProjectName[number]))
                    {
                        foreach (Match _relation in ExtractedRelations)
                        {
                            var userInput = _relation.Groups[1].Value.Trim();
                            if (ProjectName.ContainsKey(number))
                            {
                                string ProjectsIdsPath = GetRelationFilePath(ProjectName[number], dataDict["databaseId"] as string);
                                if (!string.IsNullOrEmpty(ProjectsIdsPath))
                                {
                                    ProjectsId = LoadJsonData(ProjectsIdsPath);
                                    foreach (var availableRelation in ProjectsId.Values)
                                    {
                                        if (("!" + userInput).Contains($"!{availableRelation[0].GetString()}"))
                                        {
                                            selectedRelations[number] = availableRelation[0].GetString();
                                            selectedRelationRegex.Add(Regex.Escape(availableRelation[0].GetString()));
                                            number++;
                                        }
                                    }

                                    if (selectedRelations.Count != 0)
                                    {
                                        dataDict["Relations"] = selectedRelations;
                                        string refinedName = Regex.Replace(rawInputstring.Trim(), $@"\s?(?<!\\)!\s?({string.Join("|", selectedRelationRegex)})", "", RegexOptions.IgnoreCase).Trim();
                                        var filteredQueryForRelations = GetData(refinedName, defaultDB: defaultDB, TimeSkip: false, ManualProjectRunning: true);
                                        if (filteredQueryForRelations.TryGetValue("Name", out object Name))
                                        {
                                            dataDict["Name"] = Name.ToString();
                                        }
                                        else
                                        {
                                            dataDict["Name"] = string.Empty;
                                        }

                                        dataDict["databaseId"] = filteredQueryForRelations["databaseId"].ToString();
                                        if (refinedName.Contains("@") && !Escaped(inputString, "@"))
                                        {
                                            dataDict["fakeDBKeywork"] = false;
                                        }
                                        else
                                        {
                                            dataDict["fakeDBKeywork"] = true;
                                        }
                                    }
                                }

                            }
                        }
                    }
                }
                else
                {
                    dataDict["Relations"] = new Dictionary<int, string>();
                    dataDict["fakeDBKeywork"] = false;
                }

            }

            if (!ManualTagsRunning)
            {
                if (inputString.Contains("#"))
                {
                    string RelationsPattern = @"(?<!\\)#([^\\#]*)";
                    var ExtractedTags = Regex.Matches(inputString, RelationsPattern);
                    Dictionary<int, string> selectedTags = new Dictionary<int, string>();
                    List<string> selectedTagsRegex = new List<string>();
                    int number = 1;
                    dataDict["tags"] = new Dictionary<int, string>();
                    try
                    {
                        if (SelectionNameMap.ContainsKey(number) && !string.IsNullOrEmpty(SelectionNameMap[number]))
                        {
                            foreach (Match _tag in ExtractedTags)
                            {
                                var userInput = _tag.Groups[1].Value.Trim() + " ";
                                if (SelectionNameMap.ContainsKey(number))
                                {

                                    var databaseElement = databaseId[dataDict["databaseId"].ToString()];

                                    JsonElement tagsArray = new JsonElement();
                                    if (SelectionTypeMap[number] == PropertySelectionType.MultiSelectionType)
                                    {
                                        tagsArray = databaseElement.GetProperty("multi_select").GetProperty(SelectionNameMap[number]);
                                    }
                                    else if (SelectionTypeMap[number] == PropertySelectionType.SingleSelectionType)
                                    {
                                        tagsArray = databaseElement.GetProperty("select").GetProperty(SelectionNameMap[number]);
                                    }
                                    else if (SelectionTypeMap[number] == PropertySelectionType.StatusSelectionType)
                                    {
                                        tagsArray = databaseElement.GetProperty("status").GetProperty(SelectionNameMap[number]);
                                    }

                                    List<string> availableTags;

                                    if (SelectionTypeMap[number] == PropertySelectionType.CheckBox)
                                    {
                                        availableTags = new List<string> { "Check", "Uncheck" };
                                    }
                                    else
                                    {
                                        availableTags = tagsArray.EnumerateArray().Select(item => item.GetString()).ToList();
                                    }

                                    foreach (var _availableTag in availableTags)
                                    {
                                        if (("#" + userInput).Contains("#" + _availableTag + " "))
                                        {
                                            selectedTags[number] = _availableTag;
                                            selectedTagsRegex.Add(_availableTag);
                                            number++;
                                        }
                                    }

                                    if (selectedTags.Count != 0)
                                    {
                                        dataDict["tags"] = selectedTags;
                                        string refinedName = Regex.Replace(rawInputstring.Trim(), $@"\s?(?<!\\)#\s?({string.Join("|", selectedTagsRegex)})", "", RegexOptions.IgnoreCase).Trim();
                                        var filteredQueryForTags = GetData(refinedName, defaultDB: defaultDB, TimeSkip: false, ManualTagsRunning: true);
                                        if (filteredQueryForTags.TryGetValue("Name", out object Name))
                                        {
                                            dataDict["Name"] = Name.ToString();
                                        }
                                        else
                                        {
                                            dataDict["Name"] = string.Empty;
                                        }

                                        dataDict["databaseId"] = filteredQueryForTags["databaseId"].ToString();
                                        if (refinedName.Contains("@") && !Escaped(inputString, "@"))
                                        {
                                            dataDict["fakeDBKeywork"] = false;

                                        }
                                        else
                                        {
                                            dataDict["fakeDBKeywork"] = true;
                                        }
                                    }
                                }

                            }

                        }
                    }
                    catch
                    {

                    }
                }
                else
                {
                    dataDict["tags"] = new Dictionary<int, string>();
                    dataDict["fakeDBKeywork"] = false;
                }

            }

            return dataDict;
        }

        List<string> GetDateFromMethodResult(Dictionary<string, string> ResoultionValues)
        {
            List<string> parsedDates;

            if (ResoultionValues.TryGetValue("value", out string date))
            {
                parsedDates = new List<string> {date, null};
            }
            else
                if (ResoultionValues.TryGetValue("type", out string type) && (type == "daterange" || type == "datetimerange" || type == "timerange" ))
                {
                    if (ResoultionValues.TryGetValue("start", out string startDate)) {
                        parsedDates = new List<string> {ResoultionValues["start"], ResoultionValues["end"]};
                    } else {
                        parsedDates = new List<string> {ResoultionValues["end"], null};
                    }
                }
                else
                {
                    parsedDates = new List<string> {ResoultionValues["start"], null};
                }

            return parsedDates;
        }

        string AppendTimeIfNeeded(DateTime dateTime, string sep = " | ")
        {
            if (dateTime.TimeOfDay != TimeSpan.Zero)
            {
                return sep + dateTime.ToString("h:mm tt", CultureInfo.InvariantCulture);
            }
            else {
                return string.Empty;
            }
        }

        string HumanizedDate(List<string> parsedDate)
        {
            if (string.IsNullOrEmpty(parsedDate[1])) {
                DateTime parsedclock = Convert.ToDateTime(parsedDate[0]);
                string HumanizedDate = parsedclock.ToString("ddd", CultureInfo.InvariantCulture);
                if (parsedclock.TimeOfDay != TimeSpan.Zero)
                {
                    HumanizedDate = HumanizedDate + AppendTimeIfNeeded(parsedclock);
                }
                else
                {
                    string daySuffix = GetDaySuffix(parsedclock.Day);
                    HumanizedDate = $"{HumanizedDate} | {parsedclock.Day + daySuffix} {parsedclock.ToString("MMM yyyy", CultureInfo.InvariantCulture)}";
                }
                return HumanizedDate;
            }
            else
            {
                DateTime StartDate = Convert.ToDateTime(parsedDate[0]);
                DateTime EndDate = Convert.ToDateTime(parsedDate[1]);
                string HumanizedStartDate = NotionDataParser.GetHumanDateFormat(StartDate);
                string HumanizedEndDate = NotionDataParser.GetHumanDateFormat(EndDate);

                if (HumanizedStartDate == HumanizedEndDate)
                {
                    string DateTime = AppendTimeIfNeeded(EndDate,sep: "");
                    if (!string.IsNullOrEmpty(DateTime))
                    {
                        return HumanizedStartDate + AppendTimeIfNeeded(StartDate,sep: " ") + " → " + DateTime;
                    }
                }
                 
                // return (HumanizedEndDate.Contains(HumanizedStartDate.Substring(HumanizedStartDate.Length - 4))
                //      ? HumanizedStartDate.Substring(0, HumanizedStartDate.Length - 6)
                //      : HumanizedStartDate) + " → " + HumanizedEndDate;
                return HumanizedStartDate + AppendTimeIfNeeded(StartDate,sep: " ") + " → " + HumanizedEndDate + AppendTimeIfNeeded(EndDate,sep: " ");

            }

            
        }

        bool IsDateOrTime(string typeName) =>
            typeName.StartsWith("datetimeV2.date") || typeName.StartsWith("datetimeV2.time");

        bool IsDuration(string typeName) =>
            typeName == "datetimeV2.duration";


        bool IsCombinedBetweenRangeAndDuration (List<ModelResult> results,string input, out ModelResult CombinedOne)
        {
            CombinedOne = new ModelResult();
            if ((IsDateOrTime(results[0].TypeName) && IsDuration(results[1].TypeName)) ||
                (IsDateOrTime(results[1].TypeName) && IsDuration(results[0].TypeName)))
            {
                string StartDate;
                string EndDate;
                if (IsDateOrTime(results[0].TypeName))
                {
                    StartDate = GetDateFromMethodResult(((List<Dictionary<string, string>>)results[0].Resolution["values"]).Last())[0];
                    
                    EndDate = Convert.ToDateTime(StartDate).AddSeconds(int.Parse(((List<Dictionary<string, string>>)results[1].Resolution["values"]).Last()["value"])).ToString();

                } else {
                    StartDate = GetDateFromMethodResult(((List<Dictionary<string, string>>)results[1].Resolution["values"]).Last())[0];
                    EndDate = Convert.ToDateTime(StartDate).AddSeconds(int.Parse(((List<Dictionary<string, string>>)results[0].Resolution["values"]).Last()["value"])).ToString();

                }
                CombinedOne.Start = results[0].Start; 
                CombinedOne.End = results[1].End;
                CombinedOne.Text = input.Substring(results[0].Start, results[1].End - results[0].Start + 1);
                CombinedOne.TypeName = "datetimeV2.daterange";
                CombinedOne.Resolution = new SortedDictionary<string, object> 
                {
                    { 
                        "values", new List<Dictionary<string, string>> 
                        { 
                            new Dictionary<string, string> 
                            {
                                { "type", "daterange" },
                                { "start", StartDate } ,
                                { "end", EndDate } 

                            }
                        } 
                    }
                };

                return true;
            }
            return false;

        }

        public string TextToDate(out ModelResult methodResult, string input = "", bool fullInput = true)
        {
            methodResult = null;
            var results = DateTimeRecognizer.RecognizeDateTime(input, "en-us");
            bool IsItFilter = false;
            if (fullInput) {
                foreach (CustomPayload filter in _settings.Filters)
                {
                    if (filter.Enabled && filter.JsonType == JsonType.Filter && input.StartsWith(filter.Title, StringComparison.OrdinalIgnoreCase))
                    {
                        IsItFilter = true;
                    }
                }
            }

            string returnedName = input;
            if (results.Any())
            {
                try
                {
                    // string jsonString = System.Text.Json.JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
                    // File.WriteAllText(_settings.FullCachePath + ".txt", jsonString);
                    
                    if (results.Count > 1)
                    {
                        bool Combined = IsCombinedBetweenRangeAndDuration(results, input,out ModelResult CombinedResult);
                        if (Combined)
                        {
                            results = new List<ModelResult> {CombinedResult};
                        }
                        
                    }
                    
                    
                    foreach (ModelResult result in results)
                    {
                        
                        if (result.TypeName != "datetimeV2.duration" && !IsItFilter &&
                        (input.Trim().StartsWith(result.Text, StringComparison.OrdinalIgnoreCase) ||
                        input.Substring(result.Start - 1, 1) != "\\") &&
                        !(input.Substring(0, result.Start).Contains("*") || input.Substring(0, result.Start).Contains("^")))
                        {
                            methodResult = result;
                            if (!input.Trim().StartsWith(result.Text, StringComparison.OrdinalIgnoreCase))
                            {
                                if (input.StartsWith("on", StringComparison.OrdinalIgnoreCase) || input.Substring(result.Start - 4, 4).Equals(" on ", StringComparison.OrdinalIgnoreCase) ) {
                                    returnedName = returnedName.Remove(result.Start - 3, result.End - result.Start + 4);
                                }
                                else if (input.StartsWith("from", StringComparison.OrdinalIgnoreCase) || input.Substring(result.Start - 6, 6).Equals(" from ", StringComparison.OrdinalIgnoreCase)) {
                                    returnedName = returnedName.Remove(result.Start - 5, result.End - result.Start + 6);
                                }
                                else {
                                    returnedName = returnedName.Remove(result.Start, result.End - result.Start + 1);

                                }
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
                catch (ArgumentOutOfRangeException)
                {
                    methodResult = null;
                    return input;
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

        (Dictionary<string, Dictionary<string, object>>, Dictionary<string, List<Dictionary<string, object>>>, string) FormatData(Dictionary<string, object> filteredQueryEditing, string Mode = "Create", string DbNameInCache = "", string datePropName = null, string pageId = null)
        {
            string DATABASE_ID = null;
            // Context.API.ShowMsg(ProjectName.First().Key.ToString(), ProjectName.First().Value.ToString());
            Dictionary<string, List<Dictionary<string, object>>> children = new Dictionary<string, List<Dictionary<string, object>>> {
                { "children", new List<Dictionary<string, object>>() }};

            if (filteredQueryEditing.ContainsKey("databaseId") && Mode != "Edit")
            {
                DATABASE_ID = Convert.ToString(databaseId[filteredQueryEditing["databaseId"].ToString()].GetProperty("id").ToString());
            }
            else
            {
                if (DbNameInCache != null)
                {
                    filteredQueryEditing["databaseId"] = DbNameInCache;
                    DATABASE_ID = DbNameInCache;
                }
                else
                {
                    DATABASE_ID = null;
                }
            }

            var data = new Dictionary<string, Dictionary<string, object>> { };

            if (!string.IsNullOrWhiteSpace(filteredQueryEditing["Name"].ToString()))
            {
                string titleMap;
                if (!string.IsNullOrEmpty(DATABASE_ID))
                {
                    titleMap = $"{databaseId[filteredQueryEditing["databaseId"].ToString()].GetProperty("title")}";
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
                                        { "content", filteredQueryEditing["Name"] }
                                    }
                                }
                            }
                        }
                    }
                };
            };
            if (!string.IsNullOrEmpty(DATABASE_ID))
            {

                if (filteredQueryEditing.ContainsKey("parsedDate") && databaseId[filteredQueryEditing["databaseId"].ToString()].GetProperty("date").GetArrayLength() > 0)
                {
                    List<string> parsed_result_strings = new List<string>();
                    foreach (string dateValue in filteredQueryEditing["parsedDate"] as List<string>)
                    {
                        string parsed_result_string = null;
                        if(!string.IsNullOrEmpty(dateValue))
                        {
                            DateTime parsed_result = Convert.ToDateTime(dateValue);
                            
                            if (parsed_result.TimeOfDay != TimeSpan.Zero)
                            {
                                parsed_result = parsed_result.ToUniversalTime();
                                parsed_result_string = parsed_result.ToString("yyyy-MM-ddTHH:mm:ss");
                            }
                            else
                            {
                                parsed_result_string = parsed_result.ToString("yyyy-MM-dd");
                            }
                        }
                        parsed_result_strings.Add(parsed_result_string);

                    }
                    
                    
                    

                    if (!data.ContainsKey(DateName))
                    {
                        data.Add(DateName, new Dictionary<string, object>());
                    }
                    data[DateName].Add("date", new Dictionary<string, object> { { "start", parsed_result_strings[0] }, { "end", parsed_result_strings[1] } });
                }

                if (filteredQueryEditing.TryGetValue("tags", out object tags) && tags is Dictionary<int, string> Tags && Tags.Count > 0)
                {
                    int mapKey = 1;

                    void AddKeyIfNeeded()
                    {
                        if (!data.ContainsKey(SelectionNameMap[mapKey]))
                        {
                            data.Add(SelectionNameMap[mapKey], new Dictionary<string, object>());
                        }
                    }

                    foreach (var tag in Tags.Values)
                    {
                        AddKeyIfNeeded();
                        if (SelectionTypeMap[mapKey] == PropertySelectionType.MultiSelectionType)
                        {
                            if (!data[SelectionNameMap[mapKey]].ContainsKey("multi_select"))
                            {
                                data[SelectionNameMap[mapKey]].Add("multi_select", new List<Dictionary<string, string>>());
                            }
                            List<Dictionary<string, string>> previousTags = data[SelectionNameMap[mapKey]]["multi_select"] as List<Dictionary<string, string>>;
                            previousTags.Add(new Dictionary<string, string> { { "name", tag } });
                            data[SelectionNameMap[mapKey]]["multi_select"] = previousTags;
                        }
                        else if (SelectionTypeMap[mapKey] == PropertySelectionType.SingleSelectionType)
                        {
                            data[SelectionNameMap[mapKey]].Add("select", new Dictionary<string, string> { { "name", tag } });
                        }
                        else if (SelectionTypeMap[mapKey] == PropertySelectionType.StatusSelectionType)
                        {
                            data[SelectionNameMap[mapKey]].Add("status", new Dictionary<string, string> { { "name", tag } });
                        }
                        else if (SelectionTypeMap[mapKey] == PropertySelectionType.CheckBox)
                        {
                            data[SelectionNameMap[mapKey]].Add("checkbox", tag == "Check");
                        }
                        mapKey++;
                    }
                }

                if (filteredQueryEditing.ContainsKey("link"))
                {
                    data[UrlMap] = new Dictionary<string, object> { { "url", filteredQueryEditing["link"] } };
                }

                Dictionary<int, string> JObjectToDictionary(JObject jObject)
                {
                    Dictionary<int, string> dict = new Dictionary<int, string>();

                    foreach (var property in jObject.Properties())
                    {
                        if (int.TryParse(property.Name, out int key))
                        {
                            dict[key] = property.Value.ToString();
                        }
                        else
                        {
                            throw new FormatException($"Key '{property.Name}' is not a valid integer.");
                        }
                    }

                    return dict;
                }

                if (filteredQueryEditing.ContainsKey("Relations"))
                {
                    Dictionary<int, string> Projects = new Dictionary<int, string>();
                    if (filteredQueryEditing["Relations"] is Dictionary<int, string> _projects)
                    {
                        Projects = _projects;
                    }
                    else
                    {
                        Projects = JObjectToDictionary(filteredQueryEditing["Relations"] as JObject);
                    }
                    
                    void GetOldRelations ()
                    {
                        JObject pageProperties = _notionDataParser.RetrievePageJsonObjectById(pageId);
                        
                        foreach (var prop in (JObject)pageProperties["properties"])
                        {
                            if (ProjectName.ContainsValue(prop.Key.ToString()))
                            {   
                                if (!data.ContainsKey(prop.Key.ToString()))
                                {
                                    data[prop.Key.ToString()] = new Dictionary<string, object>();
                                }
                                
                                JArray relationIds = prop.Value["relation"] as JArray;
                                
                                foreach (var relationItem in relationIds)
                                {
                                    data[prop.Key.ToString()].Add("relation", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id", relationItem["id"].ToString() } } });
                                }
                                
                            }
                        }
                    }
                    
                    if (Mode == "Edit") {
                        try {
                        GetOldRelations ();
                        } catch { }
                    }
                    
                    foreach (var relation in ProjectName)
                    {   
                        if (true)
                        {
                            ProjectsId = LoadJsonData(GetRelationFilePath(relation.Value, filteredQueryEditing["databaseId"] as string));

                            var ProjectRelationID = ProjectsId.FirstOrDefault(_project => _project.Value[0].GetString() == Convert.ToString(Projects[relation.Key])).Key;

                            if (!data.ContainsKey(relation.Value))
                            {
                                data[relation.Value] = new Dictionary<string, object>();
                            }
                            if (data[relation.Value].Count >= 1)
                            {
                                if (data[relation.Value]["relation"] is List<Dictionary<string, object>> relationList)
                                {
                                    relationList.Add(new Dictionary<string, object> { { "id", ProjectRelationID } });
                                }
                            }
                            else
                            {
                                data[relation.Value].Add("relation", new List<Dictionary<string, object>> { new Dictionary<string, object> { { "id", ProjectRelationID } } });
                            }
                        }
                        else
                        {
                            Context.API.ShowMsg(filteredQueryEditing["Relations"].GetType().ToString(), "");
                        }
                    }
                    
                }
            }

            if (filteredQueryEditing.ContainsKey("content"))
            {
                if (filteredQueryEditing["content"] is List<List<string>> contentItem)
                {
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
                }
                return (data, children, DATABASE_ID);
            }
            else
            {
                children = null;
                return (data, children, DATABASE_ID);
            }
        }

        public async Task<HttpResponseMessage> CreatePage(Dictionary<string, object> filteredDataDict, Dictionary<string, List<Dictionary<string, object>>> children = null, string DatabaseId = null, bool open = false, bool retryFailedRequest = false, string datePropName = null, string urlPropName = null, Dictionary<int, string> relationPropName = null, string mainPayload = null)
        {
            Dictionary<string, object> payload = new Dictionary<string, object> ();
            
            try
            {
                if (retryFailedRequest)
                {
                    Context.API.LogWarn(nameof(Main), mainPayload, MethodBase.GetCurrentMethod().Name);
                    DateName = datePropName;
                    UrlMap = urlPropName;
                    ProjectName = relationPropName;
                    //SelectionTypeMap = selectionPropType;
                    //SelectionNameMap = selectionPropName;
                }
                var (data_return, children_return, DATABASE_ID) = FormatData(filteredDataDict);
                var data = data_return;
                children = children_return;

                using (HttpClient client = new HttpClient())
                {
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

                    string createUrl = "https://api.notion.com/v1/pages";
                    
                    payload["parent"] = new Dictionary<string, object> { { "database_id", DATABASE_ID } };
                    payload["properties"] = data;

                    if (children != null)
                    {
                        payload["children"] = children["children"];
                    }
                    string payloadJson = " ";
                    if (retryFailedRequest) {
                        payloadJson = mainPayload;
                    }
                    else {
                        
                        payloadJson = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        mainPayload = payloadJson;
                    }
                    Context.API.LogWarn(nameof(Main), mainPayload, MethodBase.GetCurrentMethod().Name);

                    StringContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
                    Console.WriteLine(payloadJson);

                    HttpResponseMessage response = await client.PostAsync(createUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                            JObject jsonObject = JObject.Parse(response.Content.ReadAsStringAsync().Result);
                            Dictionary<int, string> FilteredProjects;
                            if (filteredDataDict.ContainsKey("Relations") && filteredDataDict["Relations"] is Dictionary<int, string> _filteredProjects)
                            {
                                FilteredProjects = _filteredProjects;
                            }
                            else if (retryFailedRequest)
                            {
                                FilteredProjects = (filteredDataDict["Relations"] as JObject).ToObject<Dictionary<int, string>>();
                            }
                            else {
                                FilteredProjects = new Dictionary<int, string>();
                            }
                            var itemDatabaseObject = databaseId.FirstOrDefault(Database => Database.Key == filteredDataDict["databaseId"].ToString());
                            string DBName = itemDatabaseObject.Key;
                            string projectName = FilteredProjects.Count != 0 ?
                             FilteredProjects.Count == 1 ? $"<{FilteredProjects.First().Value}>" : $"<{FilteredProjects.Count} relations>"
                             : string.Empty;
                            string itemName = filteredDataDict.ContainsKey("Name") ? filteredDataDict["Name"].ToString() : string.Empty;
    
                            JsonArray jsonArray = new JsonArray
                            {
                                itemName,
                                FilteredProjects.Count != 0 ? FilteredProjects.First().Value : string.Empty ,
                                DBName,
                                "Images\\app.png"
                            };
    
                            searchResults[jsonObject["id"].ToString()] = JsonDocument.Parse(jsonArray.ToString()).RootElement;
    
                            string jsonString = System.Text.Json.JsonSerializer.Serialize(searchResults, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
    
    
                            File.WriteAllText(_settings.FullCachePath, jsonString);
    
                            if (_settings.RelationDatabasesIds.Contains(itemDatabaseObject.Value.GetProperty("id").GetString()))
                            {
                                JsonArray newRelationItem = new JsonArray
                                {
                                    itemName,
                                    "" ,
                                    "notion://www.notion.so/" + jsonObject["id"].ToString().Replace("-",""),
                                    "Images\\app.png",
                                    "",
                                    DBName
                                };
                                string targetRelationDatabasePath = Path.Combine(cacheDirectory, itemDatabaseObject.Value.GetProperty("id").GetString() + ".json");
                                ProjectsId = LoadJsonData(targetRelationDatabasePath);
    
                                ProjectsId[jsonObject["id"].ToString()] = JsonDocument.Parse(newRelationItem.ToString()).RootElement;
    
                                string relationItemJson = System.Text.Json.JsonSerializer.Serialize(ProjectsId, new JsonSerializerOptions
                                {
                                    WriteIndented = true
                                });
                                File.WriteAllText(targetRelationDatabasePath, relationItemJson);
                            }
                        

                        if (open)
                        {
                            Context.API.ShowMsg($"Opening the new item ({filteredDataDict["databaseId"]})",
                                            $"{itemName}\n{projectName}",
                                            iconPath: Context.CurrentPluginMetadata.IcoPath);
                            string created_PageID = jsonObject["id"].ToString().Replace("-", "");
                            string notionUrl = $"notion://www.notion.so/{created_PageID}";
                            OpenNotionPage(notionUrl);
                        }
                        else
                        {
                            Context.API.ShowMsg($"Adding a new item ({filteredDataDict["databaseId"]})",
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
            catch (Exception ex)
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                    Context.API.LogException(nameof(Main), $"Internet available ==>{IsInternetConnected()}", ex, MethodBase.GetCurrentMethod().Name);
                    HttpResponseMessage FakeRespone = new HttpResponseMessage();
                    FakeRespone.ReasonPhrase = "Bad Request";
                    return FakeRespone;
                }
                else
                {
                    if (_settings.FailedRequests)
                    {
                        
                        await _apiCacheManager.CacheFunction(nameof(CreatePage), new List<object> { filteredDataDict, null, DatabaseId, open, true, (retryFailedRequest ? datePropName : DateName), (retryFailedRequest ? urlPropName : UrlMap), (retryFailedRequest ? relationPropName : ProjectName), mainPayload });
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

        async Task<HttpResponseMessage> EditPageMainProperty(bool open, string pageId, Dictionary<string, object> filteredQueryEditing, List<string> fromContext = null, bool retryFailedRequest = false, string datePropName = null, string urlPropName = null, Dictionary<int, string> relationPropName = null, string mainPayload = null, string childPayload = null)
        {
            Dictionary<string, object> payload = new Dictionary<string, object>();
            try
            {
                if (retryFailedRequest)
                {
                    DateName = datePropName;
                    UrlMap = urlPropName;
                    ProjectName = relationPropName;
                }

                var (data, children, DatabaseId) = FormatData(filteredQueryEditing, Mode: "Edit", DbNameInCache: searchResults[pageId][2].GetString(), pageId:pageId);
                using (HttpClient client = new HttpClient())
                {
                    HttpResponseMessage response = null;
                    string EditUrl;
                    client.DefaultRequestHeaders.Add("Authorization", "Bearer " + _settings.InernalInegrationToken);
                    client.DefaultRequestHeaders.Add("Notion-Version", "2022-06-28");

                    if (children != null || (retryFailedRequest && !string.IsNullOrEmpty(childPayload)))
                    {
                        string childrenPayloadJson = string.Empty;
                        EditUrl = $"https://api.notion.com/v1/blocks/{pageId}/children";
                        if (retryFailedRequest) { 
                            childrenPayloadJson = childPayload;
                        }
                        else {
                            childrenPayloadJson = System.Text.Json.JsonSerializer.Serialize(children, new JsonSerializerOptions
                            {
                                WriteIndented = true
                            });
                            childPayload = childrenPayloadJson;
                        }
                        StringContent content = new StringContent(childrenPayloadJson, Encoding.UTF8, "application/json");
                        try {
                            response = await client.PatchAsync(EditUrl, content);
                        } 
                        catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException) { }

                    }

                    if (data.Count != 0 || (retryFailedRequest && !string.IsNullOrEmpty(mainPayload)) )
                    {
                            EditUrl = $"https://api.notion.com/v1/pages/{pageId}";
                            string payloadJson = string.Empty;
                        
                            if (retryFailedRequest) 
                            {
                                payloadJson = mainPayload;
                            } 
                            else { 
                                payload["properties"] = data;
                                payloadJson = System.Text.Json.JsonSerializer.Serialize( payload , new JsonSerializerOptions
                                {
                                    WriteIndented = true
                                });
                                mainPayload = payloadJson;
                            }
                            
                            StringContent content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                            response = await client.PatchAsync(EditUrl, content);

                        
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
                                var newTitle = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(filteredQueryEditing["Name"])).RootElement; ;
                                jsonArray[0] = newTitle;

                                if (_settings.RelationDatabases.Contains(searchResults[pageId][2].GetString()))
                                {
                                    var relatedPageArray = ProjectsId[pageId].EnumerateArray().ToList();
                                    relatedPageArray[0] = newTitle;
                                    string targetRelationDatabasePath = Path.Combine(cacheDirectory, _settings.RelationDatabasesIds[_settings.RelationDatabases.IndexOf(searchResults[pageId][2].GetString())] + ".json");
                                    ProjectsId = LoadJsonData(targetRelationDatabasePath);
                                    ProjectsId[pageId] = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(relatedPageArray)).RootElement;

                                    string newRelatedPageJson = System.Text.Json.JsonSerializer.Serialize(ProjectsId, new JsonSerializerOptions
                                    {
                                        WriteIndented = true
                                    });
                                    File.WriteAllText(targetRelationDatabasePath, newRelatedPageJson);

                                }
                            }
                            if (filteredQueryEditing.TryGetValue("Relations", out var _relations))
                            {
                                Dictionary<int, string> FilteredProjects;
                                if (retryFailedRequest ) {
                                    FilteredProjects = (_relations as JObject).ToObject<Dictionary<int, string>>();
                                } else {
                                    FilteredProjects = _relations as Dictionary<int, string>;
                                }
                                if (FilteredProjects.Count() > 0)
                                {
                                    string targetRelationDatabasePath = Path.Combine(cacheDirectory, _settings.RelationDatabasesIds[0] + ".json");
                                    ProjectsId = LoadJsonData(targetRelationDatabasePath);
                                    foreach (string relation in FilteredProjects.Values) {
                                        if (ProjectsId.Any(x => x.Value[0].GetString() == relation)) {
                                            jsonArray[1] = JsonDocument.Parse(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(relation)).RootElement;
                                            break;
                                        }
                                    }
                                }
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
                        String payload_debug = System.Text.Json.JsonSerializer.Serialize(payload, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });
                        Context.API.LogWarn(nameof(Main), payload_debug, MethodBase.GetCurrentMethod().Name);

                        return response;
                    }
                }
            }
            catch (Exception ex)
            {
                if (IsInternetConnected())
                {
                    Context.API.ShowMsgError($"Proccessing Error", "Unexpected Error While Proccesing Propeties.");
                    Context.API.LogException("Main", "Proccessing error", ex);
                    HttpResponseMessage FakeRespone = new HttpResponseMessage();
                    FakeRespone.ReasonPhrase = "Bad Request";
                    return FakeRespone;
                }
                else
                {
                    if (_settings.FailedRequests)
                    {
                        await _apiCacheManager.CacheFunction(nameof(EditPageMainProperty), new List<object> { open, pageId, filteredQueryEditing, fromContext, true, (retryFailedRequest ? datePropName : DateName), (retryFailedRequest ? urlPropName : UrlMap), (retryFailedRequest ? relationPropName : ProjectName), mainPayload, childPayload });
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
                if (jObject.Properties().Any())
                {
                    bool allKeysAreIntegers = jObject.Properties()
                                              .All(p => int.TryParse(p.Name, out _));
                    if (allKeysAreIntegers)
                    {
                        return jObject.ToObject<Dictionary<int, string>>();
                    }
                }
                else
                {
                    return jObject.ToObject<Dictionary<int, string>>();
                }

                return jObject.ToObject<Dictionary<string, object>>();
            }

            return arg;
        }

        // private object ConvertToExpectedType(object arg)
        // {
        //     if (arg is JObject jObject)
        //     {
        //         var dict = jObject.ToObject<Dictionary<string, string>>();

        //         if (dict != null && IsInt32StringDictionary(dict))
        //         {
        //             var intDict = new Dictionary<int, string>();
        //             foreach (var kvp in dict)
        //             {
        //                 if (int.TryParse(kvp.Key, out int key))
        //                 {
        //                     intDict[key] = kvp.Value;
        //                 }
        //                 else
        //                 {
        //                     // If any key is not an integer, return the original JObject
        //                     return arg;
        //                 }
        //             }
        //             return intDict;
        //         }
        //     }

        //     return arg;
        // }
        // private bool IsInt32StringDictionary(Dictionary<string, string> dict)
        // {
        //     foreach (var key in dict.Keys)
        //     {
        //         if (!int.TryParse(key, out _))
        //         {
        //             return false;
        //         }
        //     }
        //     return true;
        // }
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
            string cacheJson = JsonConvert.SerializeObject(cachedFunctions, Formatting.Indented);
            await File.WriteAllTextAsync(Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "FailedRequests.json"), cacheJson);
        }
        public async Task<List<CachedFunction>> GetCachedFunctions()
        {
            try
            {
                string cacheJson = await File.ReadAllTextAsync(Path.Combine(_context.CurrentPluginMetadata.PluginDirectory, "FailedRequests.json"));
                return JsonConvert.DeserializeObject<List<CachedFunction>>(cacheJson);
            }
            catch
            {
                return new List<CachedFunction>();
            }
        }
    }
    enum PropertySelectionType
    {
        MultiSelectionType,
        SingleSelectionType,
        StatusSelectionType,
        CheckBox
    }
}
