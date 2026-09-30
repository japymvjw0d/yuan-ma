/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
*/

float textureSmooth(in vec2 coord) {
	coord += 0.5;
	vec2 whole = floor(coord);
	vec2 part  = curve(coord - whole);
	coord = whole + part - 0.5;
	return texture(noisetex, coord * rcp(256.0)).x;
}

float WaterHeight(in vec2 p) {
	float wavesTime = frameTimeCounter * 1.2 * WATER_WAVE_SPEED;
	p.y *= 0.8;

	float wave = 0.0;
	wave += textureSmooth((p + vec2(0.0, p.x - wavesTime)) * 0.8);
	wave += textureSmooth((p - vec2(-wavesTime, p.x)) * 1.6) * 0.5;
	wave += textureSmooth((p + vec2(wavesTime * 0.6, p.x - wavesTime)) * 2.4) * 0.2;
	wave += textureSmooth((p - vec2(wavesTime * 0.6, p.x - wavesTime)) * 3.6) * 0.1;

	// 远处的波纹逐渐变平（Derivative 用 MC 的渲染距离 far；这里对应生存战争的可视距离）
	return wave / (0.8 + dot(abs(dFdx(p) + dFdy(p)), vec2(80.0 / renderDistance)));
}

vec3 GetWavesNormal(in vec2 position) {
	float wavesCenter = WaterHeight(position);
	float wavesLeft   = WaterHeight(position + vec2(0.04, 0.0));
	float wavesUp     = WaterHeight(position + vec2(0.0, 0.04));

	vec2 wavesNormal = vec2(wavesCenter - wavesLeft, wavesCenter - wavesUp);

	return normalize(vec3(wavesNormal * WATER_WAVE_HEIGHT, 0.5));
}
