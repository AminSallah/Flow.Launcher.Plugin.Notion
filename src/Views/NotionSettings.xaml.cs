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

        public NotionSettings(PluginInitContext context, SettingsViewModel viewModel)
		{
			this.InitializeComponent();
            Context = context;
            _viewModel = viewModel;
            _settings = viewModel.Settings;
            DataContext = viewModel;
            this._dataParser = new NotionDataParser(Context, _settings);
        }


        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_settings.SelectedPayload != null)
            {
                var selected = _settings.SelectedPayload;
                new CustomPayloadWindow(_settings, selected, Action.Edit, Context).ShowDialog();
            }
        }


        

        private async void ClearCachedIcons(object sender, RoutedEventArgs e)
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
                if (selectedCustomBrowser.Cachable)
                {
                    File.Delete(Path.Combine(Context.CurrentPluginMetadata.PluginDirectory, "cache", $"{selectedCustomBrowser.Title}.json"));
                }
                _settings.Filters.Remove(selectedCustomBrowser);
                

            }
        }


        private void AddButton_Click(object sender, RoutedEventArgs e)
        {
            new CustomPayloadWindow(_settings, new CustomPayload(), Action.Add, Context).ShowDialog();
        }



        private void ListView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            
        }


        private void RelationComboBox_Loaded(object sender, RoutedEventArgs e)
        {
            ((ComboBox)sender).SelectionChanged += Relation_SelectionChanged;
        }

        private async void Relation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                await semaphoreSlim.WaitAsync();

                await Task.Run(async () =>
                {
                    try
                    {
                    Context.API.ShowMsg("Relation database", $"{_settings.RelationDatabase} successfully set as relation database please wait while querying it for you.");
                    _settings.RelationDatabaseId = Main.databaseId[_settings.RelationDatabase].GetProperty("id").GetString();
                    Main.databaseId = await _dataParser.DatabaseCache();
                    Main.ProjectsId = await _dataParser.QueryDB(_settings.RelationDatabaseId, null, _settings.RelationCachePath);
                    Context.API.ShowMsg("Query Relation database", $"{_settings.RelationDatabase} successfully queryied, now you can use its pages for relation properties");
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

        private void UserControl_Unloaded(object sender, RoutedEventArgs e)
        {
            if (RelationComboBox != null)
            {
                RelationComboBox.Loaded -= RelationComboBox_Loaded;
                RelationComboBox.SelectionChanged -= Relation_SelectionChanged;
            }
        }


        private void Database_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
           

        }
    }


}
