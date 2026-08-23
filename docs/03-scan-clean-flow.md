# 运行时流程

## 1. 启动

```
Program.Main  [STAThread]
  └─ ApplicationConfiguration.Initialize()
  └─ Application.Run(new MainForm())
        ├─ MainForm 构造：设定标题/尺寸/字体 → InitializeLayout() → UpdateAdminUi()
        └─ Load 事件（async）→ ScanAsync()      ← 打开窗口即自动扫描
```

`UpdateAdminUi()`（`MainForm.cs:357`）在此时决定：

- 已是管理员 → 隐藏「Restart as Administrator」按钮，绿色提示「Administrator mode enabled」。
- 非管理员 → 显示按钮，橙色提示部分目录可能 Access denied。

## 2. 扫描流程 `ScanAsync`（`MainForm.cs:215`）

```
SetBusyState(true, "Scanning common cleanup locations...")   ← 禁用按钮/表格，等待光标
  │
  ├─ CleanupTargets.GetDefaultTargets()        ← UI 线程！枚举 C:\Users\* 与 JetBrains 目录
  │
  ├─ await Task.Run(() => cleaner.ScanTargets(targets))      ← 后台线程，串行扫描每个目标
  │       └─ 对每个 target: ScanTarget()
  │             ├─ GetDirectoryState(path) → (Exists, Accessible)
  │             │     ├─ 不存在        → 返回 Exists=false，扫描结束
  │             │     └─ 存在但无权限  → 加 warning「Access denied...」，Bytes=0
  │             ├─ EnumerateFilesSafely()
  │             │     ├─ DeleteRootFilesOnly 或 !Recursive → TopDirectoryOnly
  │             │     └─ 否则 EnumerateFilesRecursive()（显式栈 + 逐目录 try/catch）
  │             └─ 对每个文件: new FileInfo → IsSafeToDelete() 过滤 .sys → 累加 Length，记录路径
  │
  ├─ rows.RaiseListChangedEvents = false → Clear → 逐行 Add → = true → ResetBindings()
  │       每行 Selected = DefaultSelected && Exists && Accessible && EstimatedBytes > 0
  │       Status  = Missing / Access denied / Ready        （GetStatus, MainForm.cs:342）
  │       Notes   = Description + Warnings，用 " | " 连接   （BuildNotes, MainForm.cs:329）
  │
  ├─ UpdateSummary() → "Total X   Selected Y"
  └─ finally: SetBusyState(false)
```

**关键点**

- 扫描阶段会把**每个待删文件的完整路径**保存进 `ScanResult.Files`，清理阶段直接复用这份列表，不再重新遍历。
- 空目录（`EstimatedBytes == 0`）即使 `DefaultSelected = true` 也不会被自动勾选。
- 单个文件读取失败只产生一条 warning 并跳过，不中断整个目标的扫描。
- 关闭 `RaiseListChangedEvents` 后批量填充，避免 `DataGridView` 逐行刷新造成卡顿。

## 3. 清理流程 `CleanAsync`（`MainForm.cs:257`）

```
筛选 rows: Selected && Exists && EstimatedBytes > 0
  ├─ 为空 → 提示「Select at least one cleanup target first.」并返回
  │
  ├─ 确认弹窗（YesNo + Warning 图标）："Clean N selected targets and free about X?"
  │     └─ 非 Yes → 直接返回
  │
  ├─ SetBusyState(true, "Deleting selected files...")
  │
  ├─ await Task.Run(cleaner.ScanTargets(selectedTargets))    ← ★ 删除前重新扫描
  ├─ await Task.Run(cleaner.CleanTargets(freshScans))
  │       └─ 对每个文件: FileInfo → IsReadOnly = false → Delete()
  │             失败(UnauthorizedAccess/IO) → 记入 Errors，继续下一个
  │       └─ TryDeleteEmptyDirectories()：按路径长度降序，删掉变空的子目录
  │
  ├─ await ScanAsync()                                       ← 删完立即重扫刷新表格
  └─ 结果弹窗："Cleanup completed. Freed X." + 最多 8 条错误信息
```

**关键点**

- ★ **删除前的二次扫描**（`MainForm.cs:284`）是有意为之：用户从上次扫描到点击删除之间，
  文件可能已变化。这样删除操作总是基于最新的文件列表，缓解 TOCTOU 问题。
  代价是选中大目录时会扫两遍。
- 错误只展示前 8 条（`.Take(8)`，`MainForm.cs:288`），避免弹窗过长。
- `TryDeleteEmptyDirectories` 仅在 `Recursive = true` 时执行，且**不会删除目标根目录本身**
  （`EnumerateDirectoriesBottomUp` 只收集子目录，不含 root）—— 所以 `Temp` 这类目录清理后仍存在。
- 「自底向上」是靠**路径字符串长度降序排序**近似实现的（`FileSystemCleaner.cs:251`），
  不是真正的深度优先后序遍历。同深度不同长度的路径顺序可能不理想，但对删空目录这个用途基本够用。

## 4. 权限提升流程 `RestartAsAdministrator`（`MainForm.cs:369`）

```
已是管理员？ → 仅刷新 UI，返回
  │
  └─ AdminHelper.TryRestartElevated(this, out errorMessage)
        ├─ Environment.ProcessPath 为空 → false + "Unable to determine..."
        ├─ 拼接原命令行参数（跳过 argv[0]，按需加引号转义）
        ├─ Process.Start(UseShellExecute=true, Verb="runas")   ← 触发 UAC 弹窗
        │     ├─ 成功  → true  → MainForm.Close()  （旧进程退出，新进程接管）
        │     ├─ 用户取消 (Win32 1223) → false + "The elevation request was canceled."
        │     └─ 其他异常 → false + ex.Message
        └─ 调用方：只有非「用户取消」的错误才弹 MessageBox（MainForm.cs:383）
```

程序没有 app.manifest，默认以普通权限启动；提权是运行时按需的可选动作。
提权后新进程会重新走一遍启动流程并自动扫描，此时 `Prefetch` 等目录不再显示 Access denied。

## 5. 忙碌状态与线程模型

`SetBusyState(bool, string?)`（`MainForm.cs:312`）统一控制：
禁用 Scan/Clean/Admin 三个按钮与表格、切换 `UseWaitCursor`、更新底部状态文本。

线程模型很简单：
- 只有 `cleaner.ScanTargets` / `cleaner.CleanTargets` 两处放进 `Task.Run` 的后台线程。
- `CleanupTargets.GetDefaultTargets()`（含磁盘目录枚举）仍在 UI 线程执行。
- 结果回到 UI 线程后再更新 `BindingList`，没有跨线程访问控件的问题。
- **没有 `CancellationToken`** —— 扫描/删除一旦开始无法取消，只能等它结束。
