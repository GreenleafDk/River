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

varying float depth;
varying float speed;
varying float conc;

float eta(ivec2 c, ivec2 sz) {
	vec4 s = texelFetch(tex0, clamp(c, ivec2(0), sz - 1), 0);
	return s.x + max(s.y, 0.0);
}

void vertex() {
	ivec2 c = ivec2(UV + 0.5);
	ivec2 sz = textureSize(tex0, 0);
	vec4 s = texelFetch(tex0, c, 0);
	depth = s.y;
	speed = length(s.zw);
	conc = texelFetch(tex1, c, 0).w;
	VERTEX.y = (s.x + s.y) * vscale - (s.y < 0.01 ? 0.08 * vscale : 0.0);
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
