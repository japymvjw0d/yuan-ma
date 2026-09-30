/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
	保留"体积光（丁达尔）+ 中等体积雾（FOG_TYPE 1）"；去掉彩色玻璃阴影与生物群系沙尘；
	far 改为生存战争的可视距离 renderDistance。需要调用方提供 directIlluminance、skyIlluminance。
*/

#ifndef VOLUMETRIC_FOG_SAMPLES
	#define VOLUMETRIC_FOG_SAMPLES 12
#endif
#define VOLUMETRIC_FOG_DENSITY 0.002
#define SEA_LEVEL 63.0
#define VOLUMETRIC_LIGHT_STRENGTH 0.2
#define UW_VOLUMETRIC_LIGHT_STRENGTH 0.1

#include "/lib/Lighting/ShadowDistortion.glsl"

vec3 WorldPosToShadowPos(in vec3 worldPos) {
	vec3 shadowClipPos = transMAD(shadowModelView, worldPos);
	shadowClipPos = projMAD(shadowProjection, shadowClipPos);

	return shadowClipPos;
}

/* Medium */
float CalculateFogDensity(in vec3 rayPosition) {
	float fogDensity = exp2(min((SEA_LEVEL + 28.0 - rayPosition.y) * 0.15, 0.2));

	rayPosition *= 0.07;
	rayPosition += volFogWind;
	float noise = Get3DNoiseSmooth(rayPosition) * 4.0;
	noise -= Get3DNoiseSmooth(rayPosition * 4.0 + volFogWind);

	fogDensity = saturate(noise * 4.0 * fogDensity - 5.0) * 1.4;
	fogDensity = fogDensity * oneMinus(timeNoon) + timeNoon;

	return fogDensity;
}

vec4 CalculateVolumetricFog(in vec3 worldPos, in vec3 worldDir, in float dither) {
	float rayLength = min(renderDistance + wetness * 3e-5 * dotSelf(worldPos.xz), length(worldPos));
	uint steps = uint(float(VOLUMETRIC_FOG_SAMPLES) * 0.4 + rayLength * 0.1);
		 steps = min(steps, uint(VOLUMETRIC_FOG_SAMPLES));

	float rSteps = 1.0 / float(steps);

	float stepLength = rayLength * rSteps,
		  transmittance = 1.0,
		  LdotV = dot(worldLightVector, worldDir),
		  LdotV01 = LdotV * 0.5 + 0.5,
		  skylightSample = 0.0;

	float mistDensity = VOLUMETRIC_FOG_DENSITY * volFogDensity;
	mistDensity *= CornetteShanksPhase(LdotV, 0.7 - wetness * 0.3) * 0.45 + HenyeyGreensteinPhase(LdotV, -0.3) * 0.15 + 0.1;

	float airDensity = VOLUMETRIC_LIGHT_STRENGTH;
	airDensity *= RayleighPhase(LdotV) * (3.0 / renderDistance);

	vec3 rayStep = worldDir * stepLength,
		 rayPosition = rayStep * dither + cameraPosition;

	vec3 shadowStart = WorldPosToShadowPos(vec3(0.0)),
		 shadowEnd = WorldPosToShadowPos(rayStep);

	vec3 shadowStep = shadowEnd - shadowStart,
		 shadowPosition = shadowStep * dither + shadowStart;

	vec3 sunlightSample = vec3(0.0);

	airDensity *= max(saturate(meWeight + 0.25) + timeMidnight * 4.0, wetness);
	mistDensity *= max(sqr(meWeight) + timeMidnight * 2.0, wetness);

	stepLength *= eyeSkylightFix;

	uint i = 0u;
	while (++i < steps) {
		rayPosition += rayStep, shadowPosition += shadowStep;
		if (rayPosition.y > 384.0) continue;

		vec3 shadowProjPos = DistortShadowSpace(shadowPosition) * 0.5 + 0.5;

		float fogDensity = airDensity;
		float density = CalculateFogDensity(rayPosition) * mistDensity;
		fogDensity += density;

		if (fogDensity < 1e-5) continue;
		fogDensity *= stepLength;

		vec3 shadow = vec3(1.0);
		if (saturate(shadowProjPos) == shadowProjPos) {
			shadow = vec3(textureLod(shadowtex1, shadowProjPos, 0.0));
		}

		float stepTransmittance = fastExp(-fogDensity);

		float powder = 1.0 - fastExp(-fogDensity * 3.0);
		powder = powder * oneMinus(LdotV01) + LdotV01;

		float fogSample = powder * transmittance * oneMinus(stepTransmittance);

		sunlightSample += shadow * fogSample;
		skylightSample += fogSample;
		transmittance *= stepTransmittance;

		if (transmittance < 1e-3) break;
	}

	vec3 fogSunColor = directIlluminance * sunlightSample * SUNLIGHT_INTENSITY;
	vec3 fogSkyColor = skyIlluminance * skylightSample;
	vec3 fogColor = fogSunColor * 20.0 + fogSkyColor * 2.0;

	fogColor *= oneMinus(0.8 * wetness);

	return vec4(fogColor, transmittance);
}

vec3 UnderwaterVolumetricLight(in vec3 worldPos, in vec3 worldDir, in float dither) {
	float rayLength = min(24.0, length(worldPos));
	uint steps = uint(12.0 + 0.5 * rayLength);
		 steps = min(steps, 22u);

	float rSteps = 1.0 / float(steps);
	float stepLength = rayLength * rSteps;

	vec3 shadowStart = WorldPosToShadowPos(vec3(0.0)),
		 shadowEnd = WorldPosToShadowPos(worldDir * stepLength);

	vec3 shadowStep = shadowEnd - shadowStart,
		 shadowPosition = shadowStep * dither + shadowStart;

	const vec3 coeff = waterAbsorption + 0.02;
	vec3 stepTransmittance = fastExp(-coeff * stepLength);

	vec3 transmittance = vec3(1.0);
	vec3 scattering = vec3(0.0);

	uint i = 0u;
	while (++i < steps) {
		shadowPosition += shadowStep;
		vec3 shadowProjPos = DistortShadowSpace(shadowPosition) * 0.5 + 0.5;
		if (saturate(shadowProjPos) != shadowProjPos) continue;

		vec3 sampleShadow = vec3(textureLod(shadowtex1, shadowProjPos, 0.0));

		scattering += sampleShadow * transmittance * oneMinus(stepTransmittance);
		transmittance *= stepTransmittance;
	}

	vec3 lightVector = refract(worldLightVector, vec3(0.0, -1.0, 0.0), 1.0 / WATER_REFRACT_IOR);
	float LdotV = dot(lightVector, worldDir);
	float phase = HenyeyGreensteinPhase(LdotV, 0.8) + HenyeyGreensteinPhase(LdotV, 0.6);

	vec3 fogColor = 8.0 / coeff * directIlluminance * oneMinus(0.95 * wetness);

	fogColor *= scattering * phase * UW_VOLUMETRIC_LIGHT_STRENGTH;

	return fogColor * SUNLIGHT_INTENSITY;
}
