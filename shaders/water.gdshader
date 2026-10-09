shader_type spatial;
render_mode blend_mix, cull_disabled, depth_draw_opaque, specular_schlick_ggx;

// The water surface: same grid as the ground, raised by the depth h. Dry vertices sink just under the ground so the
// shoreline is where the surface meets it.

uniform sampler2D tex0 : filter_nearest;
uniform sampler2D tex1 : filter_nearest;
uniform float vscale = 4.0;
uniform float dx = 2.0;
uniform int view_mode = 0;   // 0 natural, 1 speed, 2 depth, 3 material
uniform float max_speed = 3.0;
uniform float max_depth = 3.0;

uniform float smooth_amt = 1.0;

varying float depth;
varying float speed;
varying float conc;

float zraw(ivec2 c, ivec2 sz) { return texelFetch(tex0, clamp(c, ivec2(0), sz - 1), 0).x; }

// Ground as the terrain shader draws it (same 5x5 binomial blend).
float zsm(ivec2 c, ivec2 sz) {
	float z = zraw(c, sz);
	if (smooth_amt <= 0.0) return z;
	// 5x5 binomial (1 4 6 4 1)^2 / 256: wide enough to round off bank steps of several cells.
	const float k[5] = float[5](1.0, 4.0, 6.0, 4.0, 1.0);
	float s = 0.0;
	for (int dj = -2; dj <= 2; dj++)
		for (int di = -2; di <= 2; di++)
			s += k[di + 2] * k[dj + 2] * zraw(c + ivec2(di, dj), sz);
	return mix(z, s / 256.0, smooth_amt);
}

// Water surface smoothed over the wet cells only (dry neighbours would pull it down onto the land).
float eta(ivec2 c, ivec2 sz) {
	vec4 s = texelFetch(tex0, clamp(c, ivec2(0), sz - 1), 0);
	float e0 = s.x + max(s.y, 0.0);
	if (smooth_amt <= 0.0 || s.y < 0.02) return e0;
	float sum = 0.0, wsum = 0.0;
	for (int dj = -1; dj <= 1; dj++)
		for (int di = -1; di <= 1; di++) {
			vec4 n = texelFetch(tex0, clamp(c + ivec2(di, dj), ivec2(0), sz - 1), 0);
			if (n.y < 0.02) continue;
			float w = (di == 0 ? 2.0 : 1.0) * (dj == 0 ? 2.0 : 1.0);
			sum += w * (n.x + n.y);
			wsum += w;
		}
	return mix(e0, sum / wsum, smooth_amt);
}

void vertex() {
	ivec2 c = ivec2(UV + 0.5);
	ivec2 sz = textureSize(tex0, 0);
	vec4 s = texelFetch(tex0, c, 0);
	speed = length(s.zw);
	conc = texelFetch(tex1, c, 0).w;
	float zg = zsm(c, sz);
	float e = eta(c, sz);
	depth = s.y < 0.02 ? 0.0 : max(e - zg, s.y * 0.5);
	VERTEX.y = s.y < 0.02 ? (zg - 0.08) * vscale : e * vscale;
	float el = eta(c - ivec2(1, 0), sz), er = eta(c + ivec2(1, 0), sz);
	float ed = eta(c - ivec2(0, 1), sz), eu = eta(c + ivec2(0, 1), sz);
	NORMAL = normalize(vec3(-(er - el) * vscale / (2.0 * dx), 1.0, -(eu - ed) * vscale / (2.0 * dx)));
}

vec3 ramp(float t) {
	t = clamp(t, 0.0, 1.0);
	vec3 a = vec3(0.10, 0.25, 0.75), b = vec3(0.10, 0.80, 0.80), c = vec3(0.95, 0.90, 0.20), d = vec3(0.95, 0.25, 0.10);
	return t < 0.33 ? mix(a, b, t / 0.33) : t < 0.66 ? mix(b, c, (t - 0.33) / 0.33) : mix(c, d, (t - 0.66) / 0.34);
}

void fragment() {
	if (depth < 0.02) discard;
	float d = 1.0 - exp(-depth / 1.2);
	vec3 shallow = vec3(0.30, 0.55, 0.55), deep = vec3(0.04, 0.17, 0.28);
	vec3 col = mix(shallow, deep, d);
	col = mix(col, vec3(0.55, 0.45, 0.30), clamp(conc * 40.0, 0.0, 0.6));   // muddy with suspended sediment
	float a = clamp(0.25 + d * 0.8, 0.0, 0.9) * smoothstep(0.02, 0.06, depth);
	if (view_mode == 1) { col = ramp(speed / max_speed); a = 0.9 * smoothstep(0.02, 0.06, depth); }
	else if (view_mode == 2) { col = ramp(depth / max_depth); a = 0.9 * smoothstep(0.02, 0.06, depth); }
	ALBEDO = col;
	ALPHA = a;
	ROUGHNESS = view_mode == 0 ? 0.08 : 0.6;
	SPECULAR = 0.5;
	METALLIC = 0.0;
}
