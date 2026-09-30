// ShaderMod v2：按方向采样天空图、云穹图（多个通道共用）。需要 skyMap、cloudDome 采样器。

vec2 WorldDirToDome(in vec3 worldDir) {
	float azimuth = atan(worldDir.x, worldDir.z);
	float elevation = asin(clamp(worldDir.y, 0.0, 1.0));
	return vec2(azimuth * rTAU + 0.5, elevation / (0.5 * PI));
}

// rgb = 云的散射光，a = 透射率
vec4 SampleCloudDome(in vec3 worldDir) {
	if (worldDir.y <= 0.0) return vec4(0.0, 0.0, 0.0, 1.0);
	vec4 clouds = textureBicubic(cloudDome, WorldDirToDome(worldDir));
	// 地平线附近逐渐淡出，避免硬边
	float fade = saturate(worldDir.y * 40.0);
	return vec4(max(clouds.rgb, vec3(0.0)) * fade, mix(1.0, saturate(clouds.a), fade));
}

vec3 SampleSkyRadiance(in vec3 worldDir) {
	return max(textureBicubic(skyMap, ProjectSky(worldDir)).rgb, vec3(0.0));
}

// 天空 + 云（不含日月星）
vec3 SampleSkyWithClouds(in vec3 worldDir) {
	vec4 clouds = SampleCloudDome(worldDir);
	return SampleSkyRadiance(worldDir) * clouds.a + clouds.rgb;
}

// 高度图：以世界坐标 xz 对边长取模，存放每一列最高方块的高度（0~255）
float GetTopHeight(in vec3 absolutePos) {
	ivec2 texel = ivec2(floor(absolutePos.xz)) & (textureSize(heightMap, 0) - 1);
	return texelFetch(heightMap, texel, 0).r * 255.0;
}

// 露天程度：0 = 头顶有遮挡（洞穴、室内、树下），1 = 露天
float GetSkyExposure(in vec3 absolutePos) {
	return saturate((absolutePos.y - GetTopHeight(absolutePos)) * 0.5 + 0.25);
}
