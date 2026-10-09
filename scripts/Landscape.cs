using System;
using Godot;

namespace RiverInABox;

/// <summary>
/// The landscape at the start: size in metres, the general fall (%, a driving force only: the view draws the land
/// flat), the grid, and per cell the relative height z, loose layer, substrate erodibility, vegetation and water.
/// Built on the CPU from a preset and uploaded once.
/// </summary>
public sealed class Landscape
{
	public float Width = 300f;        // across (m)
	public float Length = 1200f;      // downstream (m)
	public float SlopePercent = 0.3f; // general fall, % (drive only)
	public int CellsAcross = 150;     // resolution
	public float InletY = 150f;       // inlet position across (m)
	public float InletWidth = 16f;    // inlet / starting channel width (m)
	public float InletAngle = 12f;    // inflow direction, degrees from straight downstream (+ = towards +y)
	public float ChannelDepth = 1.2f; // starting channel (0: none)
	public float Roughness = 0.12f;   // random bumps (m)
	public float Patchiness = 0.3f;   // random variation of how easily the soil erodes (+-)
	public float VegetationStart = 0.8f;
	public float LooseDepth = 0f;     // loose sediment everywhere at the start (m)
	public bool PeriodicSides = true; // the sides wrap around (a river leaving one side comes in at the other)
	public float SidePush = 1f;       // without periodic sides: fictive slope towards the middle near the walls (%), a force only
	public float SideRise = 2f;       // ... target height of the wall zone at the wall (m): it grows back to this when dug into
	public float SideRecoverDays = 3f; // ... how long the wall zone takes to grow back (landscape days, 0 = never)
	public float SideWidth = 10f;     // ... over this share of the width at each side (%)
	public bool ProtectSides = true;  // ... and that band is hard rock, so the river cannot erode into it
	public bool Sea;                  // a sea at the outlet (fixed level): deltas
	public float SeaFraction = 0.25f; // share of the length that is sea
	public float SeaDepth = 4f;       // depth of the basin below the plain (m)
	public float SeaLevel = -0.8f;    // sea level at the outlet, relative to the plain (m)
	public int Preset;
	public int Seed = 1;

	public static readonly string[] PresetNames = { "Floodplain", "Sand table", "Rocky valley", "Rolling hills", "River delta" };

	public int Nx, Ny;
	public float Dx;
	public float[] Z = Array.Empty<float>(), Loose = Array.Empty<float>(), Erod = Array.Empty<float>(),
		Veg = Array.Empty<float>(), Water = Array.Empty<float>();

	public Landscape Clone() => (Landscape)MemberwiseClone();

	/// <summary>Preset defaults (sizes, fall, soil) – the user may change them before building.</summary>
	public void ApplyPreset(int preset)
	{
		Preset = preset;
		Width = 300; Length = 1200; SlopePercent = 0.3f; InletWidth = 16; InletAngle = 12; ChannelDepth = 1.2f;
		Roughness = 0.12f; VegetationStart = 0.8f; LooseDepth = 0; Sea = false;
		switch (preset)
		{
			case 1:   // the classroom sand table, full size: all loose sand, no plants, steeper
				SlopePercent = 1.0f; VegetationStart = 0; LooseDepth = 3; ChannelDepth = 0.6f; InletWidth = 10;
				Roughness = 0.05f; Width = 200; Length = 800;
				break;
			case 2:   // floodplain with rock outcrops
				break;
			case 4:   // a sea at the outlet: the river builds a delta
				Sea = true; Length = 1600;
				break;
			case 3:   // uneven ground: low hills the river has to find its way through
				Roughness = 0.3f; ChannelDepth = 0.8f;
				break;
		}
		InletY = Width * 0.5f;
	}

	public void Build()
	{
		Dx = Width / CellsAcross;
		Ny = CellsAcross;
		Nx = Math.Max(8, (int)MathF.Round(Length / Dx));
		int n = Nx * Ny;
		Z = new float[n]; Loose = new float[n]; Erod = new float[n]; Veg = new float[n]; Water = new float[n];
		var noise = new FastNoiseLite { Seed = Seed, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 1f / 40f, FractalOctaves = 4 };
		var hills = new FastNoiseLite { Seed = Seed + 7, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 1f / 250f, FractalOctaves = 3 };
		var patches = new FastNoiseLite { Seed = Seed + 21, NoiseType = FastNoiseLite.NoiseTypeEnum.Perlin, Frequency = 1f / 30f, FractalOctaves = 2 };
		var rocks = new FastNoiseLite { Seed = Seed + 13, NoiseType = FastNoiseLite.NoiseTypeEnum.Cellular, Frequency = 1f / 120f };
		// With periodic sides the noise is sampled on a cylinder (y around a circle) so it has no seam.
		float rad = Width / (2 * MathF.PI);
		float N(FastNoiseLite f, float x, float y)
		{
			if (!PeriodicSides) return f.GetNoise2D(x, y);
			float a = y / rad;
			return f.GetNoise3D(x, rad * MathF.Cos(a), rad * MathF.Sin(a));
		}
		float seaStart = Length * (1 - SeaFraction), ramp = Math.Max(Length * 0.04f, 4 * Dx);
		float s = SlopePercent / 100f;
		for (int j = 0; j < Ny; j++)
			for (int i = 0; i < Nx; i++)
			{
				int c = j * Nx + i;
				float x = (i + 0.5f) * Dx, y = (j + 0.5f) * Dx;
				float z = Roughness * N(noise, x, y) * 2f;
				if (Preset == 3) z += 4f * N(hills, x, y) * MathF.Min(1f, MathF.Abs(y - InletY) / (InletWidth * 3f));
				// Starting channel: a shallow straight groove from the inlet down the landscape.
				if (ChannelDepth > 0)
				{
					float r = MathF.Abs(y - InletY) / (InletWidth * 0.5f);
					if (r < 1) z -= ChannelDepth * (1 - r * r * r * r);
				}
				// Walls (sides not periodic): the ground rises towards both sides, so the river turns away instead of
				// running along a wall and sticking to it.
				float side = 0;   // 0 inside, up to 1 at a wall
				if (!PeriodicSides && SideWidth > 0)
				{
					float edge = Math.Min(y, Width - y) / (Width * SideWidth / 100f);
					if (edge < 1) { float k = 1 - edge; side = k * k * (3 - 2 * k); z += SideRise * side; }
				}
				float e = Math.Clamp(1f + Patchiness * 2f * N(patches, x, y), 0.2f, 1.5f);
				// Protected sides: the band towards the walls turns to rock, so the river cannot cut into the side slopes.
				if (ProtectSides && side > 0) e = e + (0.02f - e) * Math.Min(side * 2, 1);
				if (Preset == 2)
				{
					float rk = N(rocks, x, y);
					if (rk > 0.55f && MathF.Abs(y - InletY) > InletWidth) { e = 0.03f; z += 1.5f * (rk - 0.55f) / 0.45f + 0.3f; }
				}
				// Sea: the land slopes down into a basin at the outlet, filled to a flat (true) sea level. In relative
				// heights a flat sea rises by the fall towards upstream: eta = level - S (L - x).
				float w = 0;
				if (Sea)
				{
					float k = Math.Clamp((x - (seaStart - ramp)) / ramp, 0, 1);
					k = k * k * (3 - 2 * k);
					z = z * (1 - k) + (-SeaDepth + 0.3f * Roughness * N(noise, x * 3, y * 3)) * k;
					float eta = SeaLevel - s * (Length - x);
					if (x > seaStart - 2 * ramp) w = Math.Max(eta - z, 0);
				}
				Z[c] = z;
				Erod[c] = e;
				Loose[c] = Sea && x > seaStart - ramp ? Math.Max(LooseDepth, 1f) : ProtectSides && side > 0.25f ? 0 : LooseDepth;
				Veg[c] = z > -0.2f && w <= 0 ? VegetationStart : 0;
				Water[c] = w;
			}
	}
}

/// <summary>Physics knobs (the Settings panel). Units as named; see river.glsl for how each is used.</summary>
public sealed class SimParams
{
	public float Discharge = 20f;        // m3/s
	public float Manning = 0.03f;
	public float GrainMm = 4f;
	public float Helix = 8f;             // helical flow strength (A in tan d = A h kappa)
	public float SlopeEffect = 0.6f;     // downhill pull on bedload
	public float BankErosion = 2f;       // x 1e-5: bank retreat in m/s per m/s of excess near-bank speed
	public float BankSpeed = 0.5f;       // near-bank speed below which banks hold (m/s)
	public float OuterBank = 20f;        // extra bank erosion on the outside of bends
	public float InnerBank = 1f;         // x the helical flow turn: sand carried into the inside of bends (point bars)
	public float ReposeDry = 0.9f;       // tan of the angle of repose, dry
	public float ReposeWet = 0.45f;      // ... under water
	public float Suspended = 0.5f;       // x 1e-5 m/s: pick-up of suspended sediment
	public float SettlingMm = 20f;       // settling speed, mm/s
	public float VegetationDays = 20f;   // time for plants to take over dry ground (morphological days), 0 = none
	public float VegetationStrength = 0.8f;
	public float BedSmoothing = 1f;     // x 1e-5 m2/s: evens out grid-sized bumps on the wet bed
	public float Feed = 0.75f;            // sediment supply at the inlet, x equilibrium
	public float Morfac = 100f;          // morphological acceleration
	public float Eddy = 0.5f;            // turbulent viscosity, m2/s
	public float InletWander = 15f;     // random wandering of the inflow direction, degrees (0: none)
	public float WanderDays = 1f;       // how fast it wanders (morphological days)
	public float Evaporation = 20f;     // mm/h, dries up puddles
	public float Cfl = 0.3f;

	public SimParams Clone() => (SimParams)MemberwiseClone();
}
