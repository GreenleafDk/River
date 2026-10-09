shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

// The ground. One vertex per sim cell (UV = cell index); heights from tex0 (z, h, u, v), soil from tex1
// (substrate erodibility, vegetation, loose layer, suspended conc.). The general slope is not drawn.

uniform sampler2D tex0 : filter_nearest;
uniform sampler2D tex1 : filter_nearest;
uniform float vscale = 4.0;
uniform float dx = 2.0;
uniform int view_mode = 0;   // 0 natural, 1 speed, 2 depth, 3 material
uniform float contour = 0.0; // contour spacing in metres (0: none)

varying vec4 s0;
varying vec4 s1;
varying float wz;

float zat(ivec2 c, ivec2 sz) { return texelFetch(tex0, clamp(c, ivec2(0), sz - 1), 0).x; }

void vertex() {
	ivec2 c = ivec2(UV + 0.5);
	ivec2 sz = textureSize(tex0, 0);
	s0 = texelFetch(tex0, c, 0);
	s1 = texelFetch(tex1, c, 0);
	float zl = zat(c - ivec2(1, 0), sz), zr = zat(c + ivec2(1, 0), sz);
	float zd = zat(c - ivec2(0, 1), sz), zu = zat(c + ivec2(0, 1), sz);
	VERTEX.y = s0.x * vscale;
	wz = s0.x;
	NORMAL = normalize(vec3(-(zr - zl) * vscale / (2.0 * dx), 1.0, -(zu - zd) * vscale / (2.0 * dx)));
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

void fragment() {
	vec3 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float grain = hash(floor(world.xz * 3.0)) * 0.08 - 0.04;
	vec3 soil = vec3(0.42, 0.33, 0.23);
	vec3 sand = vec3(0.80, 0.71, 0.52);
	vec3 rock = vec3(0.47, 0.47, 0.49);
	vec3 plant = vec3(0.22, 0.38, 0.14);
	float e = s1.x, veg = s1.y, loose = s1.z, h = s0.y;
	vec3 col = mix(soil, sand, smoothstep(0.0, 0.15, loose));
	col = mix(col, rock, smoothstep(0.6, 0.1, e));
	col = mix(col, plant, veg * 0.85 * smoothstep(0.02, 0.0, h));
	if (view_mode == 3) {
		col = mix(vec3(0.55, 0.38, 0.2), vec3(0.95, 0.85, 0.55), smoothstep(0.0, 0.15, loose));
		col = mix(col, vec3(0.35, 0.4, 0.55), smoothstep(0.6, 0.1, e));
		col = mix(col, vec3(0.1, 0.6, 0.15), veg);
	}
	col *= 1.0 + grain;
	// Wet ground is darker.
	col *= mix(1.0, 0.6, smoothstep(0.0, 0.05, h));
	if (contour > 0.0) {
		float f = wz / contour;
		float w = fwidth(f);
		float line = 1.0 - smoothstep(0.0, w * 1.5, abs(fract(f + 0.5) - 0.5));
		col = mix(col, col * 0.55, line * 0.7);
	}
	ALBEDO = col;
	ROUGHNESS = 0.92;
	SPECULAR = 0.2;
}
