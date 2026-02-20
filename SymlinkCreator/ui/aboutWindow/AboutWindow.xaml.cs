using SymlinkCreator.i18n;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace SymlinkCreator.ui.aboutWindow
{
    public partial class AboutWindow : Window
    {
        #region constructor

        public AboutWindow()
        {
            InitializeComponent();
            this.Loaded += AboutWindow_Loaded;
            LocalizationManager.LanguageChanged += (s, e) => RefreshUiText();
        }

        #endregion


        #region window event handles

        private void AboutWindow_Loaded(object sender, RoutedEventArgs e)
        {
            RefreshUiText();
        }

        #endregion


        #region localization

        private void RefreshUiText()
        {
            this.Title = LocalizationManager.Get("AboutWindowTitle");
            DeveloperTextBlock.Text = LocalizationManager.Get("AboutDeveloper");
            VisitWebsiteRun.Text = LocalizationManager.Get("AboutVisitWebsite");
            OkButton.Content = LocalizationManager.Get("OkButton");
        }

        #endregion


        #region control event handles

        private void Hyperlink_OnClick(object sender, RoutedEventArgs e)
        {
            Hyperlink hyperlink = sender as Hyperlink;
            if (hyperlink?.NavigateUri != null)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(hyperlink.NavigateUri.ToString()) { UseShellExecute = true });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to open link: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OkButton_OnClick(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion
    }
}
