# TMCL 开发与交接说明

## 用户目标与约束

- 用中文沟通，直接完成已授权的实现、修复、GitHub 上传与构建，避免重复确认和重复研究。
- 用户提醒用量有限：优先复用本文与现有代码；不要为文档修改重复运行六个平台构建。
- Minecraft 启动器，Windows / Linux / macOS，x64 / ARM64；默认 Minecraft **1.8.9**。
- 默认离线账号，微软登录可选。不得强制微软账号，不借用其他启动器的 Client ID。
- 自动检测 Java 主版本和架构，缺少时自动安装并校验 Temurin OpenJDK。
- 多线程下载、BMCLAPI / 官方源切换、测速、IPv4 地区检测，中国地区默认 BMCLAPI；手动选择优先。
- 两套界面：**Fluent 2** 与 Claude。每套支持浅色、深色、跟随系统，立即生效、保存、重启恢复。
- 最新待完成需求：参考 HMCL 的实现，给 TMCL 添加 **Forge / Fabric 安装功能**。
- 服务器只有约 4 GB 内存。优先测试 1.8.9；不要启动较新 Minecraft 进行高负载测试。1.8.9 实测使用 1024 MB 堆内存。

## 仓库与环境

- 仓库：https://github.com/tourio114/tmcl
- 本地：`/root/tmcl`，分支 `main`。
- .NET 8 SDK：`/opt/tmcl-dotnet/dotnet`，未必在 PATH 中。
- 已登录的 GitHub CLI 可直接使用。GitHub 插件曾返回空仓库列表，因此此前通过 `gh` 完成上传和发布。
- 不要输出凭据、访问令牌、OAuth 缓存、包含在线账号令牌的启动参数。
- 不创建或重写已有发布标签，除非用户明确要求；发布新版本需要用户授权。
- 不因本文自行启动子代理；当前任务没有用户授权的代理并行工作要求。

## 当前实现与已发布版本

- 桌面 UI：Avalonia 11.3.9，FluentTheme，非网页、非真正的 WinUI 3。
- 游戏核心：CmlLib.Core 4.1.0。
- 微软登录：CmlLib.Core.Auth.Microsoft 3.4.0、XboxAuthNet.Game.Msal 0.2.0，系统浏览器授权。用户尚无自己的 Client ID，不能宣称真实账号登录已实测。
- 程序程序集版本为 **0.10.0**。
- 已正式发布 **v0.10**：https://github.com/tourio114/tmcl/releases/tag/v0.10
- v0.10 指向提交 `c145bc9141adaab7a323ab77c5171e845a17dc1f`，包含六个平台包和 SHA256SUMS.txt。资产的大小与 GitHub 返回的 SHA-256 已核对。
- **v0.10 发布后**用户要求把 PowerToys 风格改成 Fluent 2；已完成并上传至 main，提交 `75c09aead47365239dfa256a9a5ecc0ab494effd`。
- Fluent 2 六个平台构建全绿：https://github.com/tourio114/tmcl/actions/runs/44522738262
- 现有 v0.10 标签及资产保持原发布内容；Fluent 2 更新目前从 Actions 开发构建下载，不能把旧 v0.10 资产说成已包含 Fluent 2。
- **Forge/Fabric 尚未实现**：截至编写本文，只有源代码研究，没有 ModLoaderService、加载器安装 UI，也没有引入 CmlLib Forge 安装依赖。不要把研究结果当成已完成的功能。

## 关键文件

- `src/Tmcl.App/MainWindow.axaml`：开始游戏 / 启动器设置两个页面，账号、版本、Java、下载源和外观控件。
- `src/Tmcl.App/MainWindow.axaml.cs`：UI 操作、RunOperation 互斥、取消、版本列表、启动、微软登录。
- `src/Tmcl.App/Appearance.cs`：Fluent 2 / Claude 配色与尺寸 token，修改风格应集中在这里。
- `src/Tmcl.App/App.axaml`：共享样式与设计器默认 token。
- `src/Tmcl.App/Settings.cs`：JSON 设置；旧 `InterfaceStyle = PowerToys` 自动迁移为 `Fluent2`；旧 `Dark` 偏好迁移为主题选项。
- `src/Tmcl.Core/VerifiedInstaller.cs`：1–32 路并发、SHA-1 / 长度校验、原子文件替换、重试与临时文件清理。已经完成的文件可复用，**没有单文件断点续传**。
- `src/Tmcl.Core/MirrorHandler.cs`：下载 URL 镜像转换、请求头保留、超时回退与短时间跳过慢镜像。
- `src/Tmcl.Core/JavaService.cs`：Java 检测、Adoptium API、SHA-256 校验和跨平台归档安装。
- `src/Tmcl.Core/DownloadSources.cs`：强制 IPv4 的地区检测、官方/BMCLAPI测速与推荐。
- `src/Tmcl.Core/FallbackVersionLoader.cs`：在线列表失败时回退本地已安装版本。
- `tools/Tmcl.Smoke`：实际下载、Java 安装和游戏进程测试。
- `tests/Tmcl.Tests`：当前 **31 项测试**，覆盖下载、镜像、取消、Java 解析与下载源选择。
- `scripts/package.py`：Windows ZIP、Linux TAR.GZ、macOS .app TAR.GZ；Mac 进行临时签名，没有公证。
- `.github/workflows/build.yml`：六种架构构建。两个 macOS 包均在 **macos-15-intel** 上交叉发布。
- `docs/VALIDATION.md`：验证记录与限制；README 有实际 UI 截图。

## 验证与构建

```bash
cd /root/tmcl
/opt/tmcl-dotnet/dotnet build -c Release
/opt/tmcl-dotnet/dotnet test -c Release --no-build
```

根据变更运行必要检查；通过后不要无故重复全量测试。UI 可用 `xvfb-run`、`xdotool`、ImageMagick `import` 检查，截图可用工具查看。模拟用户配置请隔离 `XDG_DATA_HOME`，不要污染真实账号或设置。

```bash
/opt/tmcl-dotnet/dotnet run --project tools/Tmcl.Smoke -c Release -- 1.8.9 /tmp/tmcl-189 --java-only
xvfb-run -a -s '-screen 0 1280x800x24' /opt/tmcl-dotnet/dotnet run --project tools/Tmcl.Smoke -c Release -- 1.8.9 /tmp/tmcl-189 --launch
```

`--launch` 会启动该测试创建的进程，30 秒后关闭它。已有 `/tmp/tmcl-189` 含完整 1.8.9、Java 8 与资源，可复用。

Linux 实测所需依赖包括 `x11-xserver-utils`（提供 xrandr）；缺失时旧 LWJGL 曾在 Xvfb 下因显示模式错误崩溃。安装后 1.8.9 成功运行 30 秒。服务器没有声卡，不能声称音频已验证。

开发提交推送 main 自动构建；纯交接文档可以用 `[skip ci]` 避免无谓构建。必须核对最新提交的六个平台结果，不把旧提交的成功当作当前成功。

## Forge / Fabric 研究结果（接下来从这里开始）

### 已读取的参考源码

- HMCL：https://github.com/HMCL-dev/HMCL，研究提交 `ade3d687bf435f593c05a0f8ac364b147c7827dc`。
- 本地只读参考：`/tmp/tmcl-hmcl-reference`。
- 关键文件：
  - `HMCLCore/src/main/java/org/jackhuang/hmcl/download/fabric/FabricInstallTask.java`
  - `HMCLCore/src/main/java/org/jackhuang/hmcl/download/forge/ForgeInstallTask.java`
  - 同目录 `ForgeOldInstallTask.java`、`ForgeNewInstallTask.java`、`ForgeRemoteVersion.java`
  - `HMCL/src/main/java/org/jackhuang/hmcl/download/HMCLDownloadProvider.java`
- HMCL 源码标记 `GPL-3.0-or-later WITH AdditionRef-HMCL`。当前 TMCL 为 MIT；参考工作流程并独立实现，不能直接复制 HMCL 源码后仍当作 TMCL 的 MIT 自有代码。

### 可复用的 MIT 库

- `/tmp/tmcl-cmllib`：CmlLib.Core 源码，含 `ModLoaders/FabricMC/FabricInstaller.cs`。
- `/tmp/tmcl-forge-reference`：https://github.com/CmlLib/CmlLib.Core.Installer.Forge ，当前源码/包版本 **2.0.0**，MIT。
- Forge 包尚未加入项目。可引入 `CmlLib.Core.Installer.Forge` 2.0.0。
- 公共 API：
  - `ForgeInstaller.GetForgeVersions(mcVersion)` 获取含安装器地址的版本（当前实现解析官网 HTML）。
  - `ForgeInstallerVersionMapper.CreateInstaller(ForgeVersion)` 返回 `IForgeInstaller`。
  - `IForgeInstaller.VersionName`；`Install(MinecraftPath, IGameInstaller, ForgeInstallOptions)`。
  - `ForgeInstallOptions` 有 JavaPath、FileProgress、ByteProgress、InstallerOutput、CancellationToken。
  - `ForgeInstaller.Install(...)` 的高级 facade 成功后会自动打开 Forge 广告页；接入前需了解该行为。可以用公开的底层安装器并由 TMCL 编排原版准备、Java 与依赖补齐。
- CmlLib 安装流程必须最后补调 `launcher.InstallAsync(installedVersionName)`；生成 profile 不等于完整安装。
- 库支持旧 Forge 和新版 processor 安装流程。不要只解压一个 installer JAR 就宣称支持现代 Forge。

### HMCL 值得参考的流程

1. 按所选原版游戏版本查询可用加载器；分别创建独立版本配置，避免 Forge/Fabric 叠装。
2. Fabric 获取官方加载器/profile 元数据，合并或继承原版、设置入口和依赖，下载依赖库。
3. Forge 下载并校验 installer，检查安装器声明的游戏版本；区分旧版 profile 与新版 processors；准备原版和 Java，再运行相应流程。
4. 版本配置只在安装成功后标记完成；取消/失败时不要留下看似已安装的半成品。
5. 官方源/BMCLAPI候选地址、缓存、校验与任务进度由公共下载层处理。

### 已验证的接口与注意事项

- Forge 官方 Maven 列表：`https://maven.minecraftforge.net/net/minecraftforge/forge/maven-metadata.xml`。
- BMCLAPI 对应游戏版本：`https://bmclapi2.bangbang93.com/forge/minecraft/1.8.9`，JSON 含 version、branch、files，installer 的 hash 是 SHA-1。
- **不要丢失 Forge branch**。1.8.9 推荐测试版本：loader `11.15.1.2318`，branch `1.8.9`，完整 Maven 版本 `1.8.9-11.15.1.2318-1.8.9`。
- 对应 installer：`https://maven.minecraftforge.net/net/minecraftforge/forge/1.8.9-11.15.1.2318-1.8.9/forge-1.8.9-11.15.1.2318-1.8.9-installer.jar`。
- 该 installer 的 `.sha1` 已请求成功，值 `ec0293ff0776b8831f2ed90511bab76e635dda0c`。
- Fabric：`https://meta.fabricmc.net/v2/versions/loader/{gameVersion}`；profile：`.../{gameVersion}/{loaderVersion}/profile/json`。
- **官方 Fabric 不支持 1.8.9**，该查询实测返回 HTTP 400。UI 应提示没有兼容版本；不要伪造兼容性。如以后支持 Legacy Fabric，应独立实现并明确命名。
- 镜像映射需扩展：Forge Maven → BMCLAPI `/maven`，Fabric Maven → `/maven`，Fabric Meta → `/fabric-meta`；沿用用户源选择和回退，不能把账号授权请求送到镜像。
- 加载器 profile 的 `JavaVersion` 可能为空，所需 Java 要沿 `IVersion.ParentVersion` 查找，不能一律默认 Java 8。现有启动/Java检查逻辑需要相应修复。
- `IVersion` 提供 `InheritsFrom`、`ParentVersion`。安装器应只接受原版基础版本，拒绝把加载器继续叠装在其他加载器上。

### 尚待完成的工作

- 实现 Core 加载器查询/安装服务和必要的 NuGet 依赖。
- UI 增加游戏基础版本、Forge/Fabric、加载器版本查询和安装入口；接入进度、取消、错误提示。
- 安装成功后刷新本地版本并选中安装结果；保留默认 1.8.9 和离线账号。
- 补齐下载源映射，验证 branch、profile 继承和安装失败清理。
- 实测 Forge 1.8.9；Fabric 用官方支持的版本测试查询/安装，不在这台低配服务器启动较新游戏。
- 更新 README / 验证记录，上传 GitHub 并核对 CI。没有用户新授权时不改写 v0.10 发布。
