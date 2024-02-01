using Modern = ModernWpf.Controls;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Flow.Launcher.Plugin.Notion.ViewModels;
using System.IO;

namespace Flow.Launcher.Plugin.Notion.Views
{
	
	public partial class NotionSettings : UserControl
	{
        public CustomPayload SelectedCustomBrowser;


        PluginInitContext Context;

       
        private readonly Settings _settings;

        public NotionSettings(PluginInitContext context, SettingsViewModel viewModel)
		{
			this.InitializeComponent();
            Context = context;
            _settings = viewModel.Settings;
            DataContext = viewModel;



        }

        



        private void ListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                var listView = (ListView)sender;

                var hit = listView.InputHitTest(e.GetPosition(listView));
                /*if (hit is FrameworkElement fe && fe.DataContext is KeywordViewModel selectedKeyword)
                {
                    listView.SelectedItem = selectedKeyword;
                    EditButton_Click(sender, e);
                }*/
            }
        }




        private async void ClearCachedIcons(object sender, RoutedEventArgs e)
        {
            var input = (UIElement)sender;
            var temp = input.IsEnabled;
            input.IsEnabled = false;

            


            
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

            /*if (_viewModel.SelectedKeyword == null)
            {
                MessageBox.Show("Please select a keyword");
            }
            else
            {
                new ChangeKeywordWindow(_viewModel).ShowDialog();
            }*/
        }



        private void ListView_MouseUp(object sender, MouseButtonEventArgs e)
        {
            
        }
    }


}
