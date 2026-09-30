# 疾跑 Sprint —— 生存战争插件版模组

适用于 **生存战争插件版 SurvivalcraftApi 1.9.2.1（Windows，非 mp 版）**。

## 功能

| 按键（默认） | 作用 |
| :---: | :--- |
| **X** | 开 / 关疾跑。开启后移动速度 × 当前倍率，再按一次恢复原速 |
| **Z** | 循环切换倍率：1.5 倍 → 2 倍 → 3 倍 → 1.5 倍…（最高 3 倍，防止地形加载跟不上或卡进方块） |

- 按键后屏幕上会弹出一行提示，例如 `疾跑：开（2 倍）`。
- 倍率对 **行走** 和 **创造模式飞行** 都生效；游泳、爬梯子和跳跃高度保持原版。
- 选择的倍率会保存到游戏目录的 `ModSettings.xml`，下次进游戏保持不变。疾跑的开/关状态不保存，每次进入存档默认为关。
- 背包/衣物/箱子等面板、任何对话框（命名输入、告示牌、暂停菜单等）打开时，或者有文本框正在输入时，按 X/Z **不会**触发。
- 没有用默认的 F/G，因为原版 F 是"飞行"，G 是"编辑物品"。两个按键都可以在 **设置 → 控制 → 键盘键位** 中修改，名称为"疾跑开关"和"疾跑倍率"。
- 速度只在内存中修改，不写入存档；`NonPersistentMod` 为 true，删除本模组不会影响存档。

## 编译（Windows + Visual Studio 2026 + .NET 10 SDK）

项目结构和构建方式照搬地图模组 `SurvivalCraftTravelMap/plugin`：

```
SprintMod/
├── SprintMod.sln
├── nuget.config                # nuget.org + SurvivalcraftAPI 官方源（nuget.fury.io）
└── SprintMod/
    ├── SprintMod.csproj        # 引用 NuGet 包 SurvivalcraftAPI.Survivalcraft 1.9.2.1
    ├── modinfo.json
    ├── SprintModLoader.cs      # 全部逻辑
    └── Assets/Lang/            # 键位名称的中英文
```

1. 用 Visual Studio 打开 `SprintMod.sln`。
2. 选择 `Release`（或 `Debug`），点 **生成解决方案**。
3. 生成后会得到 `SprintMod\bin\Release\SprintMod.scmod`（Debug 则在 `bin\Debug\` 下），输出窗口会提示"已打包 scmod 文件到: …"。

命令行也可以：

```bat
cd SprintMod
dotnet build -c Release
```

> 如果还原 NuGet 包失败（访问不了 nuget.fury.io），可以按官方文档的方法，改为直接引用游戏目录里的 dll：
> 在 `SprintMod.csproj` 里把 `PackageReference` 那一行换成下面这段（路径改成你的游戏目录）：
> ```xml
> <Reference Include="Engine" HintPath="D:\Survivalcraft\Engine.dll" Private="false" />
> <Reference Include="EntitySystem" HintPath="D:\Survivalcraft\EntitySystem.dll" Private="false" />
> <Reference Include="Survivalcraft" HintPath="D:\Survivalcraft\Survivalcraft.dll" Private="false" />
> ```

## 安装

把 `SprintMod.scmod` 复制到游戏目录下的 `Mods` 文件夹，然后启动游戏。

## 游戏内测试清单

1. 进入存档，按 **X**：提示 `疾跑：开（1.5 倍）`，走路明显变快；再按 X：提示 `疾跑：关（1.5 倍）`，恢复原速。
2. 按 **Z**：提示倍率依次变为 2 倍、3 倍、1.5 倍；疾跑开启时速度立即跟着变化。
3. 创造模式飞行时开启疾跑，飞行速度也按倍率提升。
4. 打开背包（E）、在告示牌或命名输入框里打字、按 Esc 打开暂停菜单时，按 X/Z 没有反应。
5. 把倍率切到 3 倍后退出游戏，重新启动并进入存档，按 X：提示 `疾跑：开（3 倍）`。
6. 在 设置 → 控制 → 键盘键位 中能看到"疾跑开关""疾跑倍率"两项，修改后新按键生效。
