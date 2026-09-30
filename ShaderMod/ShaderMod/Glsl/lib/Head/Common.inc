/*
	Derivative Shaders by HaringPro
	Copyright (C) 2024 HaringPro. All Rights Reserved
	—— ShaderMod 自用移植（GLSL ES 3.00），不得发布。
*/

#include "/Settings.glsl"

const float PI 			= radians(180.0);
const float rPI 		= 1.0 / PI;
const float TAU 		= radians(360.0);
const float rTAU 		= 1.0 / TAU;
const float rLOG2 		= 1.0 / log(2.0);

#define rcp(x) 			(1.0 / (x))
#define oneMinus(x) 	(1.0 - (x))
#define fastExp(x) 		exp2((x) * rLOG2)
#define max0(x) 		max(x, 0.0)
#define saturate(x) 	clamp(x, 0.0, 1.0)
#define clamp16F(x) 	clamp(x, 0.0, 65535.0)

#define transMAD(m, v)	(mat3(m) * (v) + (m)[3].xyz)
#define diagonal2(m)	vec2((m)[0].x, (m)[1].y)
#define diagonal3(m)	vec3((m)[0].x, (m)[1].y, (m)[2].z)
#define diagonal4(m)	vec4(diagonal3(m), (m)[2].w)
#define projMAD(m, v)	(diagonal3(m) * (v) + (m)[3].xyz)

float maxOf(vec2 v)   	{ return max(v.x, v.y); }
float maxOf(vec3 v)   	{ return max(v.x, max(v.y, v.z)); }
float maxOf(vec4 v)   	{ return max(v.x, max(v.y, max(v.z, v.w))); }
float minOf(vec2 v)   	{ return min(v.x, v.y); }
float minOf(vec3 v)   	{ return min(v.x, min(v.y, v.z)); }
float minOf(vec4 v)   	{ return min(v.x, min(v.y, min(v.z, v.w))); }

float sqr(float x)    	{ return x * x; }
vec2  sqr(vec2 x)	  	{ return x * x; }
vec3  sqr(vec3 x)	  	{ return x * x; }
vec4  sqr(vec4 x)	  	{ return x * x; }

float cube(float x)   	{ return x * x * x; }
vec2  cube(vec2 x)	  	{ return x * x * x; }
vec3  cube(vec3 x)	  	{ return x * x * x; }

float pow4(float x)   	{ return cube(x) * x; }
vec3  pow4(vec3 x)	  	{ return cube(x) * x; }

float pow5(float x)   	{ return pow4(x) * x; }
vec3  pow5(vec3 x)	  	{ return pow4(x) * x; }

float pow16(float x)   	{ return sqr(pow4(x)); }

float sqrt2(float c)  	{ return sqrt(sqrt(c)); }
vec3  sqrt2(vec3 c)	  	{ return sqrt(sqrt(c)); }

float curve(float x)  	{ return sqr(x) * (3.0 - 2.0 * x); }
vec2  curve(vec2 x)	  	{ return sqr(x) * (3.0 - 2.0 * x); }
vec3  curve(vec3 x)	  	{ return sqr(x) * (3.0 - 2.0 * x); }

float dotSelf(vec2 x) 	{ return dot(x, x); }
float dotSelf(vec3 x) 	{ return dot(x, x); }

vec2  sincos(float x)   { return vec2(sin(x), cos(x)); }
vec2  cossin(float x)   { return vec2(cos(x), sin(x)); }

float remap(float e0, float e1, float x) { return saturate((x - e0) / (e1 - e0)); }

vec3 LinearToSRGB(in vec3 color) {
	color = max(color, vec3(0.0));
	return mix(color * 12.92, 1.055 * pow(color, vec3(1.0 / 2.4)) - 0.055, lessThan(vec3(0.0031308), color));
}

vec3 SRGBtoLinear(in vec3 color) {
	color = max(color, vec3(0.0));
	return mix(color / 12.92, pow((color + 0.055) / 1.055, vec3(2.4)), lessThan(vec3(0.04045), color));
}

float GetLuminance(in vec3 color) {
	return dot(color, vec3(0.2722, 0.6741, 0.0537));
}

vec3 ColorSaturation(in vec3 color, in float sat) { return mix(vec3(GetLuminance(color)), color, sat); }

vec3 Blackbody(in float t) {
	// http://en.wikipedia.org/wiki/Planckian_locus
	vec4 vx = vec4(-0.2661239e9, -0.2343580e6, 0.8776956e3, 0.179910  );
	vec4 vy = vec4(-1.1063814,   -1.34811020,  2.18555832, -0.20219683);
	float it = 1.0 / t;
	float it2 = it * it;
	float x = dot(vx, vec4(it * it2, it2, it, 1.0));
	float x2 = x * x;
	float y = dot(vy, vec4(x * x2, x2, x, 1.0));

	// http://www.brucelindbloom.com/index.html?Eqn_RGB_XYZ_Matrix.html
	mat3 xyzToSrgb = mat3(
		 3.2404542,-1.5371385,-0.4985314,
		-0.9692660, 1.8760108, 0.0415560,
		 0.0556434,-0.2040259, 1.0572252
	);

	vec3 srgb = vec3(x / y, 1.0, oneMinus(x + y) / y) * xyzToSrgb;

	return max0(srgb);
}

vec4 cubic(in float x) {
	float x2 = x * x;
	float x3 = x2 * x;
	vec4 w;
	w.x = -x3 + 3.0 * x2 - 3.0 * x + 1.0;
	w.y = 3.0 * x3 - 6.0 * x2 + 4.0;
	w.z = -3.0 * x3 + 3.0 * x2 + 3.0 * x + 1.0;
	w.w = x3;
	return w * rcp(6.0);
}

vec4 textureBicubic(in sampler2D tex, in vec2 coord) {
	vec2 res = vec2(textureSize(tex, 0));

	coord = coord * res - 0.5;

	vec2 fTexel = fract(coord);
	coord -= fTexel;

	vec4 xCubic = cubic(fTexel.x);
	vec4 yCubic = cubic(fTexel.y);

	vec4 c = coord.xxyy + vec2(-0.5, 1.5).xyxy;

	vec4 s = vec4(xCubic.xz + xCubic.yw, yCubic.xz + yCubic.yw);

	vec4 offset = c + vec4(xCubic.y, xCubic.w, yCubic.y, yCubic.w) / s;
	offset *= 1.0 / res.xxyy;

	vec4 sample0 = texture(tex, offset.xz);
	vec4 sample1 = texture(tex, offset.yz);
	vec4 sample2 = texture(tex, offset.xw);
	vec4 sample3 = texture(tex, offset.yw);

	float sx = s.x / (s.x + s.y);
	float sy = s.z / (s.z + s.w);

	return mix(mix(sample3, sample2, sx), mix(sample1, sample0, sx), sy);
}
