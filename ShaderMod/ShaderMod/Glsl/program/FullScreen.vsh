// ShaderMod v2：全屏三角形顶点着色器（不需要顶点缓冲）。
// 定义 NEED_ILLUMINANCE 时，从天空图第 256 列读出光照强度，并计算曝光和天光球谐（与 Derivative deferred5.vsh 相同）。

#ifdef NEED_ILLUMINANCE
	#include "/lib/Head/Common.inc"
	#include "/lib/Head/Uniforms.inc"

	uniform sampler2D skyMap;

	flat out vec3 directIlluminance;
	flat out vec3 skyIlluminance;
	flat out vec3 sunIlluminance;
	flat out vec3 moonIlluminance;
	flat out float exposure;

	#ifdef NEED_SKY_SH
		flat out vec4 skySHR;
		flat out vec4 skySHG;
		flat out vec4 skySHB;
	#endif

	#define PRECOMPUTED_ATMOSPHERIC_SCATTERING
	uniform sampler3D atmosphereLut;
	#include "/lib/Atmosphere/Atmosphere.glsl"
	#include "/lib/Post/Exposure.glsl"
#endif

void main() {
	vec2 p = vec2(float((gl_VertexID << 1) & 2), float(gl_VertexID & 2));
	gl_Position = vec4(p * 2.0 - 1.0, 0.0, 1.0);

	#ifdef FAR_PLANE
		// 画在远平面上（深度 1），配合 LEQUAL 深度测试只覆盖还没被地形挡住的像素
		gl_Position.z = 1.0;
	#endif

	#ifdef NEED_ILLUMINANCE
		directIlluminance = texelFetch(skyMap, ivec2(skyCaptureRes.x, 0), 0).rgb;
		skyIlluminance    = texelFetch(skyMap, ivec2(skyCaptureRes.x, 1), 0).rgb;
		sunIlluminance    = texelFetch(skyMap, ivec2(skyCaptureRes.x, 2), 0).rgb;
		moonIlluminance   = texelFetch(skyMap, ivec2(skyCaptureRes.x, 3), 0).rgb;
		exposure = CalculateExposure(directIlluminance, skyIlluminance);

		#ifdef NEED_SKY_SH
			skySHR = vec4(0.0);
			skySHG = vec4(0.0);
			skySHB = vec4(0.0);

			for (uint i = 0u; i < 5u; ++i) {
				float latitude = float(i) * 0.62831853;
				float cosLatitude = cos(latitude), sinLatitude = sin(latitude);
				for (uint j = 0u; j < 5u; ++j) {
					float longitude = float(j) * 1.25663706;
					vec3 rayDir = vec3(cosLatitude * cos(longitude), sinLatitude, cosLatitude * sin(longitude));

					vec3 skyCol = textureLod(skyMap, ProjectSky(rayDir), 0.0).rgb;

					skySHR += ToSH(skyCol.r, rayDir);
					skySHG += ToSH(skyCol.g, rayDir);
					skySHB += ToSH(skyCol.b, rayDir);
				}
			}

			skySHR /= 25.0;
			skySHG /= 25.0;
			skySHB /= 25.0;
		#endif
	#endif
}
