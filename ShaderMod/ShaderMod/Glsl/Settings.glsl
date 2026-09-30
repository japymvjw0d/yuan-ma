// ShaderMod v2 的 Derivative 设置（只保留移植用到的项，默认"高"质量）。
// 数值由 C# ShaderSettings 决定的项以 uniform 形式出现在 Uniforms.inc。

#define INCLUDE_SETTINGS

const ivec2 skyCaptureRes = ivec2(255, 256);

#define PLANAR_CLOUDS
#define VOLUMETRIC_CLOUDS
#define CIRRUS_CLOUDS 1
#define CIRROCUMULUS_CLOUDS
#define CLOUDS_SPEED 1.0
#define minTransmittance 0.05

#define STARS_INTENSITY 0.1
#define STARS_COVERAGE  0.15

#define SUNLIGHT_INTENSITY 1.0
#define SKYLIGHT_INTENSITY 1.0
#define NIGHT_BRIGHTNESS 0.0005

#define WATER_REFRACT_IOR 1.33
#define WATER_FOG_DENSITY 1.0
#define WATER_WAVE_HEIGHT 1.0
#define WATER_WAVE_SPEED 1.0

#define SHADOW_MAP_BIAS 0.9

#define BLOOM_AMOUNT 1.0
#define AUTO_EXPOSURE_BIAS 0.0

#define TORCHLIGHT_COLOR_TEMPERATURE 3000

const vec3 waterAbsorption = vec3(0.4, 0.14, 0.08);
