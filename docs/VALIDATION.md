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
