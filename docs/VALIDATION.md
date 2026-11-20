# 验证记录

2026-11-19，在 Ubuntu 24.04 x64 上完成：

- Release 编译零警告、零错误；31 项自动化测试通过。
- Avalonia 窗口实际打开，加载版本列表，默认选择 1.8.9，黑白主题切换正常。截图见本目录。
- 通过 Adoptium API 自动下载 Temurin OpenJDK 8，校验 SHA-256、解压、检测 `java -version`，主版本与 x64 架构匹配；也验证过 OpenJDK 21 的自动安装。
- 从 BMCLAPI 安装并校验 Minecraft 1.8.9 的 757 项文件；镜像节点慢时回退官方，重新安装复用已有文件。
- Java 8、离线用户名、1024 MB 最大堆内存下启动 1.8.9，在 Xvfb 图形环境中加载纹理并保持运行 30 秒，随后测试程序关闭其启动的游戏进程。
- 首次图形测试缺少 `xrandr` 导致旧版 LWJGL 无法获取显示模式；补齐 `x11-xserver-utils` 后启动成功，依赖已加入 README。服务器无声卡，游戏以静音模式运行；未验证音频播放或服务器联机。
- IPv4 地区检测返回 US，默认推荐官方；同一资源测速官方约 0.49 MB/s、BMCLAPI 约 0.47 MB/s，推荐官方。该数字仅代表测试当时的网络，不保证其他地区的速度。
- GitHub Actions 首轮 Windows、Linux、macOS 的 x64 / ARM64 六种包构建全部成功；后续提交仍须以对应提交的 Actions 状态为准。

没有使用真实微软账号进行端到端授权测试，需要用户自行配置已获 Minecraft API 访问权限的 Client ID。Windows / macOS 已构建，尚未在真实桌面上启动游戏。ARM64 旧版 Minecraft 的原生库兼容性取决于游戏版本，不能由启动器构建成功推断。

## 双界面风格与主题验证

2026-11-19 更新：

- Release 编译零警告、零错误；31 项既有自动化测试通过。
- 在 Linux 原生 Avalonia 窗口中操作设置下拉框，检查 PowerToys 与 Claude 两套风格的浅色、深色、跟随系统六种组合。
- 实际验证运行中切换配色、边距、控件尺寸和品牌标识，主题与风格选择独立保存；选择 Claude + 跟随系统后重新打开应用，界面和设置选项正确恢复。
- 旧版设置仅保存 `Dark: true` 时，启动后迁移为深色主题，并使用默认 PowerToys 风格。
- 截图为实际桌面窗口截图，详见 `powertoys-light.png`、`powertoys-dark.png`、`claude-light.png`、`claude-dark.png`。
- 未在本次测试中模拟操作系统明暗主题的实时变化；“跟随系统”通过 Avalonia 的系统主题机制实现。

## Fluent 2 风格更新

2026-11-19：将默认 PowerToys 风格替换为 Fluent 2 风格，调整品牌蓝、中性色层次、4px 控件圆角、8px 卡片圆角、轻量阴影与间距；Claude 的独立配色与明暗模式继续保留。

实际窗口验证了旧 `PowerToys` 设置迁移至 `Fluent2`、浅色 / 深色显示、切换 Claude 后再返回 Fluent 2，以及明暗主题的保存。新版截图见 `fluent2-light.png` / `fluent2-dark.png`。此次更新通过开发构建分发，未改写既有 v0.10 标签和发行文件。


## 2026-11-20 微软登录重构

- 新增独立 Tmcl.Auth 模块，界面不再直接维护 MSAL / Xbox 登录上下文。
- 编译零警告、零错误；61 项自动化测试通过，其中 30 项新增登录测试使用合成会话和假的后端，不访问真实微软账号。
- 覆盖配置校验、Client ID 归一化和切换、静默恢复策略、账号切换策略、取消后不发布会话、离线会话不能冒充在线会话、退出失败清空会话且可重试、重启后清缓存、并发互斥、错误脱敏与验证地址限制。
- 在 Linux 原生窗口中点击无 Client ID 的微软登录，显示配置提示并保留离线账号；“配置微软登录”能定位设置字段；设备代码选项能够选择和保存。
- 尚未使用已获授权的真实 Client ID，浏览器 OAuth、设备代码、系统钥匙串写入和 Xbox/Minecraft 服务端授权没有端到端成功记录。自动化和界面检查不能代替这些验证。

- 对应代码提交 `fc786155b94e5cbfb13a8ed46bb4edb906750c7f` 的 Windows / Linux / macOS x64、ARM64 六种 GitHub Actions 构建均已成功：[构建记录](https://github.com/tourio114/tmcl/actions/runs/44644602959)。
