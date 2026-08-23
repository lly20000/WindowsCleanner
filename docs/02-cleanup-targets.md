# 清理目标（CleanupTarget）

所有目标由 `CleanupTargets.GetDefaultTargets()`（`CleanupTargets.cs:5`）生成。
该方法**每次调用都会重新枚举磁盘**（遍历 `C:\Users\*` 与 JetBrains 产品目录），
因此目标数量随机器环境而变，不是固定常量表。

## CleanupTarget 模型字段

| 字段 | 默认值 | 作用 |
| --- | --- | --- |
| `Name` | 必填 | 表格「Target」列显示名 |
| `Path` | 必填 | 目标绝对路径 |
| `Category` | `"General Cleanup"` | 表格「Category」列，用于归类 |
| `Description` | `""` | 拼进「Notes」列，向用户说明风险 |
| `DefaultSelected` | `true` | 扫描后是否默认勾选 |
| `Recursive` | `true` | 是否递归子目录；同时决定清理后是否删除空目录 |
| `DeleteRootFilesOnly` | `false` | 为 `true` 时只处理根目录下的文件（当前所有目标都未启用） |

`IsSafeToDelete(filePath)`（`CleanupTarget.cs:19`）是唯一的文件级过滤钩子，
当前实现**只排除 `.sys` 扩展名**，其余一律视为可删。

## 一、系统级目标（5 个，`DefaultSelected = true`）

通过 `AddIfPresent()`（`CleanupTargets.cs:19`）添加，全部 `Recursive = true`。

| Name | Category | 路径 | 说明 |
| --- | --- | --- | --- |
| User Temp Files | System Cache | `%LOCALAPPDATA%\Temp` | 当前用户临时文件 |
| System Temp Files | System Cache | `%WINDIR%\Temp` | Windows 与安装程序的共享临时文件 |
| Windows Update Cache | System Cache | `%WINDIR%\SoftwareDistribution\Download` | 已下载的更新包，通常可重新下载 |
| Prefetch Cache | System Cache | `%WINDIR%\Prefetch` | 预读数据，清理后首次启动可能略慢 |
| Crash Dumps | Diagnostics | `%LOCALAPPDATA%\CrashDumps` | 应用崩溃转储 |

> ⚠️ 方法名叫 `AddIfPresent`，但实现里**没有做 `Directory.Exists` 判断** —— 唯一的
> 「if present」是过滤掉环境变量为空的路径片段（`CleanupTargets.cs:21`）。
> 目录是否存在是在扫描阶段由 `ScanResult.Exists` 决定的，不存在时表格显示 `Missing`。

## 二、按用户档案生成的目标（`AddPerUserTargets`，`CleanupTargets.cs:39`）

遍历当前用户档案的父目录（通常是 `C:\Users`）下的**每一个**子目录，
跳过 `IsSystemProfile()` 认定的系统档案：`All Users` / `Default` / `Default User` / `Public`（`CleanupTargets.cs:232`）。

对每个真实用户档案 `<profile>` 无条件添加以下 8 个目标（**全部 `DefaultSelected = false`**，
即使路径不存在也会加入列表并显示为 `Missing`）：

| Name 模板 | Category | 路径（相对 `<profile>`） | 风险提示 |
| --- | --- | --- | --- |
| `Android AVD Files ({profile})` | Android Virtual Machine | `.android\avd` | 会删除模拟器镜像、快照与设备数据 |
| `User .cache Files ({profile})` | User Cache | `.cache` | 应用缓存，部分工具重建较慢 |
| `Codex .tmp Files ({profile})` | Codex Cache | `.codex\.tmp` | 无任务占用时通常安全 |
| `Downloads Files ({profile})` | User Downloads | `Downloads` | **个人文件**，安装包/文档/压缩包 |
| `Videos Files ({profile})` | User Videos | `Videos` | **个人文件**，录像与媒体素材 |
| `Go Build Cache ({profile})` | Go Cache | `AppData\Local\go-build` | 重建前 Go 编译会变慢 |
| `NVIDIA DXCache ({profile})` | NVIDIA Cache | `AppData\Local\NVIDIA\DXCache` | 游戏/图形程序会重建着色器缓存 |
| `NVIDIA GLCache ({profile})` | NVIDIA Cache | `AppData\Local\NVIDIA\GLCache` | 同上（OpenGL） |

`Downloads` 与 `Videos` 是**用户个人数据目录**，不是缓存。代码通过
`DefaultSelected = false` + Description 里的显式警告来保证必须由用户主动勾选。

## 三、JetBrains 目标（`AddJetBrainsTargets`，`CleanupTargets.cs:151`）

这一组是**唯一做了存在性检查**的：若 `<profile>\AppData\Local\JetBrains` 不存在则整组跳过（`CleanupTargets.cs:153`）。

存在时，枚举该目录下的每个产品目录（如 `IntelliJIdea2024.1`、`GoLand2024.2` 等），
为每个产品添加固定的 **6 个**子目录目标：

| 子目录 | Category | 说明 |
| --- | --- | --- |
| `caches` | JetBrains Cache | 索引与 IDE 缓存，下次启动重建 |
| `tmp` | JetBrains Temp | 临时文件，通常安全 |
| `log` | JetBrains Logs | IDE 日志，排障有用但一般可删 |
| `database-log` | JetBrains Database Logs | 数据库工具日志 |
| `vcsCache` | JetBrains VCS Cache | 版本控制缓存，需时间重建 |
| `projects` | JetBrains Projects Cache | ⚠️ 可能存放本地项目元数据/历史/工作数据，**清理前需谨慎评估** |

命名格式：`{productName} {subdirectoryName} ({profile})`，例如 `GoLand2024.2 caches (lly20)`。
全部 `DefaultSelected = false`、`Recursive = true`。

## 目标总数估算

```
5（系统级）
+ 8 × 用户档案数
+ 6 × JetBrains 产品数 × 拥有该目录的用户档案数
```

单用户、装了 3 个 JetBrains 产品的机器约为 `5 + 8 + 18 = 31` 个目标。

## 新增目标的方法

1. **系统级固定路径** → 在 `GetDefaultTargets()` 里加一行 `AddIfPresent(...)`。
2. **按用户档案** → 在 `AddPerUserTargets()` 的循环里 `targets.Add(new CleanupTarget { ... })`。
3. **需要遍历子目录的产品族**（类似 JetBrains）→ 仿照 `AddJetBrainsTargets` 写一个私有方法并在循环末尾调用。

新增时的约定：涉及用户数据或重建代价高的目录，一律 `DefaultSelected = false`，
并在 `Description` 中写清后果 —— 该文本会显示在表格的 Notes 列。
