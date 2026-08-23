# 通读观察：风险点与改进建议

本文记录通读代码时发现的问题，**尚未做任何修改**，仅作为后续讨论与排期的输入。
按「安全风险 → 功能缺陷 → 工程质量」排序。

---

## A. 安全 / 数据风险

### A1. 删除是永久删除，不进回收站 ★重要
`FileSystemCleaner.CleanTarget`（`FileSystemCleaner.cs:127`）调用 `FileInfo.Delete()`，
文件直接从磁盘移除，**没有任何撤销途径**。

而目标列表中包含 `Downloads`、`Videos`、`Android AVD`、`JetBrains projects` 这类
**含用户个人数据**的目录，一旦误勾选并确认，数据不可恢复。

> 建议：对 `User Downloads` / `User Videos` / `Android Virtual Machine` 等分类改用
> `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin)`
> 送入回收站；或在 `CleanupTarget` 上加一个 `UseRecycleBin` 开关，由目标自行声明。

### A2. 递归遍历不检查 reparse point（符号链接 / junction）★重要
`EnumerateFilesRecursive`（`FileSystemCleaner.cs:159`）用 `Directory.GetDirectories` 拿到子目录后
直接压栈继续下钻，**没有过滤 `FileAttributes.ReparsePoint`**。

若某个目标目录下存在 junction 或 symlink（用户把 `Downloads` 重定向到别的盘是常见做法，
`AppData` 下也存在系统 junction），遍历会**穿透到链接目标**，导致删除范围超出用户在
表格中看到的那个路径。极端情况下还可能因环形链接陷入长时间遍历。

> 建议：在压栈前检查 `(new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0`，
> 命中则跳过并记入 `Warnings`，让用户知道有目录被跳过。

### A3. 文件级保护过弱
`CleanupTarget.IsSafeToDelete`（`CleanupTarget.cs:19`）**只排除 `.sys` 一种扩展名**，
其余文件（含 `.exe`、`.iso`、用户文档）在选中的目标范围内一律删除。

对纯缓存目录这是合理的；但用于 `Downloads`/`Videos` 时，这个钩子形同虚设。

> 建议：把过滤规则下沉为每个 `CleanupTarget` 可配置的谓词 / 扩展名黑白名单，
> 而不是所有目标共用一条硬编码规则。

### A4. 清理只读属性后若删除失败不会回滚
`FileSystemCleaner.cs:126` 先 `info.IsReadOnly = false` 再 `Delete()`。
若 `Delete()` 抛异常（文件被占用），只读属性已被清除且不恢复，等于**静默修改了用户文件的属性**。

> 建议：捕获异常时把 `IsReadOnly` 恢复原值，或仅在删除失败重试时才清除只读位。

---

## B. 功能缺陷

### B1. "Scan completed" / "Cleanup completed" 状态文字永远看不到 ★确认的 bug
```csharp
statusLabel.Text = "Scan completed";     // MainForm.cs:244
// ...
finally { SetBusyState(false); }         // MainForm.cs:253
//        └─ statusLabel.Text = message ?? "Ready";   ← 立刻被覆写成 "Ready"
```
`CleanAsync` 里的 `"Cleanup completed"`（`MainForm.cs:299`）同理，
它在 MessageBox 关闭后才赋值，紧接着就被 `finally` 的 `SetBusyState(false)` 重置。

**结果：这两条成功提示从未真正显示给用户。**（失败路径因为后面跟着模态弹窗，
文字在弹窗期间可见，关闭后同样被重置。）

> 修复方向：把完成文案作为参数传给 `SetBusyState(false, "Scan completed")`，
> 或让 `finally` 不无条件覆盖 statusLabel。

### B2. 无法取消长时间扫描
扫描/删除全程没有 `CancellationToken`。选中 `Downloads` 或 `.cache` 这类可能含
数十万文件的目录时，`ScanAsync` 会长时间占用，UI 被 `SetBusyState(true)` 全面禁用，
用户只能等待或强杀进程。

> 建议：给 `ScanTargets` / `CleanTargets` 加 `CancellationToken`，并在忙碌时把
> Scan 按钮变成 Cancel。

### B3. 无进度反馈
状态栏只有一句静态的 "Scanning common cleanup locations..."，
用户不知道当前扫到第几个目标、剩余多少。目标数量在多用户 + 多 JetBrains 产品的机器上可达数十个。

> 建议：`ScanTargets` 接受 `IProgress<T>`，逐目标回报进度。

### B4. `GetDefaultTargets()` 在 UI 线程枚举磁盘
`MainForm.cs:221` 直接同步调用，而该方法内部会遍历 `C:\Users\*` 和 JetBrains 产品目录
（`CleanupTargets.cs:48`、`:158`）。本地 SSD 上很快，但若用户档案目录在慢盘或网络位置上，会造成 UI 卡顿。

> 建议：一并挪进 `Task.Run`。

### B5. `AddIfPresent` 名不符实
方法名暗示「存在才添加」，实际只过滤了**环境变量为空**的情况，从不检查 `Directory.Exists`
（`CleanupTargets.cs:19-37`）。真正的存在性判断发生在扫描阶段。

这不影响正确性（不存在的目标显示为 `Missing`），但方法名会误导后续维护者。
注意 JetBrains 那组是**唯一**做了 `Directory.Exists` 前置检查的（`CleanupTargets.cs:153`），
两种风格不一致。

> 建议：要么改名为 `AddTarget`，要么统一成「不存在就不加入列表」的行为，减少表格里的 `Missing` 噪音。

### B6. 扫描结果把全部文件路径驻留内存
`ScanResult.Files` 保存每个待删文件的完整路径。对超大目录（十万级文件）会产生可观的内存占用，
且这份列表在清理前又会被**重新扫描一次**（`MainForm.cs:284`）后丢弃重建。

> 说明：删除前重扫是**有意的 TOCTOU 缓解**，不建议去掉；但可以考虑扫描阶段只统计
> 大小与数量、清理阶段才枚举路径，用内存换一次遍历。

---

## C. 工程质量

### C1. 缺少 `.gitignore` ★易修复
仓库中没有 `.gitignore`，`git status` 显示 `bin/`、`obj/`、`.vs/`、`.idea/` 全部处于未跟踪状态，
随时可能被误提交。

> 建议：加一份标准 `dotnet` gitignore（`dotnet new gitignore`）。

### C2. README 已与代码脱节
根目录 `README.md` 只列出了 5 个系统级清理目标，**完全没有提及**代码中已实现的
用户级目标（`.cache` / `.codex` / go-build / NVIDIA / AVD）、JetBrains 目标，
以及风险最高的 `Downloads` 与 `Videos`。也没有提到「Restart as Administrator」功能。

> 建议：README 补充完整目标清单与个人数据目录的风险警告。

### C3. 没有测试项目
以下都是纯逻辑、无 IO 依赖或易于用临时目录测试的部分，目前零覆盖：
- `SizeFormatter.FormatBytes` 的进位与边界（0、负数、恰好 1024、TB 上限）
- `EnumerateDirectoriesBottomUp` 的排序是否真能保证先删深层目录
- `GetDirectoryState` 的「存在 / 不存在 / 存在但无权限」三态判断
- `AdminHelper.QuoteArgument` 的引号与反斜杠转义

### C4. 多处无条件 `catch`
`FileSystemCleaner.cs:174`、`:189`、`:240` 使用裸 `catch { }` / `catch { continue; }`，
会一并吞掉 `OutOfMemoryException` 等非预期异常，也不产生任何 warning，
用户无法得知「有目录因故被跳过」。

> 建议：收窄为 `catch (UnauthorizedAccessException or IOException)`（文件其余部分已是这种写法），
> 并把跳过的目录记入 `Warnings`。

### C5. `CleanupTargetViewModel` 未实现 `INotifyPropertyChanged`
因此刷新只能靠「清空 + 重建全部行 + `ResetBindings()`」（`MainForm.cs:224-242`）。
当前够用，但若要做 B3 的逐目标增量刷新，必须先补上属性通知。

### C6. 串行扫描
`ScanTargets`（`FileSystemCleaner.cs:5`）对目标逐个 `foreach`。在 SSD 上并行扫描多个目标
可以明显缩短总时长（`Parallel.ForEach` 或 `Task.WhenAll`）。机械硬盘上收益有限甚至为负，
需实测后再定。

### C7. 解决方案文件位于子目录
`WindowsCleannerApp.sln` 在 `WindowsCleannerApp/` 内而非仓库根，
在根目录执行 `dotnet build` 无法直接找到解决方案。属于轻微不便。

---

## D. 当前工作区状态（记录用）

截至本次通读，工作区存在**未提交改动**：

- 已修改：`CleanupTarget.cs`、`CleanupTargetViewModel.cs`、`CleanupTargets.cs`、
  `FileSystemCleaner.cs`、`MainForm.cs`、`ScanResult.cs`
- 未跟踪：`AdminHelper.cs`、`WindowsCleannerApp.sln`、`README.md`、`bin/`、`obj/`、`.vs/`、`.idea/`

也就是说**整个管理员提权功能（`AdminHelper.cs`）尚未纳入版本控制**，
唯一的提交 `0309126` 只包含最初的基础框架。本文档描述的是**工作区当前代码**的状态，
而非该提交的状态。
