#ifdef GLSL

// ShaderMod v2：太阳阴影贴图（地形）顶点着色器，由引擎的 Shader 类编译（与原版地形着色器同一套写法）。
// 坐标先变换到阴影裁剪空间，再做 Derivative 的四次方畸变（近处分辨率更高）；
// 不使用 OPENGL_POSITION_FIX：阴影贴图不翻转 y，深度直接按 GL 约定写入。

// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

uniform vec2 u_origin;
uniform mat4 u_shadowMatrix;

attribute vec3 a_position;
attribute vec2 a_texcoord;

varying vec2 v_texcoord;

void main()
{
	v_texcoord = a_texcoord;

	vec4 clip = u_shadowMatrix * vec4(a_position.x - u_origin.x, a_position.y, a_position.z - u_origin.y, 1.0);
	vec2 v = clip.xy * 1.165;
	vec2 v2 = v * v;
	float distortFactor = sqrt(sqrt(v2.x * v2.x + v2.y * v2.y)) * 0.9 + 0.1;
	gl_Position = vec4(clip.xy / distortFactor, clip.z * 0.2, 1.0);
}

#endif
