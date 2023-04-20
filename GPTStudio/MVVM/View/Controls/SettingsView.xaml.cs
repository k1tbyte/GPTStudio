﻿using GPTStudio.Infrastructure;
using System.Windows.Controls;

namespace GPTStudio.MVVM.View.Controls
{
    public partial class SettingsView : Grid
    {
        internal static Infrastructure.Models.Properties Properties => Config.Properties;
        public SettingsView()
        {
            InitializeComponent();
            DataContext = new ViewModels.SettingsViewModel();
            Loaded += (sender, e) => Config.NeedToUpdate = true;
        }
    }
}
