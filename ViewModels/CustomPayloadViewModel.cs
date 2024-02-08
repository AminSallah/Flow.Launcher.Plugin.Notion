﻿using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using System;
using Flow.Launcher.Plugin.Notion.Views;

namespace Flow.Launcher.Plugin.Notion.ViewModels
{
    public class CustomPayloadViewModel : Model
    {
        private readonly PluginInitContext context;
        private string filterTitle;
        private string filterSubTitle;
        private string database;
        private string json;
        private string icoPath;
        private bool cachable;
        private bool status;
        private JsonType jsonType;

        public string FilterTitle { get => filterTitle; set => SetProperty(ref filterTitle, value); }
        public string FilterSubTitle { get => filterSubTitle; set => SetProperty(ref filterSubTitle, value); }
        public string Database { get => database; set => SetProperty(ref database, value); }
        public string Json { get => json; set => SetProperty(ref json, value); }
        public string IcoPath { get => icoPath; set => SetProperty(ref icoPath, value); }
        public bool Cachable { get => cachable; set => SetProperty(ref cachable, value); }
        public bool Status { get => status; set => SetProperty(ref status, value); }

        public Settings Settings { get; init; }
        public CustomPayloadViewModel(Settings settings)
        {
            this.Settings = settings;
        }

        private ObservableCollection<JsonType> _typeOptions = new ObservableCollection<JsonType> { JsonType.Filter, JsonType.Property };
        public ObservableCollection<JsonType> TypeOptions
        {
            get => _typeOptions;
            set => SetProperty(ref _typeOptions, value);
        }


        private bool _filterSettingsVisibility = true;
        public bool FilterSettingsVisibility
        {
            get => _filterSettingsVisibility;
            set => SetProperty(ref _filterSettingsVisibility, value);
        }


        private void UpdateVisibility()
        {
            FilterSettingsVisibility = JsonType == JsonType.Filter;
        }

        public JsonType JsonType
        {
            get => jsonType;
            set
            {
                SetProperty(ref jsonType, value);
                UpdateVisibility();
            }
        }

        public bool NewCustomPayload(out string errorMessage)
        {
            errorMessage = null;
            if (string.IsNullOrWhiteSpace(FilterTitle))
            {
                errorMessage = "The Title cannot be empty.";
                return false;
            }
            if (Settings.Filters.Any(Filter => Filter.Title.ToLower().Trim() == FilterTitle.ToLower().Trim() ))
            {
                errorMessage = "The Title cannot be duplicated.";
                return false;
            }
            if (FilterSettingsVisibility && string.IsNullOrEmpty(Database))
            {
                errorMessage = "The Database cannot be empty.";
                return false;
            }

            if(!FilterSettingsVisibility) 
                Database = string.Empty;


            var Filter = new CustomPayload
            {
                Title = FilterTitle,
                SubTitle = FilterSubTitle,
                Database = Database,
                JsonType= JsonType,
                Json = Json,
                Cachable = Cachable,
                Enabled = Status,
                IcoPath = IcoPath,
            };

            Settings.Filters.Add(Filter);
           return true;

        }
        
    }

}