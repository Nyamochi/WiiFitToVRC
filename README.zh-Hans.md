# WiiFitToVRC

[日本語](README.md) | [English](README.en.md) | [한국어](README.ko.md) | **简体中文** | [繁體中文](README.zh-Hant.md)

这是一款将 Wii 平衡板变成 VRChat(或其他 Windows 应用程序)行走控制器的应用。只需站在平衡板上移动体重,就能将前进、后退、转向、跳跃、下蹲转换为键盘/鼠标输入、虚拟 Xbox 360 手柄输入,或 VRChat 自带的 OSC 输入。

## 简单上手(非技术用户指南)

完全不需要任何编程知识,只需以下几步即可使用。

1. 开启 Windows 的蓝牙功能(如果没有蓝牙天线,请另外准备 USB 蓝牙适配器等)。
2. 点击本仓库顶部的 `WiiFitToVRC.exe` 下载(无需安装)。
3. 双击下载的文件即可运行。
4. 应用启动后会自动开始搜索。只需按下 Wii 平衡板电池盒内的 **SYNC** 按钮即可自动连接(无需点击连接按钮)。由于规格限制,POWER 按钮无法用于此用途,因此每次连接都需要按 SYNC 按钮。
5. 连接完成后,校准也会自动开始。画面会显示“请将设备放在地板上”,5秒后自动开始校准,请在此之前把平衡板放在地板上并保持空载。
6. 校准完成后,站上平衡板像平常一样站立片刻即可完成准备。之后启动 VRChat,在平衡板上移动体重即可行走。

提示:若要确认连接是否正常,可以打开记事本并踩上平衡板 —— 如果输入了 w/s/a/d,说明连接正常。如果在 VR 模式等情况下游戏仍然没有反应,请先开启 VRChat 自身的 OSC 功能,再尝试在设置中将输出方式切换为 VRChat OSC。

如果无法正常运作,可参考 [docs](docs/) 文件夹(英文)中各功能的详细说明。

## 运行环境

- Windows 10/11
- Wii 平衡板(蓝牙)— 已停产,但在二手市场很容易以低价购得
- 支持 HID 设备的蓝牙适配器

### 使用虚拟手柄时(可选)

[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases)(这是一个真实的内核驱动程序 — 本应用无法自动为您安装,请自行下载并安装)

## 注意

跳跃动作超出了 Wii 平衡板原本的规格范围,请注意周围环境,避免损坏设备或地板,
体重超过 100kg 的用户请不要在平衡板上跳跃。即使是轻微的跳跃也会被识别。

## 特点

- **无需输入 PIN 即可完成蓝牙配对** — 原理请参见 [docs/BALANCE_BOARD.md](docs/BALANCE_BOARD.md)(英文)。
- **两阶段校准**:一次性的传感器零点校准(需走下平衡板进行),以及在后台持续自动更新的“基准体重”(即使换人站上去也能立刻跟上)。
- **前进、后退、冲刺、左右转向、跳跃、下蹲动作检测** — 各项判定逻辑及可调设置请参见 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md)(英文)。
- **四种输出模式**:
  - 键盘(转向使用 Q/E 键)
  - 键盘+鼠标(转向使用鼠标视角移动 — 默认)
  - 使用 VRChat 的 OSC 功能 — 适用于 VR 设备锁定输入、连虚拟手柄在内的一切合成输入都无法接受的环境。详情请参见 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(英文)。
  - 虚拟手柄 — 适用于会拒绝 SendInput 合成键盘/鼠标输入的游戏(包括 VRChat)。详情请参见 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)(英文)。
- 按键绑定/手柄分配、转向灵敏度、重量阈值、各类时间参数等均可在应用内设置窗口中精细调整。
- 多语言界面:自动检测 Windows 显示语言,内置日语、英语、简体/繁体中文、韩语、法语、德语、意大利语。

## 也可用于其他游戏

本应用的输出是普通的键盘 WASD(或鼠标)输入,因此即使没有官方支持,只要游戏支持 WASD 移动,就可以在其他以行走为主的游戏中使用。已尝试可用的例子:

- Death Stranding
- Resident Evil
- Monster Hunter
- Armored Core IV

## 从源码构建

需要 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```
dotnet build WiiFitToVRC.sln
```

若要生成部署在仓库根目录的自包含单文件 exe:

```
powershell -File publish.ps1
```

## 项目结构

```
WiiFitToVRC.exe          预构建的自包含可执行文件(由 publish.ps1 生成)
publish.ps1               重新构建并重新部署 WiiFitToVRC.exe 的脚本
src/
  WiiFitToVRC.Core/        核心逻辑:蓝牙配对、HID 通信、动作检测、
                           设置、多语言化、输出(键盘/鼠标/手柄/OSC)
  WiiFitToVRC.App/         WinForms 界面(监视窗口 + 设置对话框)
tools/
  PairTool/                单独测试平衡板配对的控制台工具
  ClassifyTest/             离线回放工具:对录制的 CSV 日志重新运行判定逻辑,
                           无需实机即可调整阈值
reference/
  WiiBalanceWalker_v0.4/    InTheHand.Net.Personal.dll(32feet.NET),用于蓝牙设备管理
                           — 版权说明请参见附带的 README.txt
docs/                      (目前仅提供英文版)
  BALANCE_BOARD.md          平衡板蓝牙/HID 协议详情
  GESTURE_DETECTION.md      各动作的判定方式及相关调整设置
  VRCHAT_INPUT.md           普通 SendInput 在 VRChat 中无效的原因,以及三种解决方案
```

## 设置参考

所有设置均可在应用内设置窗口(⚙ 設定)中编辑,并保存到与 exe 同目录下的 `settings.json` 中。无需手动编辑,以下是概要:

| 设置项 | 作用 |
|---|---|
| 输出方式 | 键盘 / 键盘+鼠标 / VRChat OSC / 虚拟手柄(详见 [docs/VRCHAT_INPUT.md](docs/VRCHAT_INPUT.md)) |
| 语言 | 界面显示语言,也可设为自动跟随 Windows 设置 |
| 反应灵敏度(走路/冲刺/转向/跳跃/下蹲/步幅) | 均可分别独立用"弱"~"强"(步幅为"窄"~"宽")滑块调整(不影响前进/后退)。中间(默认值)保持原有判定基准不变。冲刺/转向/跳跃/下蹲这四项若将"弱"调到最小(0),则无论判定结果如何都完全不会输出按键 — 详见 [docs/GESTURE_DETECTION.md](docs/GESTURE_DETECTION.md) |
| 转向方式 | 可在"长按"(向左右倾斜并保持)与"踏步"(对角面板交替踩踏,默认)之间切换转向判定逻辑 |
| 转向灵敏度 | 鼠标移动量(键盘+鼠标模式)或摇杆偏移%(手柄模式),左右可分别设置 |
| 触发所需的重量阈值 | 判定为“有人站在平衡板上”的校准后总重量 |
| 休眠/唤醒所需秒数 | 输出锁定/解锁前需要维持的时间(双向共用) |
| 转向动作启用 | 关闭后将完全不检测转向,无论键盘、鼠标、手柄还是 OSC 输出模式都不会发送任何相当于左右转向的动作(不影响前进/后退/冲刺) |
| 调试模式 | 显示用于为 `ClassifyTest` 录制日志的原始数据记录控件 |
| 按键绑定标签页 | 键盘输出模式下各动作对应的按键(包括冲刺修饰键) |
| 手柄标签页 | 虚拟手柄模式下各动作对应的按钮及摇杆偏移量 |

## 许可证

本项目自身代码采用 [MIT](LICENSE) 许可证。附带的 `InTheHand.Net.Personal.dll` 是第三方库(32feet.NET)— 版权说明请参见 [reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt](reference/WiiBalanceWalker_v0.4/WiiBalanceWalker_v0.4/README.txt)。

改进点·修正意见请联系制作者的 X:[@nyamo_chi](https://x.com/nyamo_chi)
