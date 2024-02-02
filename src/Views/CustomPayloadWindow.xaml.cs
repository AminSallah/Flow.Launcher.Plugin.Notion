using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media.Imaging;
using Flow.Launcher.Plugin.Notion.ViewModels;

using Microsoft.Win32;

namespace Flow.Launcher.Plugin.Notion.Views
{
    public partial class CustomPayloadWindow
    {
        private readonly CustomPayloadViewModel viewModel;
        private Action _action;
        private Settings _settings;
        private string selectedNewIconImageFullPath;

        private PluginInitContext context;
        

        private CustomPayload currentCustomBrowser;

        public CustomPayloadWindow(Settings settings, CustomPayload customPayload, Action action, PluginInitContext _context)
        {
            context = _context;
            InitializeComponent();
            viewModel = new CustomPayloadViewModel(settings);
            _settings = settings;
            currentCustomBrowser = customPayload;
            // Initialize properties with values from the provided CustomBrowser instance
            viewModel.FilterTitle = customPayload.Title;
            viewModel.FilterSubTitle = customPayload.SubTitle;
            viewModel.Database = customPayload.Database;
            viewModel.Json = customPayload.Json;
            viewModel.JsonType = customPayload.JsonType;
            viewModel.Cachable = customPayload.Cachable;
            viewModel.Status = customPayload.Enabled;
            viewModel.IcoPath = customPayload.IcoPath;
            _action = action;

            DataContext = viewModel;
        }

        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            TextBox_Title.Focus();
            try
            {
                BitmapImage bitmapImage = new BitmapImage(new Uri(viewModel.IcoPath));
                imgPreviewIcon.Source = bitmapImage;

            }
            catch
            {
                BitmapImage bitmapImage = new BitmapImage(new Uri(context.CurrentPluginMetadata.IcoPath));
                imgPreviewIcon.Source = bitmapImage;

            }


        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }


        


        private async void OnSelectIconClick(object sender, RoutedEventArgs e)
        {
            const string filter = "Image files (*.jpg, *.jpeg, *.gif, *.png, *.bmp, *.svg) |*.jpg; *.jpeg; *.gif; *.png; *.bmp; *.svg";
            var dialog = new OpenFileDialog { InitialDirectory = Main.CustomImagesDirectory, Filter = filter };

            var result = dialog.ShowDialog();
            if (result == true)
            {
                selectedNewIconImageFullPath = dialog.FileName;


                if (!string.IsNullOrEmpty(selectedNewIconImageFullPath))
                {
                    /*if (viewModel.ShouldProvideHint(selectedNewIconImageFullPath))
                        MessageBox.Show(_api.GetTranslation("flowlauncher_plugin_websearch_iconpath_hint"));*/
                    CopyNewImageToUserDataDirectoryIfRequired(fullpathToSelectedImage: selectedNewIconImageFullPath);
                    viewModel.IcoPath = selectedNewIconImageFullPath;
                    BitmapImage bitmapImage = new BitmapImage(new Uri(selectedNewIconImageFullPath));

                    imgPreviewIcon.Source = bitmapImage;
                    //imgPreviewIcon.Source = await viewModel.LoadPreviewIconAsync(selectedNewIconImageFullPath);
                }
            }
        }


        public void CopyNewImageToUserDataDirectoryIfRequired(
            string fullpathToSelectedImage)
        {
            var destinationFileNameFullPath = Path.Combine(Main.CustomImagesDirectory, Path.GetFileName(fullpathToSelectedImage));

            var parentDirectorySelectedImg = Directory.GetParent(fullpathToSelectedImage).ToString();

            if (parentDirectorySelectedImg != Main.CustomImagesDirectory )
            {
                try
                {
                    File.Copy(fullpathToSelectedImage, destinationFileNameFullPath);
                }
                catch (Exception)
                {

                    //throw;

                MessageBox.Show(string.Format("Copying the selected image file to {0} has failed, changes will now be reverted", destinationFileNameFullPath));
                //UpdateIconAttributes(selectedSearchSource, fullPathToOriginalImage);

                }
            }
        }



        private void Button_ChangeKeyword(object sender, RoutedEventArgs e)
        {
            if (_action == Action.Add)
            {
                if (viewModel.ChangeKeyword(out string errorMessage))
                {
                    Close();
                }
                else
                {
                    MessageBox.Show(this, errorMessage, "Notion Filters", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                if (!_settings.Filters.Any(Filter => Filter.Title.ToLower().Trim() == viewModel.FilterTitle.ToLower().Trim()) || currentCustomBrowser.Title == viewModel.FilterTitle)
                {
                    currentCustomBrowser.Title = viewModel.FilterTitle;
                    currentCustomBrowser.SubTitle = viewModel.FilterSubTitle;
                    currentCustomBrowser.Database = viewModel.Database;
                    currentCustomBrowser.Json = viewModel.Json;
                    currentCustomBrowser.JsonType = viewModel.JsonType;
                    currentCustomBrowser.Cachable = viewModel.Cachable;
                    currentCustomBrowser.Enabled = viewModel.Status;
                    currentCustomBrowser.IcoPath = viewModel.IcoPath;
                    Close();

                }
                else
                {
                    MessageBox.Show(this, "Title cannot be duplicated", "Notion Filters", MessageBoxButton.OK, MessageBoxImage.Error);

                }


                /*if (viewModel.EditExistingFilter(out string errorMessage))
                {
                }
                else
                {
                    MessageBox.Show(this, errorMessage, "Invalid Keyword", MessageBoxButton.OK, MessageBoxImage.Error);
                }*/
            }
        }

        
        private void ComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {

        }
    }


    public enum Action
    {
        Add,
        Edit
    }

    public enum JsonType
    {
        Filter,
        Property
    }
}
