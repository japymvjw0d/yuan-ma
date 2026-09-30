#ifdef GLSL

// ShaderMod v2：水面深度通道顶点着色器，变换与原版 Transparent.vsh 完全一致（保证深度与场景逐像素吻合）。

// <Semantic Name='POSITION' Attribute='a_position' />
// <Semantic Name='TEXCOORD' Attribute='a_texcoord' />

uniform vec2 u_origin;
uniform mat4 u_viewProjectionMatrix;

attribute vec3 a_position;
attribute vec2 a_texcoord;

varying vec2 v_texcoord;

void main()
{
	v_texcoord = a_texcoord;
	gl_Position = u_viewProjectionMatrix * vec4(a_position.x - u_origin.x, a_position.y, a_position.z - u_origin.y, 1.0);

	OPENGL_POSITION_FIX;
}

#endif
