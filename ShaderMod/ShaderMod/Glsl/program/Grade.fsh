// ShaderMod v2：调色（对应 Derivative program/Post/Grade.glsl + Final.glsl）：
// 泛光 → 曝光 → AcademyFit（ACES）色调映射 → sRGB，加抖动后写入交给游戏贴屏的 8 位渲染目标。

#include "/lib/Head/Common.inc"
#include "/lib/Head/Uniforms.inc"

uniform sampler2D compositeTex;
uniform sampler2D bloomTex1;
uniform sampler2D bloomTex2;
uniform sampler2D bloomTex3;
uniform sampler2D bloomTex4;
uniform sampler2D bloomTex5;
uniform sampler2D bloomTex6;
uniform sampler2D bloomTex7;
uniform sampler2D skyMap;
uniform sampler2D noisetex;
uniform sampler3D atmosphereLut;
uniform float bloomStrength;

flat in vec3 directIlluminance;
flat in vec3 skyIlluminance;
flat in vec3 sunIlluminance;
flat in vec3 moonIlluminance;
flat in float exposure;

#include "/lib/Head/Noise.inc"
#include "/lib/Post/ACES.glsl"

layout(location = 0) out vec4 finalOut;

void CalculateBloom(inout vec3 color, in vec2 screenCoord) {
	vec3 bloomData = vec3(0.0);

	bloomData += textureBicubic(bloomTex1, screenCoord).rgb;
	bloomData += textureBicubic(bloomTex2, screenCoord).rgb * 0.83333333;
	bloomData += textureBicubic(bloomTex3, screenCoord).rgb * 0.69444444;
	bloomData += textureBicubic(bloomTex4, screenCoord).rgb * 0.57870370;
	bloomData += textureBicubic(bloomTex5, screenCoord).rgb * 0.48225309;
	bloomData += textureBicubic(bloomTex6, screenCoord).rgb * 0.40187757;
	bloomData += textureBicubic(bloomTex7, screenCoord).rgb * 0.33489798;

	bloomData *= 0.23118661;

	float bloomAmount = BLOOM_AMOUNT * 0.15 * bloomStrength;
	bloomAmount /= fma(max(exposure, 1.0), 0.7, 0.3);

	color += bloomData * bloomAmount;
}

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy);
	vec2 screenCoord = gl_FragCoord.xy * screenPixelSize;

	vec3 color = texelFetch(compositeTex, texel, 0).rgb;

	CalculateBloom(color, screenCoord);

	color *= exposure;

	color = AcademyFit(color);

	color += (bayer16(gl_FragCoord.xy) - 0.5) * rcp(255.0);

	finalOut = vec4(saturate(color), 1.0);
}
