// ShaderMod v2：与 Iris 同名的 uniform（由 C# FrameUniforms 每帧写入），供移植的 Derivative 库使用。
// 约定：
// - "世界坐标"（Derivative 的 worldPos）= 游戏世界坐标 - 相机位置，y 轴向上；
// - 视图空间右手系、相机看向 -Z；深度纹理值 = 引擎投影的 z/w（0 近 1 远）；
// - 屏幕坐标 screenCoord 的 y=0 为画面顶部（与游戏渲染目标的行序一致）。

uniform mat4 gbufferModelView;
uniform mat4 gbufferModelViewInverse;
uniform mat4 gbufferProjection;
uniform mat4 gbufferProjectionInverse;

uniform mat4 shadowModelView;
uniform mat4 shadowProjection;
uniform mat4 shadowProjectionInverse;

uniform vec3 cameraPosition;
uniform vec3 worldSunVector;
uniform vec3 worldLightVector;

uniform float eyeAltitude;
uniform float wetness;
uniform float frameTimeCounter;
uniform float worldTimeCounter;
uniform float moonPhase;
uniform float nightVision;
uniform float isLightningFlashing;
uniform float near;
uniform float far;
uniform float timeNoon;
uniform float timeMidnight;
uniform float timeSunrise;
uniform float timeSunset;
uniform float meWeight;
uniform float eyeSkylightFix;
uniform float volFogDensity;
uniform vec3 volFogWind;

// ShaderMod 自有参数
uniform float renderDistance;   // 生存战争的可视距离（对应 MC 的 far）
uniform float shadowDistance;   // 阴影覆盖半径（格）
uniform float shadowMapRes;     // 阴影贴图边长（像素）

uniform vec2 screenSize;
uniform vec2 screenPixelSize;

uniform int isEyeInWater;
uniform int frameCounter;

#define wetnessCustom wetness
