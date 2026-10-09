shader_type spatial;
render_mode cull_disabled, specular_schlick_ggx;

// The ground. One vertex per sim cell (UV = cell index); heights from tex0 (z, h, u, v), soil from tex1
// (substrate erodibility, vegetation, loose layer, suspended conc.). The general slope is not drawn.
// smooth_amt blends each vertex towards a 5x5 binomial average of the heights (view only): cell staircases on steep banks
// that cross the grid at an angle look jagged otherwise.

uniform sampler2D tex0 : filter_nearest;
uniform sampler2D tex1 : filter_nearest;
uniform float vscale = 4.0;
uniform float dx = 2.0;
uniform int view_mode = 0;   // 0 natural, 1 speed, 2 depth, 3 material
uniform float contour = 0.0; // contour spacing in metres (0: none)
uniform float smooth_amt = 1.0;
uniform float fall = 0.0;    // the general fall (fraction) when it is shown: heights drop by fall * x

varying vec4 s0;
varying vec4 s1;
varying float wz;

float zat(ivec2 c, ivec2 sz) { return texelFetch(tex0, clamp(c, ivec2(0), sz - 1), 0).x; }

float zsm(ivec2 c, ivec2 sz) {
	float z = zat(c, sz);
	if (smooth_amt <= 0.0) return z;
	// 5x5 binomial (1 4 6 4 1)^2 / 256: wide enough to round off bank steps of several cells.
	const float k[5] = float[5](1.0, 4.0, 6.0, 4.0, 1.0);
	float s = 0.0;
	for (int dj = -2; dj <= 2; dj++)
		for (int di = -2; di <= 2; di++)
			s += k[di + 2] * k[dj + 2] * zat(c + ivec2(di, dj), sz);
	return mix(z, s / 256.0, smooth_amt);
}

void vertex() {
	ivec2 c = ivec2(UV + 0.5);
	ivec2 sz = textureSize(tex0, 0);
	s0 = texelFetch(tex0, c, 0);
	s1 = texelFetch(tex1, c, 0);
	float z = zsm(c, sz);
	float zl = zsm(c - ivec2(1, 0), sz), zr = zsm(c + ivec2(1, 0), sz);
	float zd = zsm(c - ivec2(0, 1), sz), zu = zsm(c + ivec2(0, 1), sz);
	VERTEX.y = (z - fall * VERTEX.x) * vscale;
	wz = z - fall * VERTEX.x;
	NORMAL = normalize(vec3(-((zr - zl) / (2.0 * dx) - fall) * vscale, 1.0, -(zu - zd) * vscale / (2.0 * dx)));
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
