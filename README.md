# 屏幕准星 · ScreenCrosshair

一个轻量的 Windows 屏幕准星工具。双击运行，在显示器正中央显示透明、置顶且可穿透鼠标点击的准星。

[下载最新版 EXE](https://github.com/JackyWilliam/ScreenCrosshair/releases/latest/download/ScreenCrosshair.exe) · [版本与完整压缩包](https://github.com/JackyWilliam/ScreenCrosshair/releases)

![准星设置界面](docs/settings.png)

## 功能

- 始终置顶，显示和隐藏时不抢走当前程序的键盘焦点。
- 鼠标点击穿透准星，通知区域图标提供设置和退出入口。
- 十字、圆点、圆环三种样式，六种颜色，可调整长度、间隔、粗细、描边和中心点。
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

## 构建

在 Windows PowerShell 中运行：

```powershell
.\source\build.ps1
```

脚本使用 Windows 自带的 .NET Framework C# 编译器，输出仓库根目录下的 `ScreenCrosshair.exe`。

## 验证

已在本机通过 35 项运行时断言，覆盖窗口样式、焦点保持、鼠标命中穿透、快捷键注册与处理、三块显示器居中、设置控件布局、设置持久化和退出后热键释放。交付 EXE 另行验证了重复启动、设置关闭后继续显示和隐藏恢复。具体游戏中的显示效果仍需实际使用验证。

实现参考：[Windows layered windows](https://learn.microsoft.com/en-us/windows/win32/winmsg/window-features)、[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)。
