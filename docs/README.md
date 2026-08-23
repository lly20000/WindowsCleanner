# WindowsCleanner 项目文档

本目录记录对 WindowsCleanner 代码库的通读结果，作为后续开发的参考基线。

> 记录时间：2026-08-24
> 对应提交：`0309126 Add 1st Version create basic framework`（工作区另有未提交改动）

## 文档索引

| 文档 | 内容 |
| --- | --- |
| [01-architecture.md](01-architecture.md) | 技术栈、目录结构、各文件职责、分层与依赖关系 |
| [02-cleanup-targets.md](02-cleanup-targets.md) | 清理目标（CleanupTarget）的完整清单与生成规则 |
| [03-scan-clean-flow.md](03-scan-clean-flow.md) | 扫描流程、清理流程、权限提升流程的逐步说明 |
| [04-observations.md](04-observations.md) | 通读中发现的风险点、待确认问题与改进建议 |

## 一句话概述

WindowsCleanner 是一个 .NET 9 + WinForms 的单窗口桌面工具：启动后自动扫描一批预设的
Windows 临时文件/缓存目录，在表格中展示每个目标的文件数与可回收空间，用户勾选后确认删除。

## 快速上手

```powershell
# 运行
dotnet run --project .\WindowsCleannerApp\WindowsCleannerApp.csproj

# 构建
dotnet build .\WindowsCleannerApp\WindowsCleannerApp.sln
```

需要 .NET 9 SDK，且只能在 Windows 上运行（`net9.0-windows` + WinForms）。
部分系统目录（如 `Prefetch`、`SoftwareDistribution\Download`）需要管理员权限才能完整扫描，
程序内提供「Restart as Administrator」按钮触发 UAC 重启。
