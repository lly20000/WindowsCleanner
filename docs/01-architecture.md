# 架构与代码地图

## 技术栈

| 项 | 值 |
| --- | --- |
| 语言 / 运行时 | C# / .NET 9 (`net9.0-windows`) |
| UI 框架 | Windows Forms（`UseWindowsForms=true`），**纯代码建 UI，无 Designer 文件** |
| 输出类型 | `WinExe`（无控制台窗口） |
| 语言特性 | `ImplicitUsings=enable`、`Nullable=enable`、集合表达式 `[]`、`required` 成员、`record struct` |
| 依赖 | 无任何 NuGet 第三方包，仅 BCL |
| 测试 | **无测试项目** |

配置见 `WindowsCleannerApp/WindowsCleannerApp.csproj`，解决方案为 `WindowsCleannerApp/WindowsCleannerApp.sln`（注意 sln 位于子目录而非仓库根）。

## 目录结构

```
WindowsCleanner/                     ← 仓库根（git root）
├── README.md                        ← 面向用户的简介
├── docs/                            ← 本文档目录
└── WindowsCleannerApp/              ← 唯一的项目目录
    ├── WindowsCleannerApp.sln
    ├── WindowsCleannerApp.csproj
    ├── Program.cs                   ← 入口
    ├── MainForm.cs                  ← 全部 UI
    ├── AdminHelper.cs               ← 管理员权限检测与提升重启
    ├── CleanupTargets.cs            ← 清理目标的「配置源」
    ├── CleanupTarget.cs             ← 清理目标模型
    ├── FileSystemCleaner.cs         ← 扫描与删除的核心引擎
    ├── ScanResult.cs                ← 扫描结果模型
    ├── CleanupResult.cs             ← 清理结果模型
    ├── CleanupTargetViewModel.cs    ← 表格行的绑定模型
    ├── SizeFormatter.cs             ← 字节数格式化
    ├── bin/ obj/ .vs/ .idea/        ← 构建与 IDE 产物（当前未被 .gitignore 忽略）
```

所有类型都在同一个命名空间 `WindowsCleannerApp` 下，且除模型外均为 `internal sealed` / `internal static`。

## 分层与依赖

```
Program
   └─> MainForm ──────────────┬──> CleanupTargets  (生成 IReadOnlyList<CleanupTarget>)
        (UI / 交互 / 汇总)     ├──> FileSystemCleaner (扫描 + 删除)
                              ├──> AdminHelper      (权限)
                              └──> SizeFormatter    (展示格式化)

CleanupTarget ──(被引用)──> ScanResult ──(输入)──> CleanupResult
CleanupTargetViewModel 持有 CleanupTarget，供 DataGridView 绑定
```

关键设计：**清理逻辑与 UI 解耦**。`FileSystemCleaner` 完全不引用任何 WinForms 类型，
只接受 `CleanupTarget` 列表、返回结果对象；新增清理目标只需改 `CleanupTargets.cs`，无需动 UI。

## 各文件职责

### `Program.cs`
标准 WinForms 入口，`[STAThread]` + `ApplicationConfiguration.Initialize()` + `Application.Run(new MainForm())`。共 11 行，无参数处理。

### `MainForm.cs`（约 390 行，最大的文件）
单窗口，全部控件在 `InitializeLayout()`（`MainForm.cs:29`）中以代码方式构造。

- **布局**：`TableLayoutPanel` 四行 —— 标题栏（96px 深蓝 Panel）/ 操作栏（`FlowLayoutPanel`）/ 表格（`DataGridView`，占满剩余）/ 底部状态栏（44px）。
- **表格**：`AutoGenerateColumns = false`，手工添加 8 列 —— Clean(复选框) / Target / Category / Path / Files / Space / Status / Notes。数据源是 `BindingList<CleanupTargetViewModel>`。
- **复选框即时提交**：`CurrentCellDirtyStateChanged` → `CommitEdit`（`MainForm.cs:123`），使勾选后立刻触发 `CellValueChanged` → `UpdateSummary()`，汇总数字实时更新。
- **核心方法**：
  | 方法 | 位置 | 说明 |
  | --- | --- | --- |
  | `ScanAsync` | `MainForm.cs:215` | 取目标 → 后台扫描 → 重建行集合 → 更新汇总 |
  | `CleanAsync` | `MainForm.cs:257` | 校验选择 → 确认弹窗 → 复扫 → 删除 → 重扫 → 结果弹窗 |
  | `SetBusyState` | `MainForm.cs:312` | 忙碌时禁用按钮与表格、切换等待光标 |
  | `UpdateSummary` | `MainForm.cs:322` | 计算 Total / Selected 字节数 |
  | `GetStatus` / `BuildNotes` | `MainForm.cs:342` / `:329` | 把 `ScanResult` 映射成 Status 与 Notes 文本 |
  | `UpdateAdminUi` / `RestartAsAdministrator` | `MainForm.cs:357` / `:369` | 权限提示与提权重启 |
- **启动即扫描**：构造函数中 `Load += async (_, _) => await ScanAsync();`（`MainForm.cs:26`）。

### `FileSystemCleaner.cs`（核心引擎，无状态、全部方法为 `static` 实现）
- `ScanTargets` / `CleanTargets`：对列表逐项循环（**串行**，非并行）。
- `ScanTarget`（`:29`）：先判目录状态 → 枚举文件 → 逐个 `FileInfo` 取大小 → 累加并记录完整文件路径列表。
- `CleanTarget`（`:111`）：遍历扫描阶段记录的文件路径，清除只读属性后 `Delete()`，累计已删字节数；最后调用 `TryDeleteEmptyDirectories` 清理空目录。
- `EnumerateFilesRecursive`（`:159`）：用显式 `Stack<string>` 做迭代式深度遍历，**逐层 try/catch**，某个子目录无权限时只跳过该目录而不中断整体扫描 —— 这是它不用 `Directory.EnumerateFiles(..., AllDirectories)` 的原因。
- `EnumerateDirectoriesBottomUp`（`:224`）：收集所有子目录后**按路径长度降序排序**来近似「自底向上」，从而先删深层空目录。注意根目录本身不在列表中，因此清理后目标根目录会保留。
- `GetDirectoryState`（`:255`）：区分「不存在」与「存在但无权限」三态判断，先试 `Directory.Exists`，再退回 `File.GetAttributes` 捕获 `UnauthorizedAccessException`。返回私有 `record struct DirectoryState`。

### `CleanupTargets.cs`（配置源，详见 [02-cleanup-targets.md](02-cleanup-targets.md)）
静态类，`GetDefaultTargets()` 每次调用都**重新枚举磁盘**生成目标列表（包括遍历 `C:\Users\*` 和 JetBrains 产品目录），因此列表是动态的、随机器环境变化。

### `AdminHelper.cs`
- `IsRunningAsAdministrator()`：`WindowsPrincipal.IsInRole(WindowsBuiltInRole.Administrator)`。
- `TryRestartElevated()`：用 `ProcessStartInfo { UseShellExecute = true, Verb = "runas" }` 重启自身，透传原命令行参数；专门捕获 `Win32Exception.NativeErrorCode == 1223`（用户取消 UAC）并返回友好文案，`MainForm` 对这种情况静默处理不弹错误框。
- 项目**没有 app.manifest**，即不要求默认以管理员启动，而是运行时按需提权。

### 模型类
| 类型 | 可变性 | 说明 |
| --- | --- | --- |
| `CleanupTarget` | 全 `init`，不可变 | 描述一个清理目标；含 `Recursive`、`DeleteRootFilesOnly` 开关与 `IsSafeToDelete()` 白名单钩子 |
| `ScanResult` | 全 `required init` | 扫描产物，携带 `Files` 完整路径列表与 `Warnings`，并回指 `Target` |
| `CleanupResult` | 全 `required init` | 删除产物：已删字节/文件数 + `Errors` |
| `CleanupTargetViewModel` | `Selected`/`Status` 等为可变 `set` | 表格行模型，`Name`/`Category`/`Location` 是对 `Target` 的只读投影；`SizeText` 由 `EstimatedBytes` 计算 |

`CleanupTargetViewModel` **未实现 `INotifyPropertyChanged`** —— 所以刷新依赖每次扫描后 `rows.Clear()` + 重新 `Add` + `ResetBindings()`（`MainForm.cs:224-242`），而不是属性级通知。

### `SizeFormatter.cs`
`FormatBytes(long)`：按 1024 进制逐级降到 B/KB/MB/GB/TB，保留最多两位小数（`0.##`），`<=0` 返回 `"0 B"`。
