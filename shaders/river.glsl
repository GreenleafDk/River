#[versions]

momentum = "#define K_MOMENTUM";
continuity = "#define K_CONTINUITY";
sedcell = "#define K_SEDCELL";
sedface = "#define K_SEDFACE";
bed = "#define K_BED";
export = "#define K_EXPORT";
brush = "#define K_BRUSH";
thalweg = "#define K_THALWEG";

#[compute]

#version 450

VERSION_DEFINES

// River in a Box: shallow water + sediment transport + bed evolution on a 2D grid.
// x runs downstream (length, NX cells), y across (width, NY cells), cell (i, j) centred at ((i+.5)dx, (j+.5)dx).
// Heights z are *relative*: the landscape's general slope S is not in z, it only appears as a downstream driving
// term g*S in the momentum equation (and in the bed slope seen by the sediment). So the view draws the land flat.
// Water: staggered grid (u on x faces, v on y faces), upwind face depths (Stelling & Duinmeijer 2003, positive and
// wet/dry safe), semi-Lagrangian momentum advection, implicit Manning friction.
// Sediment: bedload (Meyer-Peter Mueller) turned towards the inner bend by the helical flow (tan d = A h kappa) and
// down the bed slope, bank erosion driven by the near-bank speed (stronger at outer banks), avalanching past the
// angle of repose, and suspended load (pick-up by excess shear, settling). Bed: Exner, accelerated by a
// morphological factor (mdt = morfac * dt). Each cell has a loose layer (deposits, fully erodible) on a substrate
// with a painted erodibility (rock ~0, soft soil 1), and vegetation that grows on dry land and binds it.

layout(local_size_x = 128, local_size_y = 1, local_size_z = 1) in;

layout(set = 0, binding = 0, std430) restrict readonly buffer CellIn { vec4 cin[]; };   // h, c (volume conc.), exch, -
layout(set = 0, binding = 1, std430) restrict buffer CellOut { vec4 cel[]; };
layout(set = 0, binding = 2, std430) restrict readonly buffer UIn { float uin[]; };      // (NX+1) x NY
layout(set = 0, binding = 3, std430) restrict readonly buffer VIn { float vin[]; };      // NX x (NY+1)
layout(set = 0, binding = 4, std430) restrict buffer UOut { float uo[]; };
layout(set = 0, binding = 5, std430) restrict buffer VOut { float vo[]; };
layout(set = 0, binding = 6, std430) restrict buffer Bed { vec4 bed[]; };               // z, s (loose layer), e (substrate erodibility), veg
layout(set = 0, binding = 7, std430) restrict buffer Qs { vec4 qs[]; };                 // bedload qx, qy (m2/s), curvature (1/m), speed
layout(set = 0, binding = 8, std430) restrict buffer SFX { float sfx[]; };              // sediment flux through x faces (m2/s)
layout(set = 0, binding = 9, std430) restrict buffer SFY { float sfy[]; };
layout(set = 0, binding = 10, std430) restrict readonly buffer Params { float P[]; };
layout(set = 0, binding = 11, std430) restrict buffer Stats { uint stats[]; };
layout(set = 0, binding = 12, rgba32f) uniform restrict writeonly image2D tex0;         // z, h, u, v
layout(set = 0, binding = 13, rgba32f) uniform restrict writeonly image2D tex1;         // e (effective), veg, s, c
layout(set = 0, binding = 14, std430) restrict buffer Thal { vec4 thal[]; };            // per column: y of deepest water, depth, wet width, channels

layout(push_constant, std430) uniform PC {
	uint nx;
	uint ny;
	uint k;
	uint pad0;
	float dx;
	float dt;
	float mdt;     // morphological time step = dt * morfac
	float time;
} pc;

// Parameters (P[]), set from C# (RiverSim.cs, same indices).
#define G        P[0]
#define SLOPE    P[1]
#define MANNING  P[2]
#define HMIN     P[3]
#define QIN      P[4]
#define INLET_Y  P[5]
#define INLET_W  P[6]
#define INLET_A  P[7]
#define GRAIN    P[8]
#define THETA_C  P[9]
#define MPM      P[10]
#define HELIX_A  P[11]
#define SLOPE_B  P[12]
#define BANK_K   P[13]
#define OUTER_C  P[14]
#define CURV_L   P[15]
#define REPOSE_D P[16]
#define REPOSE_W P[17]
#define AVAL_F   P[18]
#define POROSITY P[19]
#define SUSP_M   P[20]
#define SETTLE   P[21]
#define VEG_T    P[22]
#define VEG_S    P[23]
#define VEG_N    P[24]
#define FEED     P[25]
#define LOOSE_S0 P[26]
#define DELTA    P[27]
#define INLET_U  P[28]
#define BANK_UC  P[29]
#define HDRY     P[30]
#define VMAX     P[31]
#define EDDY     P[32]
#define EVAP     P[33]
#define INNER_D  P[34]
#define PERIODIC P[35]   // 1: the sides (y) wrap around
#define SEA      P[36]   // 1: a sea with a fixed level at the outlet
#define SEA_LVL  P[37]   // its level, relative height at the outlet (m)
#define BED_D    P[38]   // bed smoothing under water (m2/s, morph time)
#define BR_X     P[40]
#define BR_Y     P[41]
#define BR_R     P[42]
#define BR_AMT   P[43]
#define BR_MODE  P[44]
#define BR_VAL   P[45]

// Stats words.
#define ST_WAVE   0   // max |U| + sqrt(g h) (float bits)
#define ST_NAN    1
#define ST_WET    2
#define ST_VOL    3   // water volume, m3 * 16 (fixed point)
#define ST_MAXU   4   // max |U| (float bits)
#define ST_MAXH   5
#define ST_CLAMP  6   // bed changes clamped
#define ST_SEDOUT 7   // sediment volume leaving at the outlet per step, m3 * 1e4

int NX, NY;
bool PER;
float DX;

// Rows wrap when the sides are periodic, else they are clamped (walls).
int wj(int j) { return PER ? ((j % NY) + NY) % NY : clamp(j, 0, NY - 1); }
int cid(int i, int j) { return wj(j) * NX + clamp(i, 0, NX - 1); }
int uid(int i, int j) { return wj(j) * (NX + 1) + i; }   // x face i (between cells i-1 and i), row j
int vid(int i, int j) { return (PER ? ((j % NY) + NY) % NY : j) * NX + i; }   // y face j (between cells j-1 and j), column i; periodic: face NY = face 0

bool inletRow(int j) {
	float y = (float(j) + 0.5) * DX;
	return abs(y - INLET_Y) <= max(INLET_W * 0.5, DX * 0.51);
}

int inletRows() {
	int n = 0;
	// Small loop over the rows near the inlet: the count of source cells (the inflow is spread over them).
	int j0 = int(floor((INLET_Y - INLET_W * 0.5) / DX)) - 1;
	int j1 = int(ceil((INLET_Y + INLET_W * 0.5) / DX)) + 1;
	for (int j = max(j0, 0); j <= min(j1, NY - 1); j++) if (inletRow(j)) n++;
	return max(n, 1);
}

// Erodibility of the top of the bed: the loose layer is fully erodible, the substrate has its painted value,
// vegetation binds both.
float erodibility(vec4 b) {
	float e = mix(b.z, 1.0, clamp(b.y / max(LOOSE_S0, 1e-4), 0.0, 1.0));
	return e * (1.0 - VEG_S * b.w);
}

float manningAt(vec4 b) { return MANNING + VEG_N * b.w; }

void statMax(uint word, float v) { if (v > 0.0) atomicMax(stats[word], floatBitsToUint(v)); }

void main() {
	NX = int(pc.nx);
	NY = int(pc.ny);
	DX = pc.dx;
	PER = PERIODIC > 0.5;
	uint t = gl_GlobalInvocationID.x;
	float dt = pc.dt;

#if defined(K_MOMENTUM)
	int nu = (NX + 1) * NY, nv = NX * (NY + 1);
	float ia = INLET_A;
	if (int(t) < nu) {
		int i = int(t) % (NX + 1), j = int(t) / (NX + 1);
		float u = 0.0;
		if (i > 0) {
			// Cells left (L) and right (R); at the outlet (i == NX) the ghost cell is a copy of the last one:
			// zero gradient of the relative surface, i.e. normal-depth outflow driven by the slope.
			int cl = cid(i - 1, j), cr = cid(min(i, NX - 1), j);
			float hL = cin[cl].x, hR = cin[cr].x;
			float zL = bed[cl].x, zR = bed[cr].x;
			if (i == NX) { zR = zL; hR = SEA > 0.5 ? max(SEA_LVL - zL, 0.0) : hL; }   // sea: fixed level outside
			float eL = zL + hL, eR = zR + hR;
			float u0 = uin[t];
			// v at this face: mean of the four surrounding v faces.
			int ia0 = i - 1, ib0 = min(i, NX - 1);
			float v0 = 0.25 * (vin[vid(ia0, j)] + vin[vid(ia0, j + 1)] + vin[vid(ib0, j)] + vin[vid(ib0, j + 1)]);
			// Semi-Lagrangian advection: sample u at the departure point (bilinear on the u grid).
			float px = float(i) - dt * u0 / DX, py = float(j) - dt * v0 / DX;   // in u-grid index units (y offset .5 removed)
			px = clamp(px, 0.0, float(NX));
			int x0 = min(int(floor(px)), NX - 1), y0, y1;
			float fy;
			if (PER) { y0 = int(floor(py)); fy = py - float(y0); y1 = y0 + 1; }   // uid wraps the rows
			else {
				py = clamp(py, 0.0, float(NY - 1));
				y0 = max(min(int(floor(py)), NY - 2), 0);
				fy = clamp(py - float(y0), 0.0, 1.0);
				y1 = min(y0 + 1, NY - 1);
			}
			float fx = px - float(x0);
			float ua = mix(uin[uid(x0, y0)], uin[uid(x0 + 1, y0)], fx);
			float ub = mix(uin[uid(x0, y1)], uin[uid(x0 + 1, y1)], fx);
			u = mix(ua, ub, fy);
			// Turbulent mixing between neighbouring faces (lateral momentum exchange; damps grid-scale streaks).
			if (EDDY > 0.0) {
				float lap = 0.0;
				if (i < NX) lap += uin[uid(i + 1, j)] - u0;
				if (i > 1) lap += uin[uid(i - 1, j)] - u0;
				if (j < NY - 1 || PER) lap += uin[uid(i, j + 1)] - u0;
				if (j > 0 || PER) lap += uin[uid(i, j - 1)] - u0;
				u += min(dt * EDDY / (DX * DX), 0.2) * lap;
			}
			// Pressure gradient of the relative surface + the general slope.
			u += dt * G * (-(eR - eL) / DX + SLOPE);
			// Upwind face depth; dry face -> no flow.
			float hf = u > 0.0 ? hL : hR;
			if (hf < HMIN || max(eL, eR) - max(zL, zR) < HMIN) u = 0.0;
			else {
				float n = manningAt(0.5 * (bed[cl] + bed[cr]));
				float sp = sqrt(u * u + v0 * v0);
				u /= 1.0 + dt * G * n * n * sp / pow(max(hf, HMIN), 4.0 / 3.0);
			}
			if (i == NX && SEA < 0.5) u = max(u, 0.0);   // no backflow, except from the sea
			// Inflow momentum: the water from the inlet arrives with the inlet's speed and direction.
			if (i == 1 && inletRow(j)) u = mix(u, INLET_U * cos(ia), 0.5);
		}
		if (isnan(u) || isinf(u)) { u = 0.0; atomicAdd(stats[ST_NAN], 1u); }
		// At most a quarter of a cell per step through each face: a cell can never send out more water than it holds
		// (the step size comes from the previous frame, so a sudden speed-up must not break positivity).
		float umax = min(VMAX, 0.25 * DX / dt);
		uo[t] = clamp(u, -umax, umax);
	}
	if (int(t) < nv) {
		int i = int(t) % NX, j = int(t) / NX;
		float v = 0.0;
		if ((j > 0 && j < NY) || PER) {
			int cb = cid(i, j - 1), ct = cid(i, j);
			float hB = cin[cb].x, hT = cin[ct].x;
			float zB = bed[cb].x, zT = bed[ct].x;
			float eB = zB + hB, eT = zT + hT;
			float v0 = vin[t];
			float u0 = 0.25 * (uin[uid(i, j - 1)] + uin[uid(i + 1, j - 1)] + uin[uid(i, j)] + uin[uid(i + 1, j)]);
			float px = float(i) - dt * u0 / DX, py = float(j) - dt * v0 / DX;   // v-grid index units (x offset .5 removed)
			px = clamp(px, 0.0, float(NX - 1));
			if (!PER) py = clamp(py, 0.0, float(NY));
			int x0 = max(min(int(floor(px)), NX - 2), 0), y0 = PER ? int(floor(py)) : min(int(floor(py)), NY - 1);
			int x1 = min(x0 + 1, NX - 1);
			float fx = clamp(px - float(x0), 0.0, 1.0), fy = py - float(y0);
			float va = mix(vin[vid(x0, y0)], vin[vid(x1, y0)], fx);
			float vb = mix(vin[vid(x0, y0 + 1)], vin[vid(x1, y0 + 1)], fx);
			v = mix(va, vb, fy);
			if (EDDY > 0.0) {
				float lap = 0.0;
				if (i < NX - 1) lap += vin[vid(i + 1, j)] - v0;
				if (i > 0) lap += vin[vid(i - 1, j)] - v0;
				if (j < NY - 1 || PER) lap += vin[vid(i, j + 1)] - v0;
				if (j > 1 || PER) lap += vin[vid(i, j - 1)] - v0;
				v += min(dt * EDDY / (DX * DX), 0.2) * lap;
			}
			v += dt * G * (-(eT - eB) / DX);
			float hf = v > 0.0 ? hB : hT;
			if (hf < HMIN || max(eB, eT) - max(zB, zT) < HMIN) v = 0.0;
			else {
				float n = manningAt(0.5 * (bed[cb] + bed[ct]));
				float sp = sqrt(v * v + u0 * u0);
				v /= 1.0 + dt * G * n * n * sp / pow(max(hf, HMIN), 4.0 / 3.0);
			}
			if (i == 0 && (inletRow(j) || inletRow(j - 1))) v = mix(v, INLET_U * sin(ia), 0.5);
		}
		if (isnan(v) || isinf(v)) { v = 0.0; atomicAdd(stats[ST_NAN], 1u); }
		float vmax = min(VMAX, 0.25 * DX / dt);
		vo[t] = clamp(v, -vmax, vmax);
	}

#elif defined(K_CONTINUITY)
	if (int(t) >= NX * NY) return;
	int i = int(t) % NX, j = int(t) / NX;
	vec4 c0 = cin[t];
	float h = c0.x, cc = c0.y;
	float uw = uo[uid(i, j)], ue = uo[uid(i + 1, j)];
	float vs = vo[vid(i, j)], vn = vo[vid(i, j + 1)];
	// Upwind depths and concentrations on the four faces.
	vec4 W = i > 0 ? cin[cid(i - 1, j)] : c0;
	vec4 E = i < NX - 1 ? cin[cid(i + 1, j)] : c0;
	vec4 S = cin[cid(i, j - 1)];
	vec4 N = cin[cid(i, j + 1)];
	float fw = uw * (uw > 0.0 ? W.x : h), cw = uw > 0.0 ? W.y : cc;
	float fe = ue * (ue > 0.0 ? h : E.x), ce = ue > 0.0 ? cc : E.y;
	float gs = vs * (vs > 0.0 ? S.x : h), cs = vs > 0.0 ? S.y : cc;
	float gn = vn * (vn > 0.0 ? h : N.x), cn = vn > 0.0 ? cc : N.y;
	if (i == 0) { fw = 0.0; }
	if (j == 0 && !PER) { gs = 0.0; }
	if (j == NY - 1 && !PER) { gn = 0.0; }
	float m = h * cc;   // suspended sediment volume per area
	float hn = h - dt / DX * (fe - fw + gn - gs);
	float mn = m - dt / DX * (fe * ce - fw * cw + gn * cn - gs * cs);

	vec4 b = bed[t];
	float eff = erodibility(b);
	float uc = 0.5 * (uw + ue), vc = 0.5 * (vs + vn);
	float sp = sqrt(uc * uc + vc * vc);
	float n = manningAt(b);
	float theta = h > HDRY ? n * n * sp * sp / (pow(h, 1.0 / 3.0) * DELTA * GRAIN) : 0.0;
	float pick = SUSP_M * max(theta / THETA_C - 1.0, 0.0);   // m/s of bed volume, before erodibility

	// Inlet: the inflow (and the sediment it carries) enters the first column's inlet cells.
	if (i == 0 && inletRow(j)) {
		float q = QIN / (float(inletRows()) * DX * DX);
		float cin_ = FEED * pick / max(SETTLE, 1e-6);   // concentration in equilibrium with this cell's shear
		hn += dt * q;
		mn += dt * q * min(cin_, 0.05);
	}
	if (j == NY - 1 && i == NX - 1) {}
	if (i == NX - 1 && ue > 0.0) atomicAdd(stats[ST_SEDOUT], uint(max(dt * fe * ce * DX * 1e4, 0.0)));

	float ero = pick * eff * (1.0 - POROSITY);   // volume of grains picked up per area per s
	float dep;
	if (hn > HDRY) {
		dep = SETTLE * mn / hn;
		dep = min(dep, max(mn, 0.0) / dt);
	} else {
		dep = max(mn, 0.0) / dt;   // too shallow: everything settles
		ero = 0.0;
	}
	mn += dt * (ero - dep);
	// Evaporation and seepage (hydro time): puddles left on the floodplain dry up.
	hn -= min(max(hn, 0.0), EVAP * dt);
	hn = max(hn, 0.0);
	mn = max(mn, 0.0);
	float cnew = hn > 1e-4 ? min(mn / hn, 0.2) : 0.0;
	// exch: bed rise rate from the suspended exchange (m/s of bed, porosity included)
	float exch = (dep - ero) / (1.0 - POROSITY);
	if (isnan(hn) || isinf(hn)) { hn = 0.0; cnew = 0.0; exch = 0.0; atomicAdd(stats[ST_NAN], 1u); }
	cel[t] = vec4(hn, cnew, exch, 0.0);
	if (hn > HMIN && pc.k == 1u) {
		atomicAdd(stats[ST_WET], 1u);
		atomicAdd(stats[ST_VOL], uint(hn * DX * DX * 16.0));
		statMax(ST_WAVE, sp + sqrt(G * hn));
		statMax(ST_MAXU, sp);
		statMax(ST_MAXH, hn);
	}

#elif defined(K_SEDCELL)
	if (int(t) >= NX * NY) return;
	int i = int(t) % NX, j = int(t) / NX;
	float h = cel[t].x;
	vec4 b = bed[t];
	float uc = 0.5 * (uo[uid(i, j)] + uo[uid(i + 1, j)]);
	float vc = 0.5 * (vo[vid(i, j)] + vo[vid(i, j + 1)]);
	float sp = sqrt(uc * uc + vc * vc);
	// Bank cell: (nearly) dry ground next to flowing water. It is undercut and slides into the channel: a flux down
	// its own slope (Sobel normal, so banks retreat the same way in every direction, not along the grid axes), at a
	// rate set by the fastest neighbouring water, more on the outside of bends and where the flow runs into it.
	{
		float bestSp = 0.0, bestH = 0.0, bestK = 0.0, zMin = b.x;
		vec2 bestVel = vec2(0.0), bestDir = vec2(0.0);
		for (int dj = -1; dj <= 1; dj++)
			for (int di = -1; di <= 1; di++) {
				if (di == 0 && dj == 0) continue;
				int ni = i + di, nj = j + dj;
				if (ni < 0 || ni >= NX || (!PER && (nj < 0 || nj >= NY))) continue;
				nj = wj(nj);
				int c = nj * NX + ni;
				float hn = cel[c].x;
				if (hn < HDRY || hn < 4.0 * h) continue;
				vec2 vn = vec2(0.5 * (uo[uid(ni, nj)] + uo[uid(ni + 1, nj)]), 0.5 * (vo[vid(ni, nj)] + vo[vid(ni, nj + 1)]));
				float s = length(vn);
				zMin = min(zMin, bed[c].x);
				bestH = max(bestH, hn);
				if (s > bestSp) { bestSp = s; bestVel = vn; bestK = qs[c].z; bestDir = normalize(vec2(di, dj)); }
			}
		if (bestH > 0.0) {
			vec4 q = vec4(0.0, 0.0, 0.0, sp);
			float bankH = b.x - zMin;
			if (bestSp > BANK_UC && bankH > 0.0) {
				float z00 = bed[cid(i - 1, j - 1)].x, z10 = bed[cid(i, j - 1)].x, z20 = bed[cid(i + 1, j - 1)].x;
				float z01 = bed[cid(i - 1, j)].x, z21 = bed[cid(i + 1, j)].x;
				float z02 = bed[cid(i - 1, j + 1)].x, z12 = bed[cid(i, j + 1)].x, z22 = bed[cid(i + 1, j + 1)].x;
				vec2 g = vec2(z20 + 2.0 * z21 + z22 - z00 - 2.0 * z01 - z02, z02 + 2.0 * z12 + z22 - z00 - 2.0 * z10 - z20);
				vec2 nrm = length(g) > 1e-5 ? -normalize(g) : bestDir;
				vec2 toBank = -bestDir;   // from the water towards this bank
				float boost = 1.0;
				if (bestSp > 0.05) {
					vec2 left = vec2(-bestVel.y, bestVel.x) / bestSp;
					vec2 centre = sign(bestK) * left;   // towards the bend's centre of curvature
					boost += OUTER_C * clamp(abs(bestK) * CURV_L, 0.0, 1.0) * max(-dot(toBank, centre), 0.0);
					boost += max(dot(bestVel / bestSp, toBank), 0.0);
				}
				float rate = BANK_K * (bestSp - BANK_UC) * boost * erodibility(b);   // m of retreat per morph second
				float vol = rate * min(bankH, 3.0 * max(bestH, 0.2));
				vol = min(vol, 0.2 * bankH * DX / max(pc.mdt, 1e-6));
				q.xy = vol * nrm;
			}
			qs[t] = q;
			return;
		}
	}
	if (h < HDRY || sp < 1e-3) { qs[t] = vec4(0.0, 0.0, 0.0, sp); return; }
	// Neighbour cell velocities (one-sided at dry neighbours) for the streamline curvature.
	vec2 vel = vec2(uc, vc);
	vec2 vE = vel, vW = vel, vN = vel, vS = vel;
	float wE = 0.0, wW = 0.0, wN = 0.0, wS = 0.0;
	if (i < NX - 1 && cel[cid(i + 1, j)].x > HDRY) { vE = vec2(0.5 * (uo[uid(i + 1, j)] + uo[uid(i + 2, j)]), 0.5 * (vo[vid(i + 1, j)] + vo[vid(i + 1, j + 1)])); wE = 1.0; }
	if (i > 0 && cel[cid(i - 1, j)].x > HDRY) { vW = vec2(0.5 * (uo[uid(i - 1, j)] + uo[uid(i, j)]), 0.5 * (vo[vid(i - 1, j)] + vo[vid(i - 1, j + 1)])); wW = 1.0; }
	if ((j < NY - 1 || PER) && cel[cid(i, j + 1)].x > HDRY) { vN = vec2(0.5 * (uo[uid(i, j + 1)] + uo[uid(i + 1, j + 1)]), 0.5 * (vo[vid(i, j + 1)] + vo[vid(i, j + 2)])); wN = 1.0; }
	if ((j > 0 || PER) && cel[cid(i, j - 1)].x > HDRY) { vS = vec2(0.5 * (uo[uid(i, j - 1)] + uo[uid(i + 1, j - 1)]), 0.5 * (vo[vid(i, j - 1)] + vo[vid(i, j)])); wS = 1.0; }
	vec2 dX = (vE - vW) / (DX * max(wE + wW, 1.0));
	vec2 dY = (vN - vS) / (DX * max(wN + wS, 1.0));
	float kappa = (uc * (uc * dX.y - vc * dX.x) + vc * (uc * dY.y - vc * dY.x)) / (sp * sp * sp);
	kappa = clamp(kappa, -0.5 / DX, 0.5 / DX);
	if (sp < 0.05) kappa = 0.0;

	float n = manningAt(b);
	float theta = n * n * sp * sp / (pow(h, 1.0 / 3.0) * DELTA * GRAIN);
	float ex = max(theta - THETA_C, 0.0);
	float qmag = MPM * sqrt(DELTA * G * GRAIN * GRAIN * GRAIN) * ex * sqrt(ex) * erodibility(b);
	// Helical flow: the near-bed flow turns towards the inside of the bend.
	// Inner bend deposition scales it: more turn = more sand carried into the inside of the bend (point bars).
	float delta = clamp(atan(HELIX_A * INNER_D * h * kappa), -0.6, 0.6);
	vec2 d = vel / sp;
	vec2 dir = vec2(d.x * cos(delta) - d.y * sin(delta), d.x * sin(delta) + d.y * cos(delta));
	// Bed slope: grains roll downhill (Talmon-like 1/sqrt(theta) weight). Total slope includes the general fall.
	float zE = bed[cid(i + 1, j)].x, zW = bed[cid(i - 1, j)].x, zN = bed[cid(i, j + 1)].x, zS = bed[cid(i, j - 1)].x;
	vec2 grad = vec2((zE - zW) / (2.0 * DX) - SLOPE, (zN - zS) / (2.0 * DX));
	vec2 corr = SLOPE_B / sqrt(max(theta, 0.02)) * grad;
	float cl = length(corr);
	if (cl > 1.0) corr /= cl;
	qs[t] = vec4(qmag * (dir - corr), kappa, sp);

#elif defined(K_SEDFACE)
	int nu = (NX + 1) * NY, nv = NX * (NY + 1);
	float mdt = max(pc.mdt, 1e-6);
	for (int axis = 0; axis < 2; axis++) {
		if (axis == 0 && int(t) >= nu) continue;
		if (axis == 1 && int(t) >= nv) continue;
		int i, j, ca, cb;   // cb = the cell on the positive side of the face
		vec2 nrm;
		float f = 0.0;
		if (axis == 0) {
			i = int(t) % (NX + 1); j = int(t) / (NX + 1);
			if (i == 0) {
				// Inlet: feed the bedload the first cell can carry (FEED = 1: equilibrium, no scour at the inlet).
				if (inletRow(j)) f = FEED * max(qs[cid(0, j)].x, 0.0);
				sfx[t] = f; continue;
			}
			if (i == NX) { sfx[t] = max(qs[cid(NX - 1, j)].x, 0.0); continue; }
			ca = cid(i - 1, j); cb = cid(i, j); nrm = vec2(1.0, 0.0);
			f = max(qs[ca].x, 0.0) + min(qs[cb].x, 0.0);
		} else {
			i = int(t) % NX; j = int(t) / NX;
			if (!PER && (j == 0 || j == NY)) { sfy[t] = 0.0; continue; }
			ca = cid(i, j - 1); cb = cid(i, j); nrm = vec2(0.0, 1.0);
			f = max(qs[ca].y, 0.0) + min(qs[cb].y, 0.0);
		}
		vec4 ba = bed[ca], bb = bed[cb];
		float ha = cel[ca].x, hb = cel[cb].x;
		// Avalanching: past the angle of repose (lower when wet), the higher side slides down.
		float dz = bb.x - ba.x - (axis == 0 ? SLOPE * DX : 0.0);   // true height difference across the face
		bool wet = ha > HDRY || hb > HDRY;
		float tanr = wet ? REPOSE_W : REPOSE_D;
		// Bed smoothing under water: a little diffusion damps grid-sized bumps the bedload would otherwise grow
		// (puddle-dotted sand bars). Limited for stability of the explicit update.
		if (ha > HDRY && hb > HDRY && BED_D > 0.0) {   // both sides under water: banks are left alone
			float dd = min(BED_D * min(erodibility(ba), erodibility(bb)), 0.15 * DX * DX / mdt);
			f -= dd * dz / DX;
		}
		float excess = abs(dz) - tanr * DX;
		if (excess > 0.0) {
			float donorE = dz > 0.0 ? erodibility(bb) : erodibility(ba);
			float vol = AVAL_F * excess * 0.5 * donorE * DX / mdt;   // per face length per morph second
			f += dz > 0.0 ? -vol : vol;
		}
		if (axis == 0) sfx[t] = f; else sfy[t] = f;
	}

#elif defined(K_BED)
	if (int(t) >= NX * NY) return;
	int i = int(t) % NX, j = int(t) / NX;
	float div = (sfx[uid(i + 1, j)] - sfx[uid(i, j)] + sfy[vid(i, j + 1)] - sfy[vid(i, j)]) / DX;
	vec4 c = cel[t];
	float dz = pc.mdt * (-div / (1.0 - POROSITY) + c.z);
	float lim = 0.05 * DX;
	if (abs(dz) > lim) { dz = clamp(dz, -lim, lim); atomicAdd(stats[ST_CLAMP], 1u); }
	vec4 b = bed[t];
	b.x += dz;
	b.y = max(b.y + dz, 0.0);
	if (c.x > HMIN) c.x = max(c.x - dz, 0.0);   // the water column shrinks over a rising bed (and deepens over scour)
	// Vegetation: grows on dry ground, drowns under deep or fast water, is torn out by fast bed change.
	float h = c.x;
	if (VEG_T > 0.0) {
		if (h < HDRY) b.w += pc.mdt / VEG_T * (1.0 - b.w);
		else b.w -= pc.mdt / VEG_T * 4.0 * clamp(h / 0.3, 0.0, 1.0) * b.w;
		b.w -= clamp(abs(dz) / 0.05, 0.0, 1.0) * b.w;
		b.w = clamp(b.w, 0.0, 1.0);
	} else b.w = 0.0;
	if (isnan(b.x)) { b = vec4(0.0, 0.0, 1.0, 0.0); atomicAdd(stats[ST_NAN], 1u); }
	bed[t] = b;
	cel[t] = c;

#elif defined(K_EXPORT)
	if (int(t) >= NX * NY) return;
	int i = int(t) % NX, j = int(t) / NX;
	vec4 c = cel[t];
	vec4 b = bed[t];
	float uc = 0.5 * (uo[uid(i, j)] + uo[uid(i + 1, j)]);
	float vc = 0.5 * (vo[vid(i, j)] + vo[vid(i, j + 1)]);
	imageStore(tex0, ivec2(i, j), vec4(b.x, c.x, uc, vc));
	imageStore(tex1, ivec2(i, j), vec4(b.z, b.w, b.y, c.y));

#elif defined(K_BRUSH)
	if (int(t) >= NX * NY) return;
	int i = int(t) % NX, j = int(t) / NX;
	vec2 p = (vec2(i, j) + 0.5) * DX;
	vec2 dp = p - vec2(BR_X, BR_Y);
	if (PER) dp.y -= round(dp.y / (float(NY) * DX)) * float(NY) * DX;   // the brush reaches across the seam
	float r = length(dp) / max(BR_R, 1e-3);
	if (r >= 1.0) return;
	float w = 0.5 + 0.5 * cos(3.14159265 * r);   // smooth bump
	vec4 b = bed[t];
	vec4 c = cel[t];
	int mode = int(BR_MODE + 0.5);
	float a = BR_AMT * w;
	if (mode == 0) { b.x += a; }                                    // raise
	else if (mode == 1) { b.x -= a; b.y = max(b.y - a, 0.0); }      // lower
	else if (mode == 2) {                                           // smooth towards the neighbours' mean
		float m = 0.25 * (bed[cid(i + 1, j)].x + bed[cid(i - 1, j)].x + bed[cid(i, j + 1)].x + bed[cid(i, j - 1)].x);
		b.x = mix(b.x, m, clamp(w * BR_AMT, 0.0, 1.0));
	}
	else if (mode == 3) { b.z = mix(b.z, BR_VAL, clamp(w * BR_AMT, 0.0, 1.0)); b.y *= 1.0 - clamp(w * BR_AMT, 0.0, 1.0); }   // paint material
	else if (mode == 4) { c.x += a; }                               // add water
	else if (mode == 5) { c.x = max(c.x - a, 0.0); }                // remove water
	else if (mode == 6) { b.w = mix(b.w, BR_VAL, clamp(w * BR_AMT, 0.0, 1.0)); }   // paint vegetation
	bed[t] = b;
	cel[t] = c;

#elif defined(K_THALWEG)
	if (int(t) >= NX) return;
	int i = int(t);
	float best = 0.0, by = -1.0, width = 0.0, segs = 0.0;
	bool was = false;
	for (int j = 0; j < NY; j++) {
		float h = cel[cid(i, j)].x;
		bool wet = h > 0.1;
		if (wet) width += DX;
		if (wet && !was) segs += 1.0;
		was = wet;
		if (h > best) { best = h; by = (float(j) + 0.5) * DX; }
	}
	thal[i] = vec4(by, best, width, segs);
#endif
}
