# OpenAW3D — scratch setting

根据本地 `recreation_information` 从空工程重建的热座战棋 setting。地图、坦克和建筑使用程序化几何体生成，游戏逻辑独立实现；本项目不是原版仓库，也不是原版的完整等价复刻。

## 运行与构建

- 本地验证环境：Windows、团结引擎 2022.3.62t15。
- 用编辑器打开工程，打开 `Assets/Scenes/MainMenu.unity` 后进入 Play。
- Windows 构建：编辑器菜单 `Tools > OpenAW3D > Build Windows`；构建脚本会初始化场景设置。
- 构建输出：`Builds/Windows/OpenAW3D.exe`。构建产物、引擎缓存和日志不进入 Git。

## 玩法与操作

红蓝双方热座轮流操作；移动坦克、攻击敌人、占领建筑并在己方基地购买增援。对手的单位与建筑同时归零时获胜。

- 左键选择/确认，右键取消或撤销尚未确认的移动。
- WASD / 方向键平移镜头，鼠标滚轮缩放，窗口内部边缘滚屏。
- 初始资金 8000，坦克价格 6000，每座己方建筑每回合收入 3000。
- 城市治疗 1 HP，基地治疗 2 HP；坦克上限 10 HP。

## 验证与已知差异

2026-10-04 使用真实游戏窗口完成一整局，第 8 天蓝方正常获胜；具体测试步骤及覆盖边界见 [PLAYTEST.md](PLAYTEST.md)。没有修改游戏数据强制判胜。

当前版本是可玩原型式复刻：14×12 简化地图、几何体模型、移动/伤害规则、建筑恢复时机及 UI 与原版存在差异，未接入原版音乐、音效、准星和路线箭头。教程阅读超过 2 秒可能错过首次 Day 1 横幅。本机试用引擎水印属于授权环境限制。测试通过不代表所有输入组合绝对无 bug。

## 来源与实现范围

游戏控制逻辑位于 `Assets/OpenAW3D.cs`，场景初始化和构建脚本位于 `Assets/Editor/`。复刻实现阶段只使用提供的玩法摘要、录像和输入序列；完成后才根据用户授权查看 [Torrunt/OpenAW3D](https://github.com/Torrunt/OpenAW3D) 源码作差异报告，当前上传版本没有因此移植原版逻辑。

`recreation_information` 为提供的参考材料，不宣称由本项目作者原创。OpenAW3D 原作者为 Corey Zeke Womack；Advance Wars 为 Nintendo / Intelligent Systems 的作品。本项目的程序化模型不使用原版 FBX、字体或音频资源。
