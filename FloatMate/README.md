# 浮岛 FloatMate

一个轻量的 Windows 悬浮桌面助手 MVP。

GitHub 仓库：[desktop-helpworker](https://github.com/xxxlibraaa-commits/desktop-helpworker)

## 已实现

- 系统托盘常驻，左键显示或隐藏；
- Mac 小组件风格的半透明桌面卡片，可拖动、贴边；
- 默认采用桌面小组件层级，浏览器、IDE 等普通窗口会自然覆盖它；
- 设置中可切换成始终置顶的传统悬浮模式；
- CPU、内存、系统盘和网络速度监控；
- 今日目标轨道：新增、开始、暂停、完成和删除；
- 目标 10% 步进、完整进度、预计时长字段和状态事件；
- 目标专注计时；
- 喝水、如厕、起身和护眼一键记录；
- 喝水、如厕、起身和护眼按事件独立保存；
- 按日期复盘、每日概览和目标/健康时间线；
- 喝水、久坐、护眼提醒，可分别启停与设置间隔；
- 半透明材质、鼠标悬停增强清晰度、失焦自动恢复小组件卡片；
- 可选的 Windows 登录后自动启动；
- 今日完成、专注、喝水和休息摘要；
- 一键导出 XLSM，分别保存今日任务与快速记录，支持筛选和后续复盘；
- 完整键盘焦点环与 Windows UI Automation 读屏标签；
- 目标为空时的引导状态，以及删除目标后的 5 秒撤销；
- 尊重 Windows 的界面动画偏好，关闭系统动画时不播放淡入；
- 等宽系统指标数字，避免刷新时产生视觉跳动。

## 设计系统

界面规范由 `ui-ux-pro-max` 生成并按 FloatMate 的 WPF 约束调整，保存在：

```text
design-system\floatmate\MASTER.md
```

当前方向为白色本地助手界面，信息密度 8/10、动效 3/10。实现优先保证低打扰、键盘可用、清晰对比度和数据扫读效率。
- 本地 JSON 持久化；
- 全局快捷键 `Ctrl + Shift + Space`。

## 开发运行

需要 .NET 8 SDK：

```powershell
dotnet run --project .\FloatMate.csproj
```

工作区根目录的 `run.ps1` 会优先使用本项目安装的本地 SDK。由于系统 PowerShell 执行策略可能阻止直接双击脚本，日常使用请双击根目录的 `启动浮岛.cmd`。

## 构建

```powershell
dotnet publish .\FloatMate.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

输出目录：`bin\Release\net8.0-windows\win-x64\publish`。

工作区根目录已经提供 `build.ps1` 和 `启动浮岛.cmd`。当前构建依赖本机已有的 .NET 8 Desktop Runtime；如需分发给没有运行时的电脑，可将发布参数改为 `--self-contained true`。

## 本地数据

数据默认保存在：

```text
%LOCALAPPDATA%\FloatMate\data.json
```

退出应用请右键系统托盘图标并选择“退出”。点击窗口上的横线或关闭窗口只会隐藏到托盘。
