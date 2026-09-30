# 疾跑 Sprint —— 生存战争插件版模组

适用于 **生存战争插件版 SurvivalcraftApi 1.9.2.1（Windows，非 mp 版）**。

## 功能

| 按键（默认） | 作用 |
| :---: | :--- |
| **Z** | 打开 / 关闭 **设置面板**，面板打开期间 **游戏暂停** |
| **X** | 全部功能的 **总开关** |

设置面板里有两个滑块，范围都是 **1.0 ~ 5.0 倍**（步长 0.1）：

- **疾跑速度**：作用于行走和创造模式飞行；游泳、爬梯子、跳跃高度保持原版。超过 3 倍时，地形加载可能跟不上。
- **挖掘速度**：挖方块的速度乘以倍率。方块要在挥手动作时才会被破坏，所以倍率太高时，速度会被挥手频率限制。
- **1.0 倍 = 该项不生效**；"恢复默认"会把两项都设回 1.0。
- 关闭面板的方式：按 Esc、再按 Z、或点"完成"。关闭时参数会保存到游戏目录的 `ModSettings.xml`，下次进游戏保持不变。

按 X 的提示：

| 情况 | 提示 |
| :--- | :--- |
| 打开，且设置了参数 | `功能：开启（疾跑 2.0 倍，挖掘 3.0 倍）`（只列出大于 1.0 的项） |
| 打开，但两项都是 1.0 | `功能：开启（未设置参数，按 Z 设置）`，此时没有实际效果 |
| 关闭 | `功能：关闭` |

其他说明：

- 总开关每次进入存档默认为关。
- 背包/衣物/箱子等面板、任何对话框（命名输入、告示牌、暂停菜单、本模组的设置面板等）打开时，或者有文本框正在输入时，按 X/Z 不会触发。
- 没有用默认的 F/G，因为原版 F 是"飞行"，G 是"编辑物品"。两个按键都可以在 **设置 → 控制 → 键盘键位** 中修改，名称为"疾跑挖掘总开关"和"疾跑挖掘设置面板"。
- 速度只在内存中修改，不写入存档；`NonPersistentMod` 为 true，删除本模组不会影响存档。
- 从 v1.0 升级时，旧的疾跑倍率会自动迁移为新的"疾跑速度"。

## 编译（Windows + Visual Studio 2026 + .NET 10 SDK）

项目结构和构建方式照搬地图模组 `SurvivalCraftTravelMap/plugin`：

```
SprintMod/
├── SprintMod.sln
├── nuget.config                # nuget.org + SurvivalcraftAPI 官方源（nuget.fury.io）
└── SprintMod/
    ├── SprintMod.csproj        # 引用 NuGet 包 SurvivalcraftAPI.Survivalcraft 1.9.2.1
    ├── modinfo.json
    ├── SprintModLoader.cs      # 按键、疾跑、挖掘、暂停、设置保存
    ├── SprintSettingsDialog.cs # 设置面板（滑块）
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

1. 进入存档，按 **X**：提示 `功能：开启（未设置参数，按 Z 设置）`，走路和挖掘速度都不变；再按 X：提示 `功能：关闭`。
2. 按 **Z**：弹出"疾跑 / 挖掘 设置"面板，鼠标出现，游戏暂停（生物停住，时间不走）。
3. 把疾跑拖到 2.0x、挖掘拖到 3.0x，按 **Esc** 关闭：面板关闭、游戏继续，**不会**弹出暂停菜单。
4. 按 X：提示 `功能：开启（疾跑 2.0 倍，挖掘 3.0 倍）`；走路明显变快，挖方块明显变快。再按 X 关闭，全部恢复原速。
5. 再按 Z 打开面板，分别用"完成"按钮和再按 Z 关闭，都能正常关闭；"恢复默认"会把两个滑块回到 1.0x。
6. 创造模式飞行时打开总开关，飞行速度也按疾跑倍率提升。
7. 打开背包（E）、在告示牌或命名输入框里打字、按 Esc 打开暂停菜单时，按 X/Z 没有反应。
8. 设好参数后退出游戏，重新启动并进入存档，按 Z：滑块保持上次的数值。
9. 在 设置 → 控制 → 键盘键位 中能看到"疾跑挖掘总开关""疾跑挖掘设置面板"两项，修改后新按键生效。
