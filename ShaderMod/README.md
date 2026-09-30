# Derivative 光影（自用移植）—— 生存战争插件版模组

> **⚠ 版权声明：仅供个人自用，不得发布、分发或上传到任何地方。**
> 本模组移植自 Java 版 Minecraft 光影包 **Derivative**（作者 HaringPro，© 2024，**All Rights Reserved**）。
> 着色器代码（`Glsl/` 目录）和纹理数据（`Data/` 目录：大气查找表、噪声图）都来自该光影包，只在本机自己玩时使用。
> ACES 色调映射部分另受 A.M.P.A.S. 许可约束（许可原文保留在 `Glsl/lib/Post/ACES.glsl` 开头）。

适用于 **生存战争插件版 SurvivalcraftApi 1.9.2.1（Windows，非 mp 版）**，面向中高端独立显卡。

## 效果

| 部分 | 效果（均来自 Derivative） |
| :--- | :--- |
| 天空 | Bruneton 物理大气散射（预计算查找表）：正午湛蓝、日出日落橙红渐变、夜晚深蓝 + 星星 + 月亮 |
| 云 | 体积积云（光线步进，含光照、糖粉效应、多重散射近似）+ 高空卷云、卷积云；云在地面投下阴影 |
| 阳光 | 2048² 阴影贴图（Derivative 的四次方畸变，近处更清晰）、遮挡物搜索 + PCF 软阴影；受光面暖、阴影偏蓝 |
| 体积光 | 阳光穿过树叶、建筑缝隙形成的光束（丁达尔效应）+ 低处薄雾 |
| 水面 | 动态波浪法线、屏幕空间反射（倒映地形、树木）、天空与云的反射、折射、水中吸收与散射；水下雾 |
| 大气与远景 | 大气透视（远处泛蓝雾化）、地形尽头自然融入天空 |
| 调色 | 7 级泛光、按天空亮度自动曝光、ACES（AcademyFit）色调映射 |

- 按 **O** 开关光影，提示 `光影：开` / `光影：关`；状态会保存，默认开启。
- 可在 **设置 → 控制 → 键盘键位** 中修改按键，名称为"光影开关"。
- 光影关闭时，画面与原版完全一致（已用像素对比验证）。

## 原理（为什么能做到这个程度，又为什么不是 100%）

MC 光影依赖 OptiFine/Iris 提供的多遍渲染管线、法线贴图、方块 ID、分开的天光/方块光。生存战争都没有，所以做法是：

1. **自己搭一条延迟渲染管线**（`Pipeline/`）。用游戏自带的 HarmonyX 接入 `ViewWidget` 和 `SubsystemSky`：
   - 画场景前：计算与 Iris 同名的 uniform（相机矩阵、太阳方向、时段……），更新天空图、云穹图，用游戏自己的区块缓冲画太阳阴影贴图；
   - 游戏画天空时：改画 Derivative 的天空、云、星星、月亮（雨雪仍由游戏画在上面）；
   - 场景画完后：取出深度，再单独画一遍水面深度，然后依次做 延迟光照 → 体积光 → 水面与雾 → 泛光 → 调色，结果交给原版代码贴到屏幕上。
2. **着色器是 Derivative 的原代码**（`Glsl/lib/`），从 GLSL 450 改写为 GLSL ES 3.00（游戏在 Windows 上用 OpenGL ES 3.2 或 ANGLE），改动都在文件头注明。
3. **光照怎么处理**：生存战争把"天光 + 方块光 + 朝向明暗"烘焙进顶点颜色，拆不开。所以不重新计算完整光照，而是用 Derivative 的阳光/天光模型算出"此处相对露天平地的光照比例"，重新分配原版亮度：受光面更亮更暖，阴影里更暗更蓝。
   - 相机周围 512×512 格的**高度图**用来判断哪里露天：洞穴、室内、树下保持原版亮度（火把照亮的地方不会被压暗）。
4. **GL 调用直接从显卡驱动取函数地址**（原生模式 wglGetProcAddress，ANGLE 兼容模式 eglGetProcAddress），不依赖游戏引擎所用的 GL 绑定库；每次调用前后完整保存、恢复 GL 状态，不会干扰游戏自己的绘制。

没有移植的部分：全局光照（GI）、PBR 材质（没有法线/高光贴图）、时间抗锯齿（TAA）、景深、动态模糊、树叶摇曳（需要方块 ID）。

出错时自动退回原版画面，不会崩溃：任何一步出错只在日志（游戏目录 `Bugs` 文件夹）里记一条 `[ShaderMod]`，本次运行停用光影；按 O 会提示"本次运行出错已停用"。

## 编译与安装

和 SprintMod 相同：

1. 用 Visual Studio 打开 `ShaderMod.sln`，选 `Release`，生成解决方案。
2. 把 `ShaderMod\bin\Release\ShaderMod.scmod`（约 7 MB）复制到游戏目录的 `Mods` 文件夹。

```
ShaderMod/
├── ShaderMod.sln
├── nuget.config
└── ShaderMod/
    ├── ShaderMod.csproj            # 引用 SurvivalcraftAPI.Survivalcraft 1.9.2.1（自带 HarmonyX）；需要 AllowUnsafeBlocks
    ├── modinfo.json
    ├── ShaderModLoader.cs          # 开关键、设置保存
    ├── Pipeline/                   # 渲染管线（C#）
    │   ├── RenderPatches.cs        # Harmony 补丁：接入 ViewWidget / SubsystemSky
    │   ├── GamePipeline.cs         # 一帧的流程：阴影贴图、天空、水面深度、后处理
    │   ├── PostChain.cs            # 原生 GL 的各个通道
    │   ├── FrameUniforms.cs        # 与 Iris 同名的 uniform
    │   ├── HeightMap.cs            # 露天判断用的高度图
    │   ├── ShaderSettings.cs       # 质量与调色参数 ← 想调效果改这里
    │   ├── GlApi.cs / GlState.cs / GlObjects.cs / ShaderSource.cs / EngineAccess.cs
    ├── Glsl/                       # 着色器（编译进 dll）：lib/ 为移植的 Derivative 库，program/ 为各通道
    ├── EngineShaders/              # 阴影贴图、水面深度通道（由引擎编译）
    ├── Data/                       # Derivative 的大气查找表、噪声图（编译进 dll）
    └── Assets/
        ├── Shaders/                # 覆盖原版地形着色器（关闭原版雾、后处理开启时不画水）
        └── Lang/                   # 键位名称
```

## 想自己调效果？

都在 `Pipeline/ShaderSettings.cs`，改完重新生成、替换 `.scmod` 即可：

| 参数 | 作用 |
| :--- | :--- |
| `ExposureBias` | 曝光补偿（EV），整体太暗就调大，如 0.5 |
| `VanillaGain` | 地形亮度（原版颜色换算到 HDR 的增益） |
| `DayLightingStrength` / `NightLightingStrength` | 阳光阴影的明暗分配强度（0 = 保持原版明暗） |
| `ShadowLift` | 阴影里保留的最低亮度，越大阴影越亮 |
| `BloomStrength` | 泛光强度 |
| `ShadowMapResolution` / `ShadowDistance` | 阴影清晰度 / 覆盖范围（格） |
| `CloudSamples`、`VolumetricFogSamples`、`PcfSamples`、`ReflectionSamples` | 云、体积光、软阴影、水面反射的采样数（卡顿时调小） |

更细的效果（云量、水波、雾浓度等）在 `Glsl/` 里对应的 Derivative 文件中，设置项与原光影包同名。

## 已知限制

- 光照是在原版亮度基础上"重新分配"，不是完整的重新计算；贴图没有法线/高光信息，所以没有 PBR 反光。
- 实体（动物、玩家手持物）沿用原版着色，只叠加阴影和雾。
- 冰、玻璃等其他半透明方块仍是原版画法（只有水被重画）。
- 与其他替换 `Shaders/Opaque|AlphaTested|Transparent` 的模组或材质包冲突：后加载的生效，此时水面可能消失或出现两层，请二选一。
- 分屏多人时各视图共用一套资源，画面大小不同会反复重建，性能较差。
- 只能在这里离线预览、编译验证，没法真机运行游戏；实际效果（尤其曝光、阴影偏移、体积光强度）可能需要根据截图再调参。

## 游戏内测试清单

1. 进入存档：天空是 Derivative 的物理大气 + 体积云，没有原版的天空穹顶和方块云；日出日落天空、云被染成橙红色。
2. 白天：方块、树木在地面投下软阴影，影子方向与太阳一致；受光面偏暖、阴影偏蓝。站在阳光下看树冠缝隙，能看到光束。
3. 按 **O**：提示 `光影：关`，画面变回原版；再按 O 恢复。
4. 水面：有波浪，倒映岸边的树木、方块和天空，能看到水下的东西（越深越蓝绿）；潜入水下有水下雾。
5. 洞穴、室内：火把照亮的地方亮度与原版相同，没有被压暗。
6. 夜晚：天空深蓝、有星星和月亮，地形暗但能看清。
7. 设置里切换"分辨率"为低/中/高、改变视距，画面都正常。
8. 帧率：中高端独显应能流畅；卡顿时按上表调低采样数或阴影分辨率。
9. 若画面异常或光影自己关了：把 `Bugs` 文件夹里带 `[ShaderMod]` 的日志发给我。
