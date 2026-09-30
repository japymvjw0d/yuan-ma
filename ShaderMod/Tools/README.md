# 离线预览与验证工具（开发用，不随模组发布）

在 Linux + Mesa（无头 OpenGL ES 3.2，`EGL_PLATFORM=surfaceless`）上用**与模组相同的 GLSL** 验证和预览光影。
依赖：python3、numpy、Pillow、PyOpenGL、glslangValidator、libEGL/libGLESv2（Mesa）。

| 脚本 | 作用 |
| :--- | :--- |
| `compile_all.py` | 用 glslangValidator 和 Mesa 编译、链接全部 GLSL ES 3.00 通道 |
| `validate_terrain.py` | 地形覆盖着色器与引擎通道着色器检查（需设置 `SC_VANILLA_SHADERS` 为原版着色器目录） |
| `preview.py [时间]` | 渲染一个合成场景（草地、石柱、树、水池、带屋顶的小屋），输出 `preview_*.png` |
| `daycycle.py` | 一天各时段 + 水面的预览拼图 `daycycle.png` |

`assemble.py` 与 `ShaderMod/Pipeline/ShaderSource.cs` 的 `#include` 展开和程序表保持一致；
`enginemath.py` 与引擎（XNA 风格、行向量）矩阵约定一致。

示例：`EGL_PLATFORM=surfaceless python3 daycycle.py`
