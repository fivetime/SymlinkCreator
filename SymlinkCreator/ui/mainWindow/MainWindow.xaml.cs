using Microsoft.WindowsAPICodePack.Dialogs;
using SymlinkCreator.core;
using SymlinkCreator.i18n;
using SymlinkCreator.ui.aboutWindow;
using SymlinkCreator.ui.utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using DataFormats = System.Windows.DataFormats;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;


namespace SymlinkCreator.ui.mainWindow
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region constructor

        public MainWindow()
        {
            InitializeComponent();
            this.Loaded += MainWindow_Loaded;

            // 订阅语言切换事件，切换时重新刷新所有 UI 文本
            LocalizationManager.LanguageChanged += (s, e) => RefreshUiText();

            if (IsRunningAsAdmin())
            {
                MessageBox.Show(
                    LocalizationManager.Get("MsgWarningAdmin", this.Title),
                    LocalizationManager.Get("MsgWarningAdminTitle"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        #endregion


        #region fields

        private string _previouslySelectedDestinationFolderPath = "";

        #endregion


        #region window event handles

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            this.DataContext = new MainWindowViewModel();
            RefreshUiText();
        }

        protected override void OnSourceInitialized(EventArgs eventArgs)
        {
            WindowMaximizeButton.DisableMaximizeButton(this);
            this.CreateSymlinksButtonImage.Source = NativeAdminShieldIcon.GetNativeShieldIcon();
            base.OnSourceInitialized(eventArgs);
        }

        #endregion


        #region localization

        // 简写辅助，减少代码量
        private static string L(string key) => LocalizationManager.Get(key);

        /// <summary>
        /// 刷新窗口所有 UI 文字（语言切换后调用）。
        /// </summary>
        private void RefreshUiText()
        {
            // Labels
            SourceListLabel.Text = L("SourceListLabel");
            DestinationPathLabel.Text = L("DestinationPathLabel");

            // Buttons
            AddFilesButton.Content = L("AddFilesButton");
            AddFilesButton.ToolTip = L("AddFilesButtonTooltip");
            AddFoldersButton.Content = L("AddFoldersButton");
            AddFoldersButton.ToolTip = L("AddFoldersButtonTooltip");
            RemoveSelectedButton.Content = L("RemoveSelectedButton");
            RemoveSelectedButton.ToolTip = L("RemoveSelectedButtonTooltip");
            ClearListButton.Content = L("ClearListButton");
            ClearListButton.ToolTip = L("ClearListButtonTooltip");
            BrowseDestinationButton.Content = L("BrowseButton");
            BrowseIconButton.Content = L("BrowseButton");
            AboutButton.Content = L("AboutButton");
            CreateSymlinksButtonText.Text = L("CreateSymlinksButton");

            // CheckBoxes
            UseRelativePathCheckbox.Content = L("UseRelativePathCheckbox");
            UseRelativePathCheckbox.ToolTip = L("UseRelativePathTooltip");
            RetainScriptCheckbox.Content = L("RetainScriptCheckbox");
            RetainScriptCheckbox.ToolTip = L("RetainScriptTooltip");
            HideSuccessDialogCheckbox.Content = L("HideSuccessDialogCheckbox");
            HideSuccessDialogCheckbox.ToolTip = L("HideSuccessDialogTooltip");
            PinToQuickAccessCheckbox.Content = L("PinToQuickAccessCheckbox");
            PinToQuickAccessCheckbox.ToolTip = L("PinToQuickAccessTooltip");

            // Custom name / icon labels
            CustomNameLabel.Text = L("CustomNameLabel");
            CustomNameTextBox.ToolTip = L("CustomNameTooltip");
            CustomIconLabel.Text = L("CustomIconLabel");
            CustomIconTextBox.ToolTip = L("CustomIconTooltip");

            // Language selector label
            LanguageSelectorLabel.Text = L("LanguageLabel");

            // Language ComboBox items
            RefreshLanguageComboBox();
        }

        private void RefreshLanguageComboBox()
        {
            string currentLang = LocalizationManager.CurrentLanguage;
            LanguageComboBox.ItemsSource = LocalizationManager.SupportedLanguages
                .Select(l => new LanguageItem { Code = l.Code, DisplayName = l.DisplayName })
                .ToList();
            LanguageComboBox.DisplayMemberPath = "DisplayName";
            LanguageComboBox.SelectedItem = ((List<LanguageItem>)LanguageComboBox.ItemsSource)
                .FirstOrDefault(l => l.Code == currentLang);
        }

        #endregion


        #region control event handles

        private void LanguageComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (LanguageComboBox.SelectedItem is LanguageItem selected &&
                selected.Code != LocalizationManager.CurrentLanguage)
            {
                LocalizationManager.ApplyLanguage(selected.Code);
            }
        }

        private void AddFilesButton_OnClick(object sender, RoutedEventArgs e)
        {
            OpenFileDialog fileDialog = new OpenFileDialog
            {
                Multiselect = true
            };

            if (fileDialog.ShowDialog() == true)
            {
                AddToSourceFileOrFolderList(fileDialog.FileNames);
            }
        }

        private void AddFoldersButton_OnClick(object sender, RoutedEventArgs e)
        {
            CommonOpenFileDialog folderBrowserDialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                Multiselect = true
            };

            if (folderBrowserDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                AddToSourceFileOrFolderList(folderBrowserDialog.FileNames);
            }
        }

        private void DestinationPathBrowseButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            CommonOpenFileDialog folderBrowserDialog = new CommonOpenFileDialog
            {
                IsFolderPicker = true,
                InitialDirectory = _previouslySelectedDestinationFolderPath
            };

            if (folderBrowserDialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                mainWindowViewModel.DestinationPath = folderBrowserDialog.FileName;
                _previouslySelectedDestinationFolderPath = folderBrowserDialog.FileName;
            }
        }

        private void RemoveSelectedButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            List<string> selectedFileOrFolderList = SourceFileOrFolderListView.SelectedItems.Cast<string>().ToList();
            foreach (string selectedItem in selectedFileOrFolderList)
            {
                mainWindowViewModel.FileOrFolderList.Remove(selectedItem);
            }
        }

        private void ClearListButton_OnClick(object sender, RoutedEventArgs e)
        {
            MainWindowViewModel mainWindowViewModel = this.DataContext as MainWindowViewModel;
            mainWindowViewModel?.FileOrFolderList.Clear();
        }

        private void SourceFileOrFolderListView_OnDrop(object sender, DragEventArgs e)
        {
            string[] droppedFileOrFolderList = GetDroppedFileOrFolderList(e);
            if (droppedFileOrFolderList != null)
            {
                AddToSourceFileOrFolderList(droppedFileOrFolderList);
            }
        }

        private void DestinationPathTextBox_OnDrop(object sender, DragEventArgs e)
        {
            string[] pathList = GetDroppedFileOrFolderList(e);
            if (pathList != null)
            {
                string droppedDestinationPath = pathList[0];
                AssignDestinationPath(droppedDestinationPath);
                e.Handled = true;
            }
        }

        private void DestinationPathTextBox_OnPreviewDragOver(object sender, DragEventArgs e)
        {
            string[] pathList = GetDroppedFileOrFolderList(e);
            if (pathList != null)
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void CreateSymlinksButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            if (mainWindowViewModel.FileOrFolderList.Count == 0)
            {
                MessageBox.Show(this, L("MsgNoSourceFiles"), L("MsgError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(mainWindowViewModel.DestinationPath))
            {
                MessageBox.Show(this, L("MsgDestinationEmpty"), L("MsgError"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            mainWindowViewModel.DestinationPath = SanitizePath(mainWindowViewModel.DestinationPath);

            SymlinkAgent symlinkAgent = new SymlinkAgent(
                mainWindowViewModel.FileOrFolderList,
                mainWindowViewModel.DestinationPath,
                mainWindowViewModel.ShouldUseRelativePath,
                mainWindowViewModel.ShouldRetainScriptFile,
                mainWindowViewModel.CustomSymlinkName,
                mainWindowViewModel.CustomIconPath,
                mainWindowViewModel.ShouldPinToQuickAccess);

            try
            {
                symlinkAgent.CreateSymlinks();
                if (!mainWindowViewModel.HideSuccessfulOperationDialog)
                {
                    MessageBox.Show(this, L("MsgSuccess"), L("MsgDone"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, L("MsgError"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void AboutButton_OnClick(object sender, RoutedEventArgs e)
        {
            AboutWindow aboutWindow = new AboutWindow();
            aboutWindow.ShowDialog();
        }

        private void BrowseIconButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            OpenFileDialog iconDialog = new OpenFileDialog
            {
                Title = L("MsgSelectIconTitle"),
                Filter = L("MsgIconFilter"),
                Multiselect = false
            };

            if (iconDialog.ShowDialog() == true)
            {
                mainWindowViewModel.CustomIconPath = iconDialog.FileName;
            }
        }

        #endregion


        #region helper methods

        private string[] GetDroppedFileOrFolderList(DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.Text))
            {
                string droppedFileOrFolderList = (string)e.Data.GetData(DataFormats.Text);
                return GetFileOrFolderListFromString(droppedFileOrFolderList);
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                try
                {
                    return (string[])e.Data.GetData(DataFormats.FileDrop);
                }
                catch (COMException) // Handle long-path scenarios
                {
                    return LongPathAware.GetPathsFromShellIdListArray(e.Data).ToArray();
                }
            }

            return null;
        }

        private string[] GetFileOrFolderListFromString(string fileOrFolderListString)
        {
            return fileOrFolderListString
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => SanitizePath(path))
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .ToArray();
        }

        private string SanitizePath(string path)
        {
            return path.Trim().Trim('"');
        }

        private void AddToSourceFileOrFolderList(IEnumerable<string> fileOrFolderList)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            foreach (string fileOrFolder in fileOrFolderList)
            {
                if (!mainWindowViewModel.FileOrFolderList.Contains(fileOrFolder))
                {
                    mainWindowViewModel.FileOrFolderList.Add(fileOrFolder);
                }
            }
        }

        private void AssignDestinationPath(string destinationPath)
        {
            if (!(this.DataContext is MainWindowViewModel mainWindowViewModel)) return;

            if (Directory.Exists(destinationPath))
                mainWindowViewModel.DestinationPath = destinationPath;
        }

        private bool IsRunningAsAdmin()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
            {
                WindowsPrincipal principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        #endregion


        #region nested types

        private class LanguageItem
        {
            public string Code { get; set; }
            public string DisplayName { get; set; }
        }

        #endregion
    }
}
