# 屏幕准星 · ScreenCrosshair

一个轻量的 Windows 屏幕准星工具。双击运行，在显示器正中央显示透明、置顶且可穿透鼠标点击的准星。

[下载最新版 EXE](https://github.com/JackyWilliam/ScreenCrosshair/releases/latest/download/ScreenCrosshair.exe) · [版本与完整压缩包](https://github.com/JackyWilliam/ScreenCrosshair/releases)

![准星设置界面](docs/settings.png)

## 功能

- 始终置顶，显示和隐藏时不抢走当前程序的键盘焦点。
- 鼠标点击穿透准星，通知区域图标提供设置和退出入口。
- 十字、圆点、圆环三种样式，六种颜色，可调整长度、间隔、粗细、描边和中心点。
- 新增霰弹枪“圆环＋中心点”和狙击枪“去掉下方竖线”样式。
- 调色板与 `#RRGGBB` 自定义颜色；长度、间隔、粗细用滑块调整。
- 可选的 Apex 局部画面武器识别，自动切换霰弹枪 / 狙击枪 / 普通样式。
- 多显示器切换，按完整显示器尺寸居中。
- 设置即时生效并自动保存，重复运行会打开已有实例的设置。
- 无需安装、管理员权限或额外依赖；不设置开机启动。

## 使用

从 Releases 下载并运行 `ScreenCrosshair.exe`。默认在主屏显示绿色十字。

| 快捷键 | 操作 |
| --- | --- |
| Ctrl + Alt + F8 | 显示 / 隐藏 |
| Ctrl + Alt + F9 | 打开设置 |
| Ctrl + Alt + F10 | 切换显示器 |
| Ctrl + Alt + F11 | 退出程序 |

双击任务栏通知区域的准星图标也能打开设置，右击可退出。关闭设置窗口后，准星继续运行。快捷键如被其他程序占用，会提示使用托盘菜单。

设置保存到 `%LOCALAPPDATA%\ScreenCrosshair\settings.xml`。

适用于 Windows 10 / 11 桌面、窗口化或无边框窗口。独占全屏可能不显示，此时请改用无边框窗口；尚未针对具体游戏验证兼容性。

## Apex 自动切换（实验性）

1. 在 Apex 训练场拿起武器，确保右下角显示当前武器名称。
2. 按 `Ctrl+Alt+F9` 打开设置，选择游戏语言：简体中文或 English。
3. 点击“框选枪名区域…”，在 20 秒内切回 Apex，拖动框选**当前正在使用的那一行武器名称**。避开备用武器名称、弹药数字和拾取提示；按 Esc 取消。
4. 框选完成后自动启用识别。关闭设置并回到 Apex 即开始检查；可在设置中关闭自动模式。

识别到霰弹枪时使用圆环加中心点，狙击枪使用没有下方竖线的十字，其他武器使用“普通样式”。神射手武器归入普通样式。当前分类依据 [EA 武器指南](https://help.ea.com/zh/articles/apex-legends/guns-and-weapons/)。

默认每 1 秒检查一次，可选 0.5 / 1 / 2 秒。连续两次识别到同一武器才切换；连续三次无法识别时恢复普通样式。换枪因此存在延迟，较长间隔会降低识别次数，但增加切换和恢复时间。不要把未完成的换枪动画当成已切换。

只在 Apex 位于前台时读取选定区域。图片仅在进程内存中处理，用完释放，**不保存截图、不上传、不录屏**；同一时刻最多处理一张，繁忙时跳过检测，不积压任务。切出 Apex 会暂停并恢复普通样式。调整 HUD 位置、缩放、语言或窗口布局后，如识别不准，请重新框选。

使用 Windows 自带 OCR，需安装对应语言的文字识别组件；缺失时状态栏会提示并暂停识别。遇到黑屏、名字读不到或区域过大，使用无边框窗口并重新框选小范围的枪名。识别失败会保留普通准星，程序不绕过游戏的画面捕获限制。

这不是官方授权工具。EA 没有明确豁免外置准星或武器识别，不读内存或不注入游戏也不能保证合规或不会封号，参见 [Apex 官方规则](https://help.ea.com/en/articles/apex-legends/play-by-the-rules/)。真实游戏中的准确率、帧率影响和兼容性尚需用户验证。

## 构建

在 Windows PowerShell 中运行：

```powershell
.\source\build.ps1
```

脚本使用 Windows 自带的 .NET Framework C# 编译器，输出仓库根目录下的 `ScreenCrosshair.exe`。

## 验证

v1.1.0 在本机通过 40 项新增断言，覆盖中英文图片 → Windows OCR → 武器类别 → 稳定切换、未知与冲突文字、旧配置兼容、自定义颜色、滑块和识别设置序列化；另通过 34 项原生窗口回归并目视核查设置截图。合成图片测试不等同于真实 Apex 验收。

运行新增回归（会短暂显示自己的测试窗口，不会启动或操作游戏）：

```powershell
.\tests\run.ps1
```

可选的 `-Interactive` 测试会显示一个合成武器 HUD，验证屏幕局部捕获。运行时保持测试窗口在前台；若焦点被其他窗口切走，测试会失败。测试构建限制为只能捕获自身窗口，不能捕获正在运行的真实 Apex。

实现参考：[Windows layered windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)、[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)。
