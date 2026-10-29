# Multisim 数据库启动错误自动重试

一个 Windows 启动器，用于尝试绕过 NI Multisim 启动时**偶发**出现的“无法访问数据库”错误。启动器每次启动一个空白实例，识别到已知数据库错误时关闭该实例并重试；主界面稳定后停止。

**这是临时绕过办法，不是数据库修复工具。** 启动器不直接修改 Multisim、数据库文件或 Windows 区域设置；Multisim 自身启动时仍可能读写其数据。连续 8 秒没有启动错误，只能说明启动检查通过，不能保证所有元件库和仿真功能正常。

## 官方修复优先

NI 说明，Circuit Design Suite 14.3 Patch 3 修复了 Windows 11 24H2 及更高版本使用中文区域设置时的一种数据库访问问题。如果适用并且可以取得更新，优先考虑官方修复：

- [NI：Circuit Design Suite 14.3 Patch 3 说明](https://www.ni.com/en/support/documentation/supplemental/26/circuit-design-suite-14-3-patch-3-details.html)
- [NI：中文 Windows 11 下的数据库访问问题](https://knowledge.ni.com/KnowledgeArticleDetails?id=kA0VU000000DreP0AS)

数据库路径错误、文件权限或其他软件问题也可能产生类似提示；本项目不判断你的错误是否属于上述 NI 问题，也不保证重试有效。

## 运行环境

- Windows 10 或 11；已在 Windows 11 和 Multisim 14.3 专业版上进行过一次实机验证。
- 已安装并可正常启动的 Multisim；本项目**不包含** NI 软件、元件库或许可证。
- 构建需要 Windows 自带的 .NET Framework 4.x C# 编译器。测试还使用 Windows PowerShell 和 WinForms。
- 默认程序路径为 `C:\Program Files (x86)\National Instruments\Circuit Design Suite 14.3\multisim.exe`；其他位置可通过参数指定。

## 使用方法

1. 下载或克隆项目，将整个文件夹保留在一起。
2. 双击 `build.cmd`，生成 `dist\MultisimRetryLauncher.exe`。项目只提供源码；每台电脑首次使用时需要构建。
3. 双击 `启动Multisim.cmd`。如果 Multisim 被设置为以管理员身份运行，启动器会请求对应权限。
4. 等待启动器报告成功，再打开或编辑电路。可给 `启动Multisim.cmd` 创建桌面快捷方式。

如果 Multisim 已经运行，启动器会停止，避免关闭已有工作。启动失败时，它会**强制结束由它自己启动的失败实例**；因此请勿在重试期间使用该实例编辑电路。出现未知提示或激活窗口时，启动器会保留窗口并停止。按 `Ctrl+C` 可取消重试，当前实例会保留。

默认最多重试 10 次，每次最多等待 75 秒，间隔 2 秒；检测到主界面连续稳定 8 秒后保留该实例。达到次数上限后退出，不会无限重试。

如果安装位置不同，先运行 `build.cmd`，再在项目目录的终端中运行：

```cmd
dist\MultisimRetryLauncher.exe --exe "D:\Your Installation\multisim.exe"
```

其他可选参数：

```cmd
dist\MultisimRetryLauncher.exe --attempts 15 --timeout 100 --stable 12
dist\MultisimRetryLauncher.exe --help
```

`--attempts` 允许 1–30 次，`--timeout` 允许 3–180 秒，`--stable` 允许 1–30 秒，`--delay-ms` 允许 300–15000 毫秒；超时时间必须大于稳定观察时间。

## 测试

在交互式 Windows 桌面运行 `tests\run-tests.cmd`。测试编译并启动项目自带的模拟窗口，**不启动真实 Multisim**；完成后会关闭它启动的模拟窗口。涵盖失败后成功、达到上限、中文和英文错误、未知弹窗、启动画面、超时及保护已有实例。执行测试时不要同时运行其他同名 `LauncherProbe.exe` 测试程序。测试脚本只为本次 PowerShell 进程临时指定执行策略，不更改系统策略。

测试输出、构建结果与启动日志写在 `dist`，已通过 `.gitignore` 排除。日志可能包含本机软件路径和弹窗文字；如需分享，请先检查并删除个人信息。

通过 Git 上传时，`.gitignore` 会排除 `dist`；如果通过 GitHub 网页手动上传文件，需自行避开 `dist`、日志和个人数据库，因为网页上传不会自动应用 `.gitignore`。

此前在一台 Windows 11、Multisim 14.3 专业版电脑上，实机启动时第一次出现主数据库访问错误，自动重试后第二次通过启动检查。这只是一次实测记录，不代表其他电脑均可复现或适用。

## 退出码

| 代码 | 含义 |
| --- | --- |
| 0 | 主界面通过启动检查 |
| 1 | 其他错误，如程序路径不存在 |
| 2 | 达到重试上限 |
| 5 | 用户取消权限请求 |
| 10 | 已有同名程序在运行 |
| 11 | 另一个启动器正在运行 |
| 12 | 发现需要人工处理的未知提示 |
| 13 | 无法安全关闭本轮失败实例 |
| 130 | 用户取消 |

## 许可与声明

本项目原创代码以 [MIT License](LICENSE) 发布。Multisim 和 National Instruments 是其相应权利人的商标；本项目为独立项目，与 NI 无隶属或背书关系。

[English summary](README.en.md)
