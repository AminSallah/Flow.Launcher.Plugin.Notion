using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
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

        List<string> DatabaseReserve;

        public CustomPayloadWindow(Settings settings, CustomPayload customPayload, Action action, PluginInitContext _context)
        {
            context = _context;
            InitializeComponent();
            viewModel = new CustomPayloadViewModel(settings);
            _settings = settings;
            currentCustomBrowser = customPayload;
            viewModel.listbox = listBox;
            viewModel.FilterTitle = customPayload.Title;
            viewModel.FilterSubTitle = customPayload.SubTitle;
            viewModel.Databases = customPayload.Databases;
            viewModel.Json = customPayload.Json;
            viewModel.JsonType = customPayload.JsonType;
            viewModel.CacheType = customPayload.CacheType;
            viewModel.Status = customPayload.Enabled;
            viewModel.IcoPath = customPayload.IcoPath;
            viewModel.Timeout = customPayload.Timeout;
            viewModel.Count = customPayload.Count;
            DatabaseReserve = viewModel.Databases.ToList();
            _action = action;


            DataContext = viewModel;
            listBox.ItemsSource = _settings.DatabaseSelectionOptions;
            if (viewModel.Databases != null && viewModel.Databases.Count != 0)
            {
                List<string> TempDatababaseList = viewModel.Databases.ToList();

                if (viewModel.ListBoxSelectionMode == System.Windows.Controls.SelectionMode.Multiple)
                {
                    foreach (var database in TempDatababaseList)
                    {
                        listBox.SelectedItems.Add(database);
                    }
                }
                else
                {
                    listBox.SelectedItem = TempDatababaseList[0];
                }
            }
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
                dropDownButton.Content = $"{selectedItemCount} databases";
            }
            
            if (listBox.SelectedItems is IList selectedItems)
            {
                if (viewModel.Databases == null)
                {
                    viewModel.Databases = new List<string>();
                }

                List<string> itemsToAdd = new List<string>();
                foreach (var selectedItem in selectedItems)
                {
                    if (selectedItem is string database)
                    {
                        itemsToAdd.Add(database);
                    }
                }
                DatabaseReserve.Clear();
                DatabaseReserve.AddRange(itemsToAdd);
            }
            if (viewModel.ListBoxSelectionMode == System.Windows.Controls.SelectionMode.Single)
            {
                popup.IsOpen = false;
                return;
            }
        }



        private void WindowLoaded(object sender, RoutedEventArgs e)
        {
            TextBox_Title.Focus();
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private async void OnSelectIconClick(object sender, RoutedEventArgs e)
        {
            const string filter = "Image files (*.jpg, *.jpeg, *.gif, *.png, *.bmp, *.svg) |*.jpg; *.jpeg; *.gif; *.png; *.bmp; *.svg";
            var dialog = new Microsoft.Win32.OpenFileDialog { InitialDirectory = Main.CustomImagesDirectory, Filter = filter };
            var result = dialog.ShowDialog();
            if (result == true)
            {
                selectedNewIconImageFullPath = dialog.FileName;
                if (!string.IsNullOrEmpty(selectedNewIconImageFullPath))
                {
                    viewModel.IcoPath = CopyNewImageToUserDataDirectoryIfRequired(fullpathToSelectedImage: selectedNewIconImageFullPath);
                }
            }
        }

        public string CopyNewImageToUserDataDirectoryIfRequired(
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
                    System.Windows.MessageBox.Show(string.Format("Copying the selected image file to {0} has failed, changes will now be reverted", destinationFileNameFullPath));
                }
            }

            return destinationFileNameFullPath;
        }

        private void Button_ChangeKeyword(object sender, RoutedEventArgs e)
        {
            viewModel.Databases = DatabaseReserve.ToList();
            if (_action == Action.Add)
            {
                if (viewModel.NewCustomPayload(out string errorMessage))
                {
                    Close();
                }
                else
                {
                    System.Windows.MessageBox.Show(this, errorMessage, "Notion Filters", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                if (!_settings.Filters.Any(Filter => Filter.Title.ToLower().Trim() == viewModel.FilterTitle.ToLower().Trim()) || currentCustomBrowser.Title == viewModel.FilterTitle || !viewModel.FilterSettingsVisibility)
                {
                    if (viewModel.Databases.Count != 0 && !string.IsNullOrEmpty(viewModel.Databases[0])
                        || viewModel.Json == """{"archived" : true}""")
                    {
                        currentCustomBrowser.Title = viewModel.FilterTitle;
                        currentCustomBrowser.SubTitle = viewModel.FilterSubTitle;
                        currentCustomBrowser.Json = viewModel.Json;
                        currentCustomBrowser.JsonType = viewModel.JsonType;
                        currentCustomBrowser.CacheType = viewModel.CacheType;
                        currentCustomBrowser.Enabled = viewModel.Status;
                        currentCustomBrowser.IcoPath = viewModel.IcoPath;
                        currentCustomBrowser.Databases = viewModel.Databases;
                        currentCustomBrowser.Timeout = viewModel.Timeout;
                        currentCustomBrowser.Count = viewModel.Count;
                        Close();
                    }
                    else
                    {
                        System.Windows.MessageBox.Show(this, "Databse cannot be empty", "Notion Filters", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
                else
                {
                    System.Windows.MessageBox.Show(this, "Title cannot be duplicated", "Notion Filters", MessageBoxButton.OK, MessageBoxImage.Error);
                }
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
    public enum CacheTypes
    {
        Disabled,
        BuildAndWait,
        BuildWithTimeout,
        BuildWithoutWaiting
    }
}
