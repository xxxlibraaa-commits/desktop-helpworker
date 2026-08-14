# FloatMate 浮岛

FloatMate 是一个本地优先的 Windows 桌面助手。它可以固定在屏幕左侧并预留独立工作区，让系统状态、今日工作、长期计划、健康记录和每日复盘始终触手可及，同时尽量减少对当前工作的打扰。

> 当前状态：可日常使用的本地版本。应用不需要账号，核心数据默认仅保存在本机。

## 主要功能

### 今日工作

- 创建多个今日目标并以轨道形式展示；
- 记录任务状态、完整进度、预计时长和专注时长；
- 为每个目标维护独立的详细工作内容；
- 已完成任务自动压缩，降低日常界面的信息负担；
- 按日期查看目标事件、健康事件和每日摘要。

### 长期计划

- 同时维护多个按月、40 天或更长周期的计划；
- 未展开的计划保持压缩，点击后再显示任务进度；
- 导入现有 XLSX 甘特计划表；
- 拖动任务时间条整体移动日期，或拖动两端调整周期；
- 修改任务状态、进度、负责人、备注和详细内容；
- 导出带甘特视图和任务数据的 XLSX，支持再次导入。

### 健康与提醒

- 喝水、如厕、起身和护眼按事件独立记录；
- 提醒基于“距上次动作经过多久”触发；
- 提醒支持启停、忽略、延后和每日次数限制；
- 历史数据可按日复盘，不把零值或未完成状态表达为警告。

### 系统与应用状态

- 查看 CPU、内存、系统盘和网络速度；
- 统计今日前台应用使用时间；
- 空闲和锁屏时间不会计入应用使用时长；
- 支持系统托盘、开机启动和全局快捷键。

## 数据导出

工作与健康数据使用独立的导出入口，不会混入同一个文件。

| 数据范围 | 格式 | 导出内容 |
| --- | --- | --- |
| 今日工作 | XLSM | 任务、详细工作内容、状态、进度、预计时长、专注时长和时间字段 |
| 今日工作 | DOCX | 排版后的工作日报，适合阅读、打印和归档 |
| 今日健康 | XLSX | 健康汇总与逐条明细，包括总次数、总量和每次发生时间 |
| 长期计划 | XLSX | 甘特视图、状态下拉及可再次导入的任务数据 |

## 界面形态

- 默认固定在屏幕左侧，使用 Windows AppBar 预留真实工作区；
- 桌面文件与普通软件窗口不会进入助手占用的区域；
- 白色高对比界面，只保留一个蓝色强调色；
- 任务进度使用白色空轨道和蓝色进度填充；
- 支持纵向滚动、键盘操作和 Windows UI Automation 读屏标签。

## 技术栈

- .NET 8
- WPF
- Windows AppBar、系统托盘、全局快捷键等原生能力
- Open XML SDK：XLSM、XLSX 和 DOCX 导入导出
- 本地 JSON 数据存储

## 目录结构

```text
desktop-helpworker/
├─ FloatMate/
│  ├─ Controls/                 长期计划页面
│  ├─ Services/                 系统监控、本地存储、导入导出与 Windows 集成
│  ├─ design-system/            视觉与交互规范
│  ├─ App.xaml(.cs)             启动、托盘及辅助命令入口
│  ├─ MainWindow.xaml(.cs)      左侧助手主界面
│  └─ Models.cs                 本地数据模型
├─ CHANGELOG.md                 版本更新记录
├─ 桌面助手产品规划.md          产品与功能规划
├─ setup.ps1                    首次安装与环境准备
├─ build.ps1                    自包含版本发布脚本
├─ run.ps1                      开发运行脚本
├─ 首次安装.cmd                 新电脑双击安装入口
├─ 备份数据.cmd / 恢复数据.cmd  个人记录迁移入口
└─ 启动浮岛.cmd                 Windows 快速启动入口
```

## 新电脑快速开始

克隆仓库后，直接双击：

```text
首次安装.cmd
```

脚本会检查 .NET 8 SDK。电脑中没有 SDK 时，会从微软官方下载到仓库内的 `.tools` 目录，然后构建一个位于 `dist\FloatMate` 的 Windows x64 自包含版本，并创建桌面快捷方式。

以后可以双击 `启动浮岛.cmd`。如果尚未完成首次构建，启动脚本也会自动进入安装流程。

命令行方式：

```powershell
git clone https://github.com/xxxlibraaa-commits/desktop-helpworker.git
cd desktop-helpworker
.\setup.ps1
.\run.ps1
```

## 下载已构建版本

不需要修改代码时，可以从 [GitHub Releases](https://github.com/xxxlibraaa-commits/desktop-helpworker/releases) 下载 `FloatMate-v0.4.1-win-x64.zip`：

1. 解压 ZIP；
2. 双击 `FloatMate.exe`；
3. 首次启动后按需要开启开机启动。

发布包是 Windows x64 自包含版本，不要求电脑预装 .NET Runtime。个人目标、计划和健康记录仍保存在 `%LOCALAPPDATA%\FloatMate\data.json`，升级应用不会覆盖这些数据。

## 开发运行

开发模式需要 .NET 8 SDK。`run.ps1` 会依次查找仓库内工具链、原开发环境工具链和系统 `PATH`。

```powershell
dotnet restore .\FloatMate\FloatMate.csproj
dotnet run --project .\FloatMate\FloatMate.csproj
```

也可以在项目根目录运行：

```powershell
.\run.ps1
```

## 构建发布

```powershell
.\build.ps1
```

默认生成无需预装 .NET Runtime 的自包含单文件版本：

```text
dist\FloatMate\FloatMate.exe
```

如果只需要体积更小、依赖系统已安装 .NET Desktop Runtime 的版本，可以运行：

```powershell
.\build.ps1 -FrameworkDependent
```

该版本输出到 `dist\FloatMate-framework-dependent`，不会覆盖默认的自包含版本。

## 更换电脑与个人数据迁移

### 在新电脑安装程序

1. 在新电脑安装 Git；
2. 克隆仓库并进入目录；
3. 双击 `首次安装.cmd`；
4. 安装完成后双击 `启动浮岛.cmd`。

```powershell
git clone https://github.com/xxxlibraaa-commits/desktop-helpworker.git
cd desktop-helpworker
.\setup.ps1
```

首次安装会优先使用电脑中已有的 .NET 8 SDK。如果没有，会从微软官方下载到仓库的 `.tools\dotnet` 目录，然后生成位于 `dist\FloatMate` 的 Windows x64 自包含版本。因此最终应用不依赖新电脑预先安装 .NET Runtime。

### 迁移个人记录

个人记录不会自动进入 GitHub。推荐通过 U 盘、加密网盘或其他可信私人渠道单独迁移。

旧电脑：

1. 从系统托盘退出 FloatMate；
2. 双击 `备份数据.cmd`；
3. 复制 `migration-data` 中最新的 `FloatMate-data-*.json`。

新电脑：

1. 完成 FloatMate 首次安装；
2. 从系统托盘退出 FloatMate；
3. 将备份文件放进仓库的 `migration-data` 文件夹；
4. 双击 `恢复数据.cmd`；
5. 重新启动 FloatMate。

恢复前，如果新电脑已经存在记录，脚本会先在 `migration-data` 中生成 `pre-restore-*.json` 备份。

### GitHub 保存范围

GitHub 仓库包含：

- 应用源代码；
- 安装、构建、启动和迁移脚本；
- 产品规划、设计规范、README 和更新记录。

以下目录受 `.gitignore` 保护，不会被正常提交：

- `.tools`：首次安装下载的 .NET SDK；
- `dist`：本机构建结果；
- `migration-data`：个人数据备份；
- `bin`、`obj`：编译缓存；
- `.codex-temp`、预览图和测试输出。

## 本地数据与隐私

应用数据默认保存在：

```text
%LOCALAPPDATA%\FloatMate\data.json
```

- 目标、健康、长期计划和应用使用时间不会自动上传；
- 导出文件只有在用户主动点击导出时才会生成；
- GitHub 仓库只保存源代码和说明文档；
- `.tools`、`dist`、`migration-data`、`bin`、`obj`、预览图和本地测试输出均通过 `.gitignore` 排除。

## 设计原则

- 本地优先：离线也能完整使用；
- 低打扰：提醒只陈述事实，可忽略或延后；
- 一眼可读：明确区分主信息、次信息和元数据；
- 克制用色：未完成使用中性灰，不用红色制造压力；
- 可复盘：工作与健康记录相互独立，并保留完整时间线；
- 用户掌控：模块可隐藏，提醒和开机启动均可关闭。

更详细的规划请查看 [桌面助手产品规划.md](./桌面助手产品规划.md)，版本变化请查看 [CHANGELOG.md](./CHANGELOG.md)。
