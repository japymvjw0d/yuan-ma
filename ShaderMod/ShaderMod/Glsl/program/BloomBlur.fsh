// ShaderMod v2：泛光高斯模糊（对应 Derivative BlurH/BlurV，9 点二项式权重）。

uniform sampler2D sourceTex;
uniform vec2 blurDirection; // (1,0) 或 (0,1)

layout(location = 0) out vec4 bloomOut;

void main() {
	ivec2 texel = ivec2(gl_FragCoord.xy);
	ivec2 maxTexel = textureSize(sourceTex, 0) - 1;
	ivec2 dir = ivec2(blurDirection);

	float sumWeight[5] = float[5](0.27343750, 0.21875000, 0.10937500, 0.03125000, 0.00390625);

	vec3 bloomTiles = vec3(0.0);
	for (int i = -4; i <= 4; ++i) {
		ivec2 sampleTexel = clamp(texel + dir * i, ivec2(0), maxTexel);
		bloomTiles += texelFetch(sourceTex, sampleTexel, 0).rgb * sumWeight[abs(i)];
	}

	bloomOut = vec4(bloomTiles, 1.0);
}
