/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
	depthtex1 = 不含水面的场景深度；屏幕坐标 y=0 为顶部（光线步进与方向无关，逻辑不变）。
*/

#include "/lib/Surface/BRDF.glsl"

#ifndef RAYTRACE_SAMPLES
	#define RAYTRACE_SAMPLES 16u
#endif
#define RAYTRACE_REFINEMENT_STEPS 6u

bool ScreenSpaceRayTrace(in vec3 viewPos, in vec3 viewDir, in float dither, in uint steps, inout vec3 rayPos) {
	float maxLength = 1.0 / float(steps);
	float minLength = length(screenPixelSize);

	vec3 position = ViewToScreenSpace(viewDir * abs(viewPos.z) + viewPos);
	vec3 screenDir = normalize(position - rayPos);
	float stepWeight = 1.0 / abs(screenDir.z);

	float stepLength = minOf((step(0.0, screenDir) - rayPos) / screenDir) * rcp(float(steps));

	screenDir.xy *= screenSize;
	rayPos.xy *= screenSize;

	vec3 rayStep = screenDir * stepLength;
	rayPos += rayStep * dither + screenDir * minLength;

	bool hit = false;

	float depth = texelFetch(depthtex1, ivec2(rayPos.xy), 0).x;
	for (uint i = 0u; i < steps; ++i) {
		if (clamp(rayPos.xy, vec2(0.0), screenSize) != rayPos.xy) return false;
		if (rayPos.z >= 1.0) break;

		stepLength = abs(depth - rayPos.z) * stepWeight;
		rayPos += screenDir * clamp(stepLength, minLength, maxLength);

		depth = texelFetch(depthtex1, ivec2(rayPos.xy), 0).x;

		if (depth < rayPos.z) {
			float linearSample = GetDepthLinear(depth);
			float currentDepth = GetDepthLinear(rayPos.z);

			if (abs(linearSample - currentDepth) / currentDepth < 0.2) {
				hit = true;
				break;
			}
		}
	}

	if (!hit) return false;

	rayStep = screenDir * stepLength;
	for (uint i = 0u; i < RAYTRACE_REFINEMENT_STEPS; ++i) {
		if (clamp(rayPos.xy, vec2(0.0), screenSize) != rayPos.xy) break;
		rayStep *= 0.5;

		depth = texelFetch(depthtex1, ivec2(rayPos.xy), 0).x;

		if (depth < rayPos.z) {
			rayPos -= rayStep;
		} else {
			rayPos += rayStep;
		}
	}

	return depth < 1.0;
}
