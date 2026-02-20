using SymlinkCreator.i18n;
using System.Windows;

namespace SymlinkCreator
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // 根据系统语言自动选择，不支持的语言回退到英文
            LocalizationManager.InitializeFromSystemCulture();
        }
    }
}