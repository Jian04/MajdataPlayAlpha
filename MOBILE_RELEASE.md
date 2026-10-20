# MajdataPlayAlpha 0.6.1 移动端构建与自签

移动端运行的是 Play，WPF Edit 和 Launcher 不包含在 APK / IPA 中。

## 构建环境

Windows 使用 Unity 6000.6.0f1 生成 Android APK 和 iOS Xcode 工程。先安装 Android Build Support、Android SDK & NDK Tools、OpenJDK、iOS Build Support，再运行：

```powershell
python Tools/Build/prepare_mobile_release.py
```

脚本只处理 `.build/unity-check` 独立构建副本，移除上游示例歌曲、皮肤导入记录与文档。源项目保留；运行时资源清单、语言和设置 JSON 保留。移动端资源哈希清单会在构建时重新生成。

## Android

Android 沿用原仓库 .github/workflows/main.yml 的独立 APK 分支：Unity 导出 Gradle 工程，Gradle assembleRelease --build-cache 打包；target SDK 29，保留原版谱面文件夹访问方式。版本为 alpha.0.6.1 / 61，只输出 ARM64 APK。现有已导出的 Gradle 工程可通过 build_android.ps1 的 -ExportedProject 参数复用，避免重复编译原生库。

```powershell
./Tools/Build/build_android.ps1 `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' `
  -ProjectPath '.build/unity-check' `
  -OutputPath '.codex-build/Android/MajdataPlayAlpha-alpha.0.6.1-arm64.apk'
```

使用 ARM64 / IL2CPP，包名 `net.majdata.majdataplayalpha`，版本 `alpha.0.6.1`、版本代码 61，可与上游版本并存。APK 使用 Android 开发签名；后续覆盖安装必须保持同一签名。

在 Android 允许对应下载器或文件管理器安装应用，再打开 APK 安装。之后可导入谱面压缩包。

## iOS：先导出工程

```powershell
./Tools/Build/build_ios.ps1 `
  -UnityPath 'C:\Program Files\Unity\Hub\Editor\6000.6.0f1\Editor\Unity.exe' `
  -ProjectPath '.build/unity-check' `
  -OutputPath '.codex-build/iOS'
```

导出内容包含 `Unity-iPhone.xcodeproj`、Data、Libraries 等目录。整个文件夹复制到 Mac，不要只复制 `.xcodeproj`。导出工程还不是 IPA。Apple 的版本字段使用 `0.6.1` / build 61；游戏内显示 `alpha.0.6.1`，工程不绑定开发者账号。

## 在线生成 IPA：GitHub Actions

工作流：[Build unsigned iOS IPA](https://github.com/Jian04/MajdataPlayAlpha/actions/workflows/ios-cloud.yml)。标准 macOS 云机在公开仓库中免费；私有仓库使用账号额度，超出可能收费。本配置只手动运行，不自动发布，不需要 Apple 密码、证书或 Unity 激活密钥。[费用和机型说明](https://docs.github.com/en/actions/how-tos/write-workflows/choose-where-workflows-run/choose-the-runner-for-a-job)

1. 下载本次的 MajdataPlayAlpha-alpha.0.6.1-Xcode.zip，保持原 ZIP 不修改。它包含完整 Xcode 工程和 build_ios_ipa.sh。
2. 在自己的公开 GitHub 仓库创建一个 Release，上传该 ZIP 作为附件。复制附件的 HTTPS 下载直链。不要把解压后的 Data、Libraries 和 IL2CPP 大文件提交进 Git。
3. 将同目录的 ios-cloud.yml 复制到仓库默认分支的 .github/workflows/ios-cloud.yml。
4. 在本机计算 ZIP 的 SHA256：

~~~powershell
Get-FileHash -Algorithm SHA256 -LiteralPath 'D:\Maimai\release\MajdataPlayAlpha-alpha.0.6.1-Xcode.zip'
~~~

5. 打开仓库 Actions → **Build unsigned iOS IPA** → **Run workflow**，填写下载直链和 SHA256，然后运行。使用标准 macos-15-intel，最长 120 分钟；不选择付费 Larger runner。
6. 成功后，打开这次运行的 Artifacts，下载 MajdataPlayAlpha-alpha.0.6.1-unsigned-IPA。解压得到未签名 IPA，发给用户按下文自签即可。Artifacts 本配置保留 7 天，正式发布前将 IPA 另存到本机或自己的 Release。
7. 失败时下载 iOS-build-log 查看 Xcode 的原始错误。工作流运行结果、IPA 归档成功与 iPhone 实机测试分别确认。

[手动运行工作流](https://docs.github.com/en/actions/how-tos/manage-workflow-runs/manually-run-a-workflow) · [下载构建产物](https://docs.github.com/en/actions/tutorials/store-and-share-data)

备选：[Codemagic](https://codemagic.io/) 的个人账号每月有 500 分钟 macOS M2 免费额度。Team 不含这份额度；不启用付费即可先试。[官方额度说明](https://docs.codemagic.io/billing/pricing/)

## 在工作 Mac 上生成 IPA

1. 安装与 Mac 系统兼容的 Xcode，打开一次完成组件安装。整个流程不需要付费开发者会员；如果工作电脑限制安装软件，使用已有 Xcode 或联系电脑管理员。
2. 打开 `Unity-iPhone.xcodeproj` 确认工程能载入。在终端执行 `xcodebuild -version`，确认使用的是完整 Xcode。
3. 将 `build_ios_ipa.sh` 放在工程旁边，运行：

```bash
bash build_ios_ipa.sh /你的路径/MajdataPlayAlpha-iOS
```

脚本执行 Release / iphoneos 归档，关闭代码签名，再将生成的应用打包成 `Build/MajdataPlayAlpha-alpha.0.6.1-unsigned.ipa`。这个 IPA 供用户用自己的账号重新签名，不能直接点开安装。

如果 `xcodebuild` 指向 Command Line Tools，可在当前终端指定 Xcode，避免改变电脑全局配置：

```bash
export DEVELOPER_DIR=/Applications/Xcode.app/Contents/Developer
bash build_ios_ipa.sh /你的路径/MajdataPlayAlpha-iOS
```

Windows 只能确认工程导出；原生链接、归档和运行需在实际 Mac / iPhone 上验证。[Unity iOS 构建流程](https://docs.unity3d.com/6000.0/Documentation/Manual/ios-environment-setup.html)

## 自己的 iPhone：Xcode 免费 Personal Team

1. 用数据线连接 iPhone，信任电脑。iOS 16 及以上开启“设置 → 隐私与安全性 → 开发者模式”，按系统提示重启确认。
2. Xcode → Settings → Accounts，添加自己的 Apple 账号。
3. 选择主应用目标 `Unity-iPhone` → Signing & Capabilities，勾选 Automatically manage signing，Team 选自己的 Personal Team。
4. 如提示包名已占用，将主应用 Bundle Identifier 改成自己的唯一名称，例如 `net.yourname.majdataplayalpha`。
5. 运行设备选自己的 iPhone，点击 Run，按系统提示信任自己的开发者签名。

免费 Personal Team 的描述文件有效期为 7 天，每台设备最多安装 3 个此类应用，过期后需重新构建安装。[Apple 免费账号限制](https://developer.apple.com/help/account/basics/about-your-developer-account)

## 其他用户：AltStore Classic 自签 IPA

用户有 Windows 或 Mac 电脑即可；安装已经编译的 IPA 不要求用户安装 Unity 或 Xcode。

1. 从 [AltStore 官方网站](https://altstore.io/) 安装 AltServer。Windows 按[官方教程](https://faq.altstore.io/altstore-classic/how-to-install-altstore-windows)准备 iTunes / iCloud，具体版本和下载来源以该教程为准。
2. 数据线连接、解锁并信任 iPhone。在 AltServer 选择 Install AltStore → 自己的设备，用自己的 Apple 账号安装 AltStore Classic。账号密码由用户在签名工具中输入，不交给谱面作者。
3. 开启手机开发者模式，在“设置 → 通用 → VPN 与设备管理”信任自己的开发者签名。
4. 把 `MajdataPlayAlpha-alpha.0.6.1-unsigned.ipa` 保存到手机“文件”。打开 AltStore Classic → My Apps → `+`，选择 IPA 安装。
5. 免费签名约 7 天过期。在 AltServer 可连接时，用 My Apps → Refresh All 续签，建议到期前操作。AltStore 自身也占一个应用名额。[AltStore 刷新说明](https://faq.altstore.io/altstore-classic/your-altstore)

无需 TestFlight。用户分别用自己的 Apple 账号签名，不共用作者账号或证书。

## 手机验证

APK 构建成功、Xcode 工程导出成功、IPA 归档成功和手机实测是四个阶段，发布时分别记录。

手机至少检查：多点触控、D 区 / TouchSlide / Mine、噪域预警与禁用恢复、负 SV、滤镜帧率、音视频剪辑偏移、暂停切歌，以及切到后台后的音频恢复。
