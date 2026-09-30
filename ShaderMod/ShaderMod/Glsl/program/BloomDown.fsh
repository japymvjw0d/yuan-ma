// ShaderMod v2：泛光降采样（对应 Derivative DownSample0/DownSample 的 DualBlurDownSample）。
// 每级是上一级的一半大小；第一级从场景降采样。

uniform sampler2D sourceTex;
uniform vec2 targetSize;
uniform float firstLevel;

layout(location = 0) out vec4 bloomOut;

void main() {
	vec2 coord = gl_FragCoord.xy / targetSize;
	vec2 sourcePixelSize = 1.0 / vec2(textureSize(sourceTex, 0));

	vec3 bloomTile;
	if (firstLevel > 0.5) {
		// DownSample0：5 点
		bloomTile  = textureLod(sourceTex, coord, 0.0).rgb;
		bloomTile += textureLod(sourceTex, vec2( 1.0,  1.0) * sourcePixelSize + coord, 0.0).rgb;
		bloomTile += textureLod(sourceTex, vec2(-1.0,  1.0) * sourcePixelSize + coord, 0.0).rgb;
		bloomTile += textureLod(sourceTex, vec2( 1.0, -1.0) * sourcePixelSize + coord, 0.0).rgb;
		bloomTile += textureLod(sourceTex, vec2(-1.0, -1.0) * sourcePixelSize + coord, 0.0).rgb;
		bloomTile *= 0.2;
	} else {
		// DownSample：3×3 加权（BLUR_SAMPLES = 1）
		bloomTile = vec3(0.0);
		float sumWeight = 0.0;
		for (int y = -1; y <= 1; ++y) {
			for (int x = -1; x <= 1; ++x) {
				float weight = clamp(1.0 - length(vec2(x, y)) * 0.25, 0.0, 1.0);
				weight *= weight;
				bloomTile += textureLod(sourceTex, coord + vec2(x, y) * sourcePixelSize, 0.0).rgb * weight;
				sumWeight += weight;
			}
		}
		bloomTile /= sumWeight;
	}

	bloomOut = vec4(clamp(bloomTile, 0.0, 65535.0), 1.0);
}
