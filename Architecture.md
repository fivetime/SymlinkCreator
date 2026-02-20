# SymlinkCreator Mod — 架构设计文档

## 1. 项目背景

本项目基于 [arnobpl/SymlinkCreator](https://github.com/arnobpl/SymlinkCreator)（MIT License）进行改造，原项目是一个基于 `mklink` 命令的 Windows GUI 工具，用于批量创建符号链接。

改造目标：
- 支持自定义符号链接名称
- 支持自定义文件夹图标（通过 `desktop.ini`）
- 支持将链接固定到资源管理器快速访问侧边栏
- 支持多语言（英文 / 中文简体）

---

## 2. Windows 链接机制对比

理解本项目设计决策的前提是理解 Windows 三种"链接"的本质区别。

| 类型 | 层级 | 对应用透明 | 支持范围 | 有文件大小 |
|------|------|-----------|---------|-----------|
| 快捷方式（.lnk） | 用户层 | ❌ 应用能识别 | 文件、目录、网络 | ✅ 有 |
| Symlink（`mklink /D`） | NTFS 文件系统 | 部分透明 | 文件、目录、网络、相对路径 | ❌ 无 |
| Junction（`mklink /J`） | NTFS 文件系统 | ✅ 完全透明 | 仅本地目录 | ❌ 无 |

### 关键差异

**快捷方式**是独立的 `.lnk` 文件，存储目标路径、显示名称、图标等元数据，显示名就是 `.lnk` 文件名，与目标文件夹完全解耦。

**Symlink** 对大多数 API 透明，但 Windows Shell（资源管理器）的 `pintohome` 动词会追踪 Symlink 到真实路径，导致快速访问显示的是源文件夹名而非链接名。

**Junction** 对所有 API 完全透明，`pintohome` 不追踪 Junction，固定后快速访问保留 Junction 的路径和名称。这是 Windows 系统内部大量使用的机制（如 `C:\Documents and Settings` → `C:\Users`）。

### 设计决策：勾选"固定到快速访问"时改用 Junction

```
未勾选固定：mklink /D  →  Symlink，支持相对路径和网络路径
已勾选固定：mklink /J  →  Junction，pintohome 不解析真实路径，快速访问显示自定义名称
```

---

## 3. 自定义名称机制

### 实现原理

自定义名称通过 `mklink` 命令的链接文件名直接实现：

```cmd
mklink /J "项目" "C:\MyProjects"
```

创建的 Junction 文件名就是"项目"，固定到快速访问后显示的就是"项目"。

### 限制

- 仅在**单个源**时生效（多个源时无法为每个源指定不同名称，回退使用原文件夹名）
- 不修改源文件夹的任何属性，源文件夹显示名保持不变

---

## 4. 自定义图标机制（desktop.ini）

### 原理

Windows 资源管理器通过读取文件夹内的 `desktop.ini` 来显示自定义图标和名称：

```ini
[.ShellClassInfo]
IconResource=C:\path\to\icon.ico,0
IconIndex=0
```

`desktop.ini` 需满足两个条件才能生效：
1. 文件本身具有 `Hidden + System` 属性
2. 所在文件夹具有 `System` 属性

### 关键约束：Symlink/Junction 的透明性问题

由于 Junction 完全透明，向 Junction 目录写入文件，物理上是写入源文件夹。因此：

```
向 C:\Links\项目\desktop.ini 写入  ←→  实际写入 C:\MyProjects\desktop.ini
```

两者是**同一个物理文件**，无法独立设置。

### 设计决策：只写图标，不写 LocalizedResourceName

`desktop.ini` 支持 `LocalizedResourceName` 字段来修改文件夹显示名，但写入后源文件夹在资源管理器中也会显示为自定义名称，用户难以区分真实目录和链接。

**最终方案：**
- `desktop.ini` 只写 `IconResource`，不写 `LocalizedResourceName`
- 自定义名称完全依赖 Junction/Symlink 的文件名（mklink 命名）
- 源文件夹显示名**永远不改变**

```csharp
// 只传图标路径，name 传 null
ApplyDesktopIni(sourceFilePath, null, _customIconPath);
```

### 图标刷新

写入 `desktop.ini` 后调用 `SHChangeNotify` 通知资源管理器刷新图标缓存：

```csharp
[DllImport("shell32.dll", CharSet = CharSet.Auto)]
private static extern void SHChangeNotify(uint wEventId, uint uFlags, IntPtr dwItem1, IntPtr dwItem2);

SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
```

---

## 5. 固定到快速访问机制

通过 PowerShell 调用 Windows Shell COM 对象的 `pintohome` 动词：

```powershell
$shell = New-Object -ComObject Shell.Application
$folder = $shell.Namespace('C:\Links\项目')
$folder.Self.InvokeVerb('pintohome')
```

此操作**不需要管理员权限**，与需要管理员权限的 `mklink` 步骤分离，互不影响。

### Symlink 路径解析问题

`pintohome` 对 Symlink（`/D`）会追踪到真实路径，快速访问最终固定的是 `C:\MyProjects` 而非 `C:\Links\项目`。

对 Junction（`/J`）不追踪，固定的就是 `C:\Links\项目`，显示名为"项目"。

**这是选择 Junction 而非 Symlink 的核心原因。**

---

## 6. 多语言支持

### 架构

采用 .NET 标准 `.resx` 资源文件机制，嵌入 exe 内，无需额外分发文件。

```
i18n/
├── Strings.resx          # 英文（默认）
├── Strings.zh-CN.resx    # 中文简体
└── LocalizationManager.cs
```

### LocalizationManager

核心职责：

```csharp
// 启动时根据系统语言自动选择
LocalizationManager.InitializeFromSystemCulture();

// 运行时切换语言，触发事件通知所有 UI 刷新
LocalizationManager.ApplyLanguage("zh-CN");

// 获取当前语言字符串
string text = LocalizationManager.Get("SourceListLabel");
```

语言切换通过事件驱动，UI 窗口订阅 `LanguageChanged` 事件后调用 `RefreshUiText()` 刷新所有控件文字，实现**运行时即时切换**，无需重启。

### 扩展新语言

只需三步：
1. 新建 `i18n/Strings.ja-JP.resx`（复制英文文件，翻译 value）
2. 在 `LocalizationManager.SupportedLanguages` 列表加一行
3. 在 `.csproj` 加一个 `<EmbeddedResource>` 条目

无需修改任何 UI 代码。

---

## 7. 项目结构

```
SymlinkCreator/
├── core/
│   ├── ApplicationConfiguration.cs   # 应用版本、名称等元信息
│   ├── ScriptExecutor.cs             # 生成并执行 .cmd 脚本
│   └── SymlinkAgent.cs               # 核心逻辑（创建链接、写 desktop.ini、固定快速访问）
├── i18n/
│   ├── LocalizationManager.cs        # 语言管理器
│   ├── Strings.resx                  # 英文资源
│   └── Strings.zh-CN.resx            # 中文资源
├── ui/
│   ├── mainWindow/
│   │   ├── MainWindow.xaml           # 主窗口 UI
│   │   ├── MainWindow.xaml.cs        # 主窗口逻辑（含 RefreshUiText）
│   │   └── MainWindowViewModel.cs    # 数据绑定 ViewModel
│   ├── aboutWindow/
│   │   ├── AboutWindow.xaml
│   │   └── AboutWindow.xaml.cs
│   └── utility/
│       ├── LongPathAware.cs          # 长路径支持
│       ├── NativeAdminShieldIcon.cs  # UAC 盾牌图标
│       └── WindowMaximizeButton.cs   # 禁用最大化按钮
└── App.xaml.cs                       # 启动入口，初始化语言
```

---

## 8. 创建链接完整流程

```
用户点击"创建符号链接"
        │
        ▼
CreateSymlinksButton_OnClick()
        │  参数验证（源列表非空、目标路径非空）
        ▼
SymlinkAgent.CreateSymlinks()
        │
        ├─ 检查目标路径是否存在
        │
        ├─ PrepareScriptExecutor()
        │       │  生成 .cmd 脚本
        │       │  勾选"固定到快速访问" → mklink /J
        │       │  未勾选              → mklink /D
        │       └─ 单个源且有自定义名称 → 用自定义名作为链接文件名
        │
        ├─ scriptExecutor.ExecuteAsAdmin()   ← 需要管理员权限
        │
        ├─ 删除临时脚本（除非勾选"保留脚本"）
        │
        └─ PostProcess()                     ← 不需要管理员权限
                │
                ├─ 有自定义图标 → ApplyDesktopIni(源文件夹, null, iconPath)
                │                  写 desktop.ini（只含 IconResource）
                │                  设置文件夹 System 属性
                │                  SHChangeNotify 刷新图标缓存
                │
                └─ 勾选"固定到快速访问" → PinToQuickAccess(junctionFullPath)
                                           PowerShell pintohome
```

---

## 9. 已知限制

| 限制 | 原因 | 说明 |
|------|------|------|
| 图标会写入源文件夹 | Junction 透明性，无法独立设置 | 只写图标不写名称，源文件夹显示名不变 |
| 快速访问无法单独设置显示名 | Windows 快速访问固定的是路径，显示名取自文件夹本身 | 依赖 Junction 文件名实现自定义名称 |
| 勾选固定时不支持网络路径和相对路径 | Junction 不支持网络路径和相对路径 | 未勾选时仍使用 Symlink，功能完整 |
| 自定义名称仅支持单个源 | 多个源无法逐一指定名称 | 多个源时回退使用原文件夹名 |
