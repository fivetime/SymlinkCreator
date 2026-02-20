using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace SymlinkCreator.core
{
    public class SymlinkAgent
    {
        #region members

        private readonly List<string> _sourceFileOrFolderList;
        private string _destinationPath;
        private readonly bool _shouldUseRelativePath;
        private readonly bool _shouldRetainScriptFile;

        // 新增：自定义名称（仅在单个源时生效）、图标路径、是否固定到快速访问
        private readonly string _customSymlinkName;
        private readonly string _customIconPath;
        private readonly bool _shouldPinToQuickAccess;

        private string[] _splittedDestinationPath;

        #endregion


        #region constructor

        public SymlinkAgent(IEnumerable<string> sourceFileOrFolderList, string destinationPath,
            bool shouldUseRelativePath = true, bool shouldRetainScriptFile = false,
            string customSymlinkName = null, string customIconPath = null,
            bool shouldPinToQuickAccess = false)
        {
            this._sourceFileOrFolderList = sourceFileOrFolderList.ToList();
            this._destinationPath = destinationPath;
            this._shouldUseRelativePath = shouldUseRelativePath;
            this._shouldRetainScriptFile = shouldRetainScriptFile;
            this._customSymlinkName = customSymlinkName;
            this._customIconPath = customIconPath;
            this._shouldPinToQuickAccess = shouldPinToQuickAccess;
        }

        #endregion


        #region methods

        public void CreateSymlinks()
        {
            // Check for destination path
            if (!Directory.Exists(_destinationPath))
            {
                throw new FileNotFoundException("Destination path does not exist", _destinationPath);
            }

            // Remove the last '\' character from the path if exists
            if (_destinationPath[_destinationPath.Length - 1] == '\\')
                _destinationPath = _destinationPath.Substring(0, _destinationPath.Length - 1);

            _splittedDestinationPath = GetSplittedPath(_destinationPath);

            string scriptFileName = ApplicationConfiguration.ApplicationFileName + "_" +
                                    DateTime.Now.Ticks.ToString() + ".cmd";

            ScriptExecutor scriptExecutor = PrepareScriptExecutor(scriptFileName);
            scriptExecutor.ExecuteAsAdmin();

            if (!_shouldRetainScriptFile)
                File.Delete(scriptFileName);

            if (scriptExecutor.ExitCode != 0)
            {
                throw new ApplicationException("Symlink script exited with an error.\n" + scriptExecutor.StandardError);
            }

            // 创建成功后的后处理：写 desktop.ini / 固定到快速访问
            PostProcess();
        }

        #endregion


        #region post-process methods

        private void PostProcess()
        {
            bool hasCustomName = !string.IsNullOrWhiteSpace(_customSymlinkName);
            bool hasCustomIcon = !string.IsNullOrWhiteSpace(_customIconPath) && File.Exists(_customIconPath);

            // 计算每个源对应的 symlink 路径
            foreach (string sourceFilePath in _sourceFileOrFolderList)
            {
                if (!Directory.Exists(sourceFilePath))
                    continue; // 只处理文件夹类型

                string[] splittedSourceFilePath = GetSplittedPath(sourceFilePath);
                string originalFolderName = splittedSourceFilePath.Last();

                // 如果有自定义名称且只有单个源，使用自定义名称；否则用原名
                string symlinkName = (hasCustomName && _sourceFileOrFolderList.Count == 1)
                    ? _customSymlinkName
                    : originalFolderName;

                string symlinkFullPath = Path.Combine(_destinationPath, symlinkName);

                // 只写图标不写名称，源文件夹显示名不受影响
                if (hasCustomIcon)
                {
                    ApplyDesktopIni(sourceFilePath, null, _customIconPath);

                    if (Directory.Exists(symlinkFullPath))
                        ApplyDesktopIni(symlinkFullPath, null, _customIconPath);
                }

                // 固定到快速访问
                if (_shouldPinToQuickAccess && Directory.Exists(symlinkFullPath))
                {
                    PinToQuickAccess(symlinkFullPath);
                }
            }
        }

        /// <summary>
        /// 向源文件夹写入 desktop.ini，实现自定义显示名称和图标。
        /// 注意：desktop.ini 写在源文件夹里，Symlink 是透明的，资源管理器会读到源文件夹的 ini。
        /// </summary>
        private void ApplyDesktopIni(string folderPath, string customName, string iconPath)
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("[.ShellClassInfo]");

                if (!string.IsNullOrWhiteSpace(customName))
                    sb.AppendLine($"LocalizedResourceName={customName}");

                if (!string.IsNullOrWhiteSpace(iconPath))
                {
                    sb.AppendLine($"IconResource={iconPath},0");
                    sb.AppendLine("IconIndex=0");
                }

                string iniPath = Path.Combine(folderPath, "desktop.ini");

                // 如果已存在，先去掉只读/隐藏属性再覆盖
                if (File.Exists(iniPath))
                    File.SetAttributes(iniPath, FileAttributes.Normal);

                File.WriteAllText(iniPath, sb.ToString(), Encoding.Unicode);

                // desktop.ini 必须是 Hidden+System 才能生效
                File.SetAttributes(iniPath, FileAttributes.Hidden | FileAttributes.System);

                // 文件夹本身需要有 System 属性，桌面/资源管理器才会读取 desktop.ini
                FileAttributes folderAttr = File.GetAttributes(folderPath);
                if ((folderAttr & FileAttributes.System) == 0)
                    File.SetAttributes(folderPath, folderAttr | FileAttributes.System);

                // 通知资源管理器刷新图标缓存
                SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to apply desktop.ini to '{folderPath}': {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 将指定文件夹固定到快速访问（通过 PowerShell Shell COM 接口）。
        /// 此操作不需要管理员权限，在普通权限下执行即可。
        /// </summary>
        private void PinToQuickAccess(string folderPath)
        {
            try
            {
                // 用 PowerShell 调用 Shell.Application COM 对象的 pintohome 动词
                string escapedPath = folderPath.Replace("'", "''");
                string script = $"$shell = New-Object -ComObject Shell.Application; " +
                                $"$folder = $shell.Namespace('{escapedPath}'); " +
                                $"$folder.Self.InvokeVerb('pintohome')";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -NonInteractive -Command \"{script}\"",
                    WindowStyle = ProcessWindowStyle.Hidden,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };

                using (var process = Process.Start(psi))
                {
                    process?.WaitForExit(5000);
                }
            }
            catch (Exception ex)
            {
                throw new ApplicationException($"Failed to pin '{folderPath}' to Quick Access: {ex.Message}", ex);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Auto)]
        private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

        #endregion


        #region helper methods

        private ScriptExecutor PrepareScriptExecutor(string scriptFileName)
        {
            ScriptExecutor scriptExecutor = new ScriptExecutor(scriptFileName);

            // Go to destination path
            scriptExecutor.WriteLine(_splittedDestinationPath[0]);
            scriptExecutor.WriteLine("cd \"" + _destinationPath + "\"");

            // 只有单个源且有自定义名称时，用自定义名称命名 symlink
            bool useCustomName = !string.IsNullOrWhiteSpace(_customSymlinkName)
                                 && _sourceFileOrFolderList.Count == 1;

            foreach (string sourceFilePath in _sourceFileOrFolderList)
            {
                string[] splittedSourceFilePath = GetSplittedPath(sourceFilePath);

                string commandLineTargetPath = sourceFilePath;
                if (_shouldUseRelativePath)
                {
                    // Check if both root drives are same
                    if (splittedSourceFilePath.First() == _splittedDestinationPath.First())
                    {
                        string relativePath = GetRelativePath(_splittedDestinationPath, splittedSourceFilePath);
                        // 只有相对路径非空时才使用，空字符串说明路径完全相同（已在上方校验过，此处兜底）
                        if (!string.IsNullOrEmpty(relativePath))
                            commandLineTargetPath = relativePath;
                    }
                }

                // 决定 symlink 的名称
                string symlinkName = useCustomName ? _customSymlinkName : splittedSourceFilePath.Last();

                scriptExecutor.Write("mklink ");
                if (Directory.Exists(sourceFilePath))
                {
                    // 勾选了"固定到快速访问"时用 /J（Junction），pintohome 不会解析 Junction 到真实路径
                    // 否则用 /D（Symlink），支持相对路径和网络路径
                    scriptExecutor.Write(_shouldPinToQuickAccess ? "/j " : "/d ");
                }

                scriptExecutor.WriteLine("\"" + symlinkName + "\" " +
                                         "\"" + commandLineTargetPath + "\"");
            }

            return scriptExecutor;
        }

        private string[] GetSplittedPath(string path)
        {
            return path.Split('\\');
        }

        private string GetRelativePath(string[] splittedCurrentPath, string[] splittedTargetPath)
        {
            List<string> splittedCurrentPathList = splittedCurrentPath.ToList();
            List<string> splittedTargetPathList = splittedTargetPath.ToList();

            while (splittedCurrentPathList.Any() && splittedTargetPathList.Any())
            {
                if (splittedCurrentPathList.First() == splittedTargetPathList.First())
                {
                    splittedCurrentPathList.RemoveAt(0);
                    splittedTargetPathList.RemoveAt(0);
                }
                else
                {
                    break;
                }
            }

            StringBuilder relativePathStringBuilder = new StringBuilder();

            for (int i = 0; i < splittedCurrentPathList.Count; i++)
            {
                relativePathStringBuilder.Append("..\\");
            }

            foreach (string splittedPath in splittedTargetPathList)
            {
                relativePathStringBuilder.Append(splittedPath);
                relativePathStringBuilder.Append('\\');
            }

            if (relativePathStringBuilder[relativePathStringBuilder.Length - 1] == '\\')
                relativePathStringBuilder.Length--;

            return relativePathStringBuilder.ToString();
        }

        #endregion
    }
}