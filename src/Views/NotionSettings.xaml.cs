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

namespace Flow.Launcher.Plugin.Notion.Views
{
	
	public partial class NotionSettings : UserControl
	{
        public CustomPayload SelectedCustomBrowser;
        PluginInitContext Context;
        SettingsViewModel _viewModel;
        private readonly Settings _settings;


        public NotionSettings(PluginInitContext context, SettingsViewModel viewModel)
		{
			this.InitializeComponent();
            Context = context;
            _viewModel = viewModel;
            _settings = viewModel.Settings;
            DataContext = viewModel;
        }


        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (_settings.SelectedSearchSource != null)
            {
                var selected = _settings.SelectedSearchSource;
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
            if (_settings.SelectedSearchSource != null)
            {
                var selected = _settings.SelectedSearchSource;
                new CustomPayloadWindow(_settings, selected, Action.Edit, Context).ShowDialog();
            }
        }



        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            if (CustomBrowsers.SelectedItem is CustomPayload selectedCustomBrowser)
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


        private void Relation_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            
        }
    }


}
