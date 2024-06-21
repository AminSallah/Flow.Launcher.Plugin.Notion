using Modern = ModernWpf.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flow.Launcher.Plugin.Notion.ViewModels;
using System.IO;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using System;
using System.Threading.Tasks;
using System.Threading;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

namespace Flow.Launcher.Plugin.Notion.Views
{

    public partial class NotionSettings : UserControl
    {
        public CustomPayload SelectedCustomBrowser;
        PluginInitContext Context;
        SettingsViewModel _viewModel;
        private readonly Settings _settings;
        private NotionDataParser _dataParser;
        private SemaphoreSlim semaphoreSlim = new SemaphoreSlim(1, 1);


        public NotionSettings(PluginInitContext context, SettingsViewModel viewModel, NotionDataParser parser)
        {
            this.InitializeComponent();
            Context = context;
            _viewModel = viewModel;
            _settings = viewModel.Settings;
            _settings.UpdateSearchFiltersOptions();
            DataContext = viewModel;

            if (viewModel.Databases != null && viewModel.Databases.Count != 0)
            {
                List<string> TempDatababaseList = viewModel.Databases.ToList();

                // Uncomment to assign multiple relation pages simultaneously + change listbox SelectionMode to multiple
                foreach (var database in TempDatababaseList)
                {
                    listBox.SelectedItems.Add(database);
                }

                listBox.SelectedItem = TempDatababaseList[0];
            }


            this._dataParser = parser;
        }


        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_settings.SelectedPayload != null)
            {
                var selected = _settings.SelectedPayload;
                new CustomPayloadWindow(_settings, selected, Action.Edit, Context).ShowDialog();
            }
        }


        private async void ClearFailedRequests(object sender, RoutedEventArgs e)
        {
            var input = (UIElement)sender;
            var temp = input.IsEnabled;
            input.IsEnabled = false;
            Main._apiCacheManager.cachedFunctions.Clear();
            await Main._apiCacheManager.SaveCacheToFile();
            _viewModel.CachedFailedRequests = Main._apiCacheManager.cachedFunctions.Count;
        }

        private async void ClearHiddenItems(object sender, RoutedEventArgs e)
        {
            var input = (UIElement)sender;
            var temp = input.IsEnabled;
            input.IsEnabled = false;
            Main.HiddenItems.Clear();
            File.WriteAllLines(Main.HiddenItemsPath, Main.HiddenItems);
            _viewModel.HiddenItemsCount = Main.HiddenItems.Count;
        }

        private void OpenNotebookIconsFolder(object sender, RoutedEventArgs e)
        {
        }

        private void DropDownButton_Click(object sender, RoutedEventArgs e)
        {
            if (popup.IsOpen)
            {
                popup.IsOpen = false;
            }
            else
            {
                popup.IsOpen = true;
            }
        }
        private void ListBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

            int selectedItemCount = listBox.SelectedItems.Count;
            if (selectedItemCount == 1)
            {
                dropDownButton.Content = listBox.SelectedItems[0];
            }
            else if (selectedItemCount == 0)
            {
                dropDownButton.Content = string.Empty;
            }
            else
            {
                dropDownButton.Content = listBox.SelectedItems[0] + $" +{selectedItemCount - 1} DBs";
            }

            if (listBox.SelectedItems is IList selectedItems)
            {
                if (_settings.RelationDatabases == null)
                {
                    _settings.RelationDatabases = new List<string>();
                }

                List<string> itemsToAdd = new List<string>();
                List<string> itemsIdsToAdd = new List<string>();
                foreach (var selectedItem in selectedItems)
                {
                    if (selectedItem is string database)
                    {
                        itemsToAdd.Add(database);
                        itemsIdsToAdd.Add(Main.databaseId[database].GetProperty("id").GetString());
                    }
                }
                _viewModel.Databases.Clear();
                _viewModel.DatabasesIds.Clear();
                _viewModel.Databases.AddRange(itemsToAdd);
                _viewModel.DatabasesIds.AddRange(itemsIdsToAdd);
            }
            if (listBox.SelectionMode == System.Windows.Controls.SelectionMode.Single)
            {
                popup.IsOpen = false;
                return;
            }
        }

        private void OpenSectionIconsFolder(object sender, RoutedEventArgs e)
        {
        }

        private void EditButton_Click(object sender, RoutedEventArgs e)
        {
            if (_settings.SelectedPayload != null)
            {
                var selected = _settings.SelectedPayload;
                new CustomPayloadWindow(_settings, selected, Action.Edit, Context).ShowDialog();
            }
        }



        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomPayloadListView.SelectedItem is CustomPayload selectedCustomBrowser)
            {
                if (selectedCustomBrowser.CacheType != 0)
                {
                    File.Delete(Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{selectedCustomBrowser.Title}.json"));
                }
                _settings.Filters.Remove(selectedCustomBrowser);
                _settings.UpdateSearchFiltersOptions();


            }
        }


        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            new CustomPayloadWindow(_settings, new CustomPayload(), Action.Add, Context).ShowDialog();
        }



        private void ListView_MouseUp(object sender, MouseButtonEventArgs e)
        {

        }


        private async void ListBox_LostFocus(object sender, EventArgs e)
        {
            if (_settings.RelationDatabases.Count != 0 && !popup.IsOpen)
            {
                try
                {
                    await semaphoreSlim.WaitAsync();

                    await Task.Run(async () =>
                    {
                        try
                        {
                            string msg;
                            if (_settings.RelationDatabases.Count == 1)
                            {
                                msg = $"{_settings.RelationDatabases[0]} database successfully set as relation database, Please wait while querying it.";

                            }
                            else
                            {
                                msg = $"{_settings.RelationDatabases.Count} databases successfully set as relation databases, Please wait while querying them.";

                            }
                            Context.API.ShowMsg("Relation database", msg);
                            Main.databaseId = await _dataParser.DatabaseCache();
                            foreach (string databaseId in _settings.RelationDatabasesIds)
                            {
                                await Task.Run(async () =>
                                {
                                    await this._dataParser.QueryDB(databaseId, null, Path.Combine(Main.cacheDirectory, $"{databaseId}.json"));
                                });
                            }
                            if (_settings.RelationDatabases.Count == 1)
                            {
                                msg = $"{_settings.RelationDatabases[0]} database successfully queryied, now you can use there pages for relation properties.";

                            }
                            else
                            {
                                msg = $"{_settings.RelationDatabases.Count} databases successfully queryied, now you can use there pages for relation properties.";

                            }
                            Context.API.ShowMsg("Query Relation database", msg);
                        }
                        finally
                        {
                            semaphoreSlim.Release();
                        }
                    });

                }
                catch (Exception ex)
                {
                    Context.API.LogException(nameof(NotionSettings), "Error while selection changed of relation ComboBox", ex);
                }
            }
        }


        private void Database_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {


        }

        private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CustomPayloadListView.SelectedItem != null)
            {
                _viewModel.NotSelected = true;
            }
            else
            {
                _viewModel.NotSelected = false;
            }
        }
    }


}
