# Overlook

[English](README.md) | **简体中文**

一款面向小尺寸屏幕的传感器面板应用，使用 **.NET 10** 和 **Avalonia UI** 重写，支持 **Windows 和 Linux**。

* 传感器以 LibreHardwareMonitor 的路径来标识，例如 `ROGPC2/Intel Core i5-14600K/Load/CPU Total`，数据来自远程的 **LibreHardwareMonitor**（Options > Remote Web Server），默认地址为 `http://127.0.0.1:8085/data.json`。
* **Linux** 还可以读取本机自己的传感器，无需 Windows 电脑。在 设置 > 传感器来源 中勾选“改用这台电脑自己的传感器”（Linux 上默认开启），即可读取 `/proc` 和 `/sys`（CPU 负载、频率和温度，主板的风扇、电压和温度芯片，NVMe/SATA 硬盘温度，AMD 显卡，内存，网络），NVIDIA 显卡则通过 `nvidia-smi` 读取。传感器使用相同的 `Computer/Hardware/Group/Name` 路径，因此同一个传感器选择器和主题都能直接使用。部分数值需要权限：CPU 功耗（RAPL）通常仅限 root 读取，无法读取时会被跳过。如果缺少主板芯片，请安装 `lm-sensors` 并运行 `sensors-detect`。
* 主题为 **JSON** 格式（`theme.json` + `assets/`）。1.x 的 XML 主题可以用内置的导入功能或 `bigbro-convert` 转换。

## 运行
    dotnet run --project src/BigBro.App
选择一个主题，点击 **显示面板**。拖动面板即可移动：它会保持在屏幕内，并自动吸附到屏幕边缘；右键可查看更多选项。
打开 **设置** 可配置 LibreHardwareMonitor 地址（带连接测试）、天气坐标、语言、开机自动启动、托盘等选项。

## 内置主题

NVIDIA，另外还有十个主题，专为支持的显示器尺寸设计：1920x480（8.8 英寸）、800x480（5 英寸）和 1024x600（7 英寸），每种尺寸也有旋转后的版本（480x1920、480x800、600x1024）。它们使用通用传感器（`@CpuLoadPercent`……），因此在任何电脑上都无需设置即可使用。

| 主题 | 尺寸 | 风格 |
|---|---|---|
| Aurora | 1920x480 | 叠在 RGB 灯光上的玻璃质感卡片（需开启 RGB 背景） |
| Neon Grid | 1920x480 | 合成波风格的落日与透视网格 |
| Minimal | 480x1920 | 黑色背景、纤细字体、五个纵向排列的区块（竖屏） |
| Carbon | 1920x480 | 碳纤维质感，带刻度的橙色和蓝色仪表 |
| Pulse | 1920x480 | 音频可视化，顶部显示 CPU、GPU 和内存 |
| Glacier | 1024x600 | 浅色主题，白色卡片 |
| Terminal | 1024x600 | 绿色荧光终端，配条形图 |
| Sunset | 800x480 | 暖色渐变、大号时钟、三个仪表 |
| Gamer HUD | 800x480 | GPU 与 CPU 仪表之间显示大号 FPS 和帧时间（Windows 上需要 MSI Afterburner） |
| Amber LCD | 600x1024 | 琥珀色七段数码管，带未点亮笔画的底影（竖屏） |

它们由 `tools/generate_themes.py`（Python 和 Pillow）生成，这也是用代码制作自己主题的最简单方式。

## 文件存放位置

* **你的文件**（设置、自己的主题、字体、语言）在 Windows 上位于 `%AppData%\Overlook`，在 Linux 上位于 `~/.config/Overlook`。
* **示例主题**（NVIDIA）随程序一起提供，从可执行文件旁边的 `Themes` 文件夹读取。Overlook 不会把它们复制到你的文件夹中，所以在那里删除某个主题就是彻底删除。
* **两处的主题** 都会显示在列表中。如果两处有同名主题，则使用你自己的那个。
* **编辑示例主题** 并保存时，修改后的副本会存入你自己的 `Themes` 文件夹，原件保持不变。
* **Windows 便携版压缩包**：其中包含一个 `portable.txt` 文件，因此所有数据都保存在程序旁边的 `Data` 文件夹中，而不是 `%AppData%`。删除 `portable.txt` 即可改回使用 `%AppData%`。

## 下载

[发布页面](https://github.com/qiangqiang101/Overlook/releases)

## 主题编辑器
点击主窗口上的 **主题编辑器...** 即可打开。可在画布上点击或拖动元素（拖动控制点调整大小），并在侧边面板中编辑每一项属性；
通过 **文件 / 编辑 / 添加 / 查看 / 工具** 菜单可以：添加、复制、删除元素并调整它们的层次，把元素嵌套到父级之下，对齐网格（对齐网格 / 显示网格 / 网格大小），撤销/重做（Ctrl+Z / Ctrl+Y），保存（Ctrl+S）或另存为副本。
面板已连接时，它会用实时数据预览，否则使用内置的示例电脑。
**导入 1.x 主题也在这里**（文件菜单：*导入 1.x 主题* / *导入 1.x 主题文件夹*）。1.x 主题按名称（如“CPU load”）引用硬件传感器，因此导入时会弹出一个窗口，让你为每一项确认或更换对应的 LibreHardwareMonitor 传感器；当能够连接到你的电脑时，会给出建议。也可以在终端中执行：

    dotnet run --project src/BigBro.ThemeConverter -- "<old>\Themes" -o Themes --sensors http://192.168.0.8:8085/data.json

`--sensors`（你电脑的 data.json 文件或网址）会把旧的传感器绑定到那台电脑上对应的传感器；不提供时，它们将保持未绑定。

主题所需的字体必须已安装，或放入 `Fonts` 文件夹（当导入器找到 1.x 安装目录时，会从中复制字体）。
格式详情：[docs/theme-format.md](docs/theme-format.md)。

## 额外要求
* **音频可视化** - Windows：无需任何东西。Linux：`parec`（软件包 `pulseaudio-utils`，或 `pipewire-pulse`）。
* **YouTube 元素** - Windows：WebView2 运行时。Linux：WebKitGTK（`libwebkit2gtk-4.1`）。视频必须允许嵌入。播放器托管在一个很小的本地网页服务器（127.0.0.1）上，因为 YouTube 会拒绝没有来源页（referrer）的嵌入。
* **FPS / 帧时间** - 需要在与 Overlook *同一台* Windows 电脑上运行 MSI Afterburner + RivaTuner，并启用 Framerate 计数器。

## 命令行工具
    Overlook --render <theme folder> --out shot.png --sensors data.json [--samples 60] [--audio-demo] [--wait 500]
    Overlook --screenshot main|settings|editor --out shot.png [--theme NAME] [--select ELEMENT] [--data <data dir>]

## 限制
* 日期/时间、ping、运行时间、磁盘大小、屏幕分辨率、刷新率和 FPS 描述的是运行本应用的这台电脑，而不是被监控的电脑。
* 内存速度由内存条的 SPD 时序推算得出。在 Linux 上，显示器刷新率需要 `xrandr`。
* YouTube 播放器是原生网页视图，因此始终位于所绘制元素的上层。
