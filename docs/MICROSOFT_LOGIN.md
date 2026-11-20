# 配置 TMCL 微软登录

本文适用于包含 `Tmcl.Auth` 模块的 main 开发构建：[下载页面](https://github.com/tourio114/tmcl/actions/workflows/build.yml)。已发布的 v0.10 尚不包含本次新增的设备代码与账号恢复功能。

TMCL 的微软登录入口和客户端流程已实现。真正完成在线账号验证，还需要你自己的 Microsoft 应用 Client ID，以及该应用访问 Minecraft 服务的资格。没有配置时，离线账号和游戏下载仍可正常使用。

## 先准备什么

- 一个用于应用注册、具有相应 Entra 目录权限的账号。
- 用于实际登录的个人 Microsoft 账号及 Minecraft Java Edition 游戏资格。
- 自己注册的 **Application (client) ID**。这是公开应用标识，不是 Client Secret，也不是账号密码。

应用开发者只需为自己的启动器注册应用；最终玩家使用已经配置好的应用登录。不应要求每位玩家注册一个 Azure 应用。目前 TMCL 提供设置入口供开发者配置，并未内置其他启动器的 ID。

## 注册应用

1. 打开 [Microsoft Entra 管理中心](https://entra.microsoft.com/)，进入“应用注册 → 新注册”，名称可以填 TMCL。
2. “支持的账号类型”选择 **仅个人 Microsoft 账号**；包含个人账号的多租户类型也可，但不要只选组织内部账号。TMCL 使用 consumers 授权端点。
3. 在“身份验证 → 添加平台”中选择 **移动和桌面应用程序**。系统浏览器方式的回调地址填写 **`http://localhost`**。
4. 如果要使用 TMCL 的**设备代码**方式，在身份验证的高级设置中将 **Allow public client flows / 允许公共客户端流**设为 Yes。系统浏览器回调流程与设备代码流程不同，不要把应用配置成需要 Client Secret 的机密 Web 客户端。
5. 复制概述页中的 **应用程序（客户端）ID**。

以微软的 [应用注册说明](https://learn.microsoft.com/en-us/entra/identity-platform/quickstart-register-app) 和 [桌面应用配置说明](https://learn.microsoft.com/en-us/entra/identity-platform/scenario-desktop-app-configuration) 为准。微软当前的注册说明列出了 Azure 订阅、目录与角色要求；如果个人账号不能进入应用注册，需要先解决该账号的目录/注册权限，启动器无法代为绕过。

## 确认 Minecraft 服务访问权限

注册 Azure 应用本身并不保证 Minecraft 服务接受此 Client ID。不要把 Microsoft Graph 的 User.Read 权限或一次微软网页登录成功，等同于 Minecraft 登录已经成功。

可在浏览器中查看微软的 [应用注册信息入口](https://aka.ms/AppRegInfo) 与 [Minecraft 应用审核入口](https://aka.ms/mce-reviewappid)，按当前官方要求处理。

2026-11-20 核对时，审核短链接仍跳转到 Microsoft Forms，HTTP 页面能够返回，但工具只能读取 Forms 框架，无法确认表单正文、是否接受新申请、具体审批条件或批准时间。这里不保证该表单当前可提交，也不保证应用一定获批。遇到 `Invalid app registration`，应核查应用访问资格，不能通过换用其他启动器的 ID 处理。

## 在 TMCL 中登录

1. 点击账号卡片里的“配置微软登录”，或进入“启动器设置 → 账号”。
2. 填入 Client ID，选择“系统浏览器（推荐）”或“设备代码”，点击“保存设置”。
3. 回到开始游戏页，点击“登录微软账号”。
4. 系统浏览器方式：在浏览器中完成登录与授权，保留启动器运行，等待它接收 localhost 回调。
5. 设备代码方式：打开启动器显示的微软验证地址，输入显示的代码，再完成账号授权。设备代码会过期，取消后需要重新获取。
6. 显示 Minecraft 用户名后，即可使用微软账号启动游戏。点击“切换微软账号”会请求新的交互授权；普通登录会优先恢复单个已缓存账号。

设备代码需要应用允许该流程；不能保证对所有已注册应用都可用。浏览器方式不要求安装 WebView2。

## 缓存、恢复与退出

- MSAL 授权缓存按 Client ID 放在 `TMCL/auth/<ClientId>/` 下，由 Windows DPAPI、macOS Keychain 或 Linux Secret Service 保护。
- 系统安全存储不可用时仅保留内存授权，界面会显示提示，不降级为明文令牌文件。
- 启动在线游戏时只尝试静默恢复/刷新；没有授权或需要交互时会提示先登录，不会悄悄打开浏览器。
- 多个缓存账号且无法明确选择时，要求交互确认，避免自动登录错误的账号。
- 退出会清除本地授权。即使缓存清理失败，也会立即清空本次会话并切回离线账号；可再次点击退出重试清理。
- 退出启动器账号不会注销浏览器中的 Microsoft 网站会话。切换账号时请在微软页面确认所选账号。
- 新版按应用隔离缓存；从旧版升级首次使用可能需要重新登录。

实现参考微软的 [静默获取令牌](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/acquire-token-silently)、[系统浏览器](https://learn.microsoft.com/en-us/entra/msal/dotnet/acquiring-tokens/using-web-browsers) 和 [MSAL 流程支持](https://learn.microsoft.com/en-us/entra/msal/msal-authentication-flows) 文档。

## 常见问题

| 提示/现象 | 检查方法 |
| --- | --- |
| 未配置 Client ID | 填写自己的应用 ID；离线账号不需要配置 |
| 应用配置错误、回调不匹配 | 检查个人账号类型、桌面平台、`http://localhost`，不要填写 Client Secret |
| Invalid app registration / Minecraft 服务拒绝应用 | 核查 Client ID 的 Minecraft API 访问资格 |
| 没有可恢复的授权 | 点击登录，重新完成交互授权 |
| Xbox 档案不存在 | 先到 xbox.com 完成该微软账号的 Xbox 档案设置 |
| 儿童/家庭或地区限制 | 在 Xbox / Microsoft 账号中检查相应状态 |
| 找不到 Java 版档案 | 确认拥有 Java 版，且登录的是正确的账号并已建立游戏档案 |
| 浏览器完成后没返回 | 保持启动器打开，检查 localhost 回调；也可尝试设备代码方式 |
| 安全存储不可用 | 本次仍可登录，但关闭后需要重新授权；Linux 可检查 Secret Service / 系统钥匙串 |

## 验证范围

已进行编译、登录编排/错误处理/取消/缓存退出的自动化测试，以及无 Client ID 时的 UI 验证。测试使用合成账号会话与假的后端，不会连接真实微软账号，不能代替真实 OAuth、Xbox 或 Minecraft 服务端授权测试。

目前尚未拿到用户自己的已授权 Client ID，因此真实微软账号的端到端登录仍待验证。代码不会记录原始 OAuth 异常、设备代码或在线游戏启动参数。
