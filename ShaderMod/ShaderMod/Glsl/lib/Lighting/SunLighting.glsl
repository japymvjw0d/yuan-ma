/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
	只保留阴影相关函数；阴影贴图分辨率与距离改为 uniform（shadowMapRes、shadowDistance）。
*/

#include "/lib/Surface/BRDF.glsl"

#ifndef PCF_SAMPLES
	#define PCF_SAMPLES 12u
#endif

#define realShadowMapRes shadowMapRes

#include "ShadowDistortion.glsl"

vec3 WorldPosToShadowProjPosBias(in vec3 worldOffsetPos, out float distortFactor) {
	vec3 shadowClipPos = transMAD(shadowModelView, worldOffsetPos);
	shadowClipPos = projMAD(shadowProjection, shadowClipPos);

	distortFactor = DistortionFactor(shadowClipPos.xy);
	return DistortShadowSpace(shadowClipPos, distortFactor) * 0.5 + 0.5;
}

vec2 BlockerSearch(in vec3 shadowProjPos, in float dither) {
	float searchDepth = 0.0;
	float sumWeight = 0.0;
	float sssDepth = 0.0;

	float searchRadius = 2.0 * shadowProjection[0].x;

	vec2 rot = cossin(dither * TAU) * searchRadius;
	const vec2 angleStep = vec2(0.70710678, 0.70710678); // cossin(TAU * 0.125)
	const mat2 rotStep = mat2(angleStep, -angleStep.y, angleStep.x);
	for (uint i = 0u; i < 8u; ++i, rot *= rotStep) {
		float fi = float(i) + dither;
		vec2 sampleCoord = shadowProjPos.xy + rot * sqrt(fi * 0.125);
		float depthSample = texelFetch(shadowtex0, ivec2(sampleCoord * realShadowMapRes), 0).x;
		float weight = step(depthSample, shadowProjPos.z);

		sssDepth += max0(shadowProjPos.z - depthSample);
		searchDepth += depthSample * weight;
		sumWeight += weight;
	}

	searchDepth *= 1.0 / max(sumWeight, 1e-6);
	searchDepth = min(2.0 * (shadowProjPos.z - searchDepth) / max(searchDepth, 1e-6), 1.0);

	return vec2(searchDepth * shadowProjection[0].x, sssDepth * shadowProjectionInverse[2].z);
}

vec3 PercentageCloserFilter(in vec3 shadowProjPos, in float dither, in float penumbraScale) {
	shadowProjPos.z -= 1e-4 - dither * 5e-5;

	const float rSteps = 1.0 / float(PCF_SAMPLES);

	vec3 result = vec3(0.0);

	vec2 rot = cossin(dither * TAU) * penumbraScale;
	const vec2 angleStep = vec2(0.70710678, 0.70710678);
	const mat2 rotStep = mat2(angleStep, -angleStep.y, angleStep.x);
	for (uint i = 0u; i < PCF_SAMPLES; ++i, rot *= rotStep) {
		float fi = float(i) + dither;
		vec2 sampleCoord = shadowProjPos.xy + rot * sqrt(fi * rSteps);
		result += textureLod(shadowtex1, vec3(sampleCoord, shadowProjPos.z), 0.0);
	}

	return result * rSteps;
}

float CalculateFakeBouncedLight(in vec3 normal) {
	normal.y = -normal.y;
	vec3 bounceVector = normalize(worldLightVector + vec3(0.0, 1.0, 0.0));
	float bounce = saturate(dot(normal, bounceVector) * 0.4 + 0.6);
	return bounce * (2.0 - bounce) * 3e-2;
}
