using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Godot;

namespace RiverInABox;

/// <summary>
/// The river model on Godot's main RenderingDevice (shaders/river.glsl). Water and bed stay on the GPU; two
/// textures (tex0 = z, h, u, v; tex1 = erodibility, vegetation, loose layer, suspended conc.) are written for the view
/// and a few stats plus the thalweg come back asynchronously.
/// Cell state and face velocities ping-pong between two buffers: uniform set s reads buffer s and writes 1 - s.
/// </summary>
public sealed class RiverSim
{
	const uint Wg = 128;
	const string ShaderPath = "res://shaders/river.glsl";
	const int ParamCount = 64;

	public readonly Landscape Land;
	public readonly SimParams Par;
	public int Nx => Land.Nx;
	public int Ny => Land.Ny;
	public float Dx => Land.Dx;
	public bool Ready { get; private set; }

	public Rid Tex0 => _tex0;
	public Rid Tex1 => _tex1;

	// Stats (read back asynchronously).
	public double HydroTime, MorphTime;
	public long StepsTotal;
	public float Dt = 0.05f;
	public float WaveSpeed = 3f, MaxU, MaxH, WaterVolume;
	public uint NanCount, WetCells, Clamped;
	public double SedimentOut;   // m3 since start
	public float Throttle = 1f;   // automatic slow-down of the landscape speed (1 = as set)
	int _frameSteps = 1;
	public float[] Thalweg = Array.Empty<float>();   // per column: y, depth, wet width, area
	public double ThalwegTime;

	sealed class Kernel { public Rid Shader, Pipeline; public readonly Rid[] Sets = new Rid[2]; }

	RenderingDevice _rd = null!;
	Rid _cellA, _cellB, _uA, _uB, _vA, _vB, _bed, _qs, _sfx, _sfy, _params, _stats, _tex0, _tex1, _thal;
	readonly Dictionary<string, Kernel> _k = new();
	readonly byte[] _push = new byte[32];
	readonly float[] _p = new float[ParamCount];
	int _last = 1;   // the set used by the last step: its OUT buffers hold the current state (set 1 writes A)
	bool _statsInFlight, _thalInFlight;
	readonly Queue<float[]> _brushes = new();

	public RiverSim(Landscape land, SimParams par)
	{
		Land = land;
		Par = par;
	}

	int Cells => Nx * Ny;
	int NU => (Nx + 1) * Ny;
	int NV => Nx * (Ny + 1);

	public void Initialize()
	{
		_rd = RenderingServer.GetRenderingDevice();
		if (_rd == null)
		{
			GD.PushError("RiverSim: no RenderingDevice. Use the Forward+ or Mobile renderer (Vulkan).");
			return;
		}
		var cells = new float[Cells * 4];
		var bed = new float[Cells * 4];
		for (int c = 0; c < Cells; c++)
		{
			cells[c * 4] = Land.Water[c];
			bed[c * 4] = Land.Z[c];
			bed[c * 4 + 1] = Land.Loose[c];
			bed[c * 4 + 2] = Land.Erod[c];
			bed[c * 4 + 3] = Land.Veg[c];
		}
		_cellA = Buffer(Cells * 16, Bytes(cells));
		_cellB = Buffer(Cells * 16, Bytes(cells));
		_uA = Buffer(NU * 4); _uB = Buffer(NU * 4);
		_vA = Buffer(NV * 4); _vB = Buffer(NV * 4);
		_bed = Buffer(Cells * 16, Bytes(bed));
		_qs = Buffer(Cells * 16);
		_sfx = Buffer(NU * 4);
		_sfy = Buffer(NV * 4);
		_params = Buffer(ParamCount * 4);
		_stats = Buffer(64);
		_thal = Buffer(Nx * 16);
		_tex0 = Texture(Nx, Ny);
		_tex1 = Texture(Nx, Ny);
		foreach (var v in new[] { "momentum", "continuity", "sedcell", "sedface", "bed", "export", "brush", "thalweg" })
			_k[v] = CreateKernel(v);
		UploadParams();
		long cl = _rd.ComputeListBegin();
		Dispatch(cl, "export", (uint)Cells, _last);
		_rd.ComputeListEnd();
		Ready = true;
	}

	static byte[] Bytes(float[] a) => MemoryMarshal.AsBytes(a.AsSpan()).ToArray();

	Rid Buffer(int bytes, byte[]? data = null) => _rd.StorageBufferCreate((uint)bytes, data ?? new byte[bytes]);

	Rid Texture(int w, int h)
	{
		var fmt = new RDTextureFormat
		{
			Width = (uint)w,
			Height = (uint)h,
			Format = RenderingDevice.DataFormat.R32G32B32A32Sfloat,
			UsageBits = RenderingDevice.TextureUsageBits.SamplingBit
				| RenderingDevice.TextureUsageBits.StorageBit
				| RenderingDevice.TextureUsageBits.CanUpdateBit
				| RenderingDevice.TextureUsageBits.CanCopyFromBit,
		};
		return _rd.TextureCreate(fmt, new RDTextureView(), new Godot.Collections.Array<byte[]>());
	}

	Kernel CreateKernel(string version)
	{
		var spirv = SpirvCache.Get(ShaderPath, version);
		if (!string.IsNullOrEmpty(spirv.CompileErrorCompute))
			GD.PushError($"RiverSim: {version} failed to compile:\n{spirv.CompileErrorCompute}");
		var k = new Kernel { Shader = _rd.ShaderCreateFromSpirV(spirv, $"river_{version}") };
		k.Pipeline = _rd.ComputePipelineCreate(k.Shader);
		for (int s = 0; s < 2; s++) k.Sets[s] = CreateSet(k.Shader, s);
		return k;
	}

	Rid CreateSet(Rid shader, int s)
	{
		Rid cin = s == 0 ? _cellA : _cellB, cout = s == 0 ? _cellB : _cellA;
		Rid uin = s == 0 ? _uA : _uB, uout = s == 0 ? _uB : _uA;
		Rid vin = s == 0 ? _vA : _vB, vout = s == 0 ? _vB : _vA;
		var u = new Godot.Collections.Array<RDUniform>
		{
			Uniform(RenderingDevice.UniformType.StorageBuffer, 0, cin),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 1, cout),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 2, uin),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 3, vin),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 4, uout),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 5, vout),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 6, _bed),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 7, _qs),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 8, _sfx),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 9, _sfy),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 10, _params),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 11, _stats),
			Uniform(RenderingDevice.UniformType.Image, 12, _tex0),
			Uniform(RenderingDevice.UniformType.Image, 13, _tex1),
			Uniform(RenderingDevice.UniformType.StorageBuffer, 14, _thal),
		};
		// Bindings a kernel does not use are ignored by uniform_set_create.
		return _rd.UniformSetCreate(u, shader, 0);
	}

	static RDUniform Uniform(RenderingDevice.UniformType type, int binding, Rid id)
	{
		var u = new RDUniform { UniformType = type, Binding = binding };
		u.AddId(id);
		return u;
	}

	void Dispatch(long cl, string kernel, uint threads, int set, float dt = 0, float mdt = 0, uint k = 0)
	{
		var kk = _k[kernel];
		var s = _push.AsSpan();
		BitConverter.TryWriteBytes(s[0..], (uint)Nx);
		BitConverter.TryWriteBytes(s[4..], (uint)Ny);
		BitConverter.TryWriteBytes(s[8..], k);
		BitConverter.TryWriteBytes(s[16..], Dx);
		BitConverter.TryWriteBytes(s[20..], dt);
		BitConverter.TryWriteBytes(s[24..], mdt);
		BitConverter.TryWriteBytes(s[28..], (float)HydroTime);
		_rd.ComputeListBindComputePipeline(cl, kk.Pipeline);
		_rd.ComputeListBindUniformSet(cl, kk.Sets[set], 0);
		_rd.ComputeListSetPushConstant(cl, _push, (uint)_push.Length);
		_rd.ComputeListDispatch(cl, Math.Max((threads + Wg - 1) / Wg, 1), 1, 1);
		_rd.ComputeListAddBarrier(cl);
	}

	/// <summary>Physics parameters to the GPU (indices as the P[] defines in river.glsl).</summary>
	public void UploadParams()
	{
		var p = _p;
		Array.Clear(p);
		p[0] = 9.81f;
		p[1] = Land.SlopePercent / 100f;
		p[2] = Par.Manning;
		p[3] = 0.005f;                 // wet face threshold (m)
		p[4] = Par.Discharge;
		p[5] = Math.Clamp(Land.InletY + WanderY * Land.InletWidth, Land.InletWidth * 0.5f, Land.Width - Land.InletWidth * 0.5f);
		p[6] = Land.InletWidth;
		p[7] = Mathf.DegToRad(Land.InletAngle + WanderAngle * Par.InletWander);
		p[8] = Par.GrainMm / 1000f;
		p[9] = 0.047f;
		p[10] = 8f;
		p[11] = Par.Helix;
		p[12] = Par.SlopeEffect;
		p[13] = Par.BankErosion * 1e-5f;
		p[14] = Par.OuterBank;
		p[15] = Land.InletWidth * 2f;  // curvature scale: a bend of radius ~2 widths counts fully
		p[16] = Par.ReposeDry;
		p[17] = Par.ReposeWet;
		p[18] = 0.2f;
		p[19] = 0.4f;
		p[20] = Par.Suspended * 1e-5f;
		p[21] = Par.SettlingMm * 1e-3f;
		p[22] = Par.VegetationDays > 0 ? Par.VegetationDays * 86400f : 0f;
		p[23] = Par.VegetationStrength;
		p[24] = 0.04f * Par.VegetationStrength;
		p[25] = Par.Feed;
		p[26] = 0.05f;
		p[27] = 1.65f;
		// Inlet speed from the discharge and an estimated inlet depth (normal flow in a channel of the inlet width).
		float h = InletDepth();
		p[28] = Par.Discharge / Math.Max(Land.InletWidth * h, 0.01f);
		p[29] = Par.BankSpeed;
		p[30] = 0.02f;                 // dry for sediment (m)
		p[31] = 15f;
		p[32] = Par.Eddy;
		p[33] = Par.Evaporation / 3.6e6f;   // mm/h -> m/s
		p[34] = Par.InnerBank;
		p[35] = Land.PeriodicSides ? 1 : 0;
		p[36] = Land.Sea ? 1 : 0;
		p[37] = Land.SeaLevel;
		p[38] = Par.BedSmoothing * 1e-5f;
		p[39] = Land.SidePush / 100f;
		p[46] = Land.Width * Land.SideWidth / 100f;
		p[47] = Land.SideRise;
		p[48] = Land.SideRecoverDays * 86400f;
		p[40] = _brush[0]; p[41] = _brush[1]; p[42] = _brush[2]; p[43] = _brush[3]; p[44] = _brush[4]; p[45] = _brush[5];
		if (_rd != null && _params.IsValid) _rd.BufferUpdate(_params, 0, ParamCount * 4, Bytes(p));
	}

	/// <summary>Normal depth of the inflow in a rectangular channel of the inlet width (Manning, wide channel).</summary>
	public float InletDepth()
	{
		float s = Math.Max(Land.SlopePercent / 100f, 1e-5f);
		float q = Par.Discharge / Math.Max(Land.InletWidth, 0.1f);
		return MathF.Pow(q * Par.Manning / MathF.Sqrt(s), 0.6f);
	}

	readonly float[] _brush = new float[6];

	// Randomness: the inflow wanders slowly like the hose at a sand table (Ornstein-Uhlenbeck, unit variance),
	// angle times InletWander degrees, position times a quarter of the inlet width.
	public float WanderAngle, WanderY;
	readonly Random _rng = new(12345);

	void Wander(float morphSeconds)
	{
		if (Par.InletWander <= 0 || morphSeconds <= 0) return;
		// Let the river settle into its starting channel first (water reaches the outlet in ~1 h of water time).
		if (HydroTime < 3600) return;
		float a = Math.Min(morphSeconds / Math.Max(Par.WanderDays * 86400f, 1f), 1f);
		float s = MathF.Sqrt(2 * a);
		WanderAngle += -WanderAngle * a + s * Gauss();
		WanderY += -WanderY * a + 0.25f * s * Gauss();
		UploadParams();
	}

	float Gauss()
	{
		double u1 = 1 - _rng.NextDouble(), u2 = _rng.NextDouble();
		return (float)(Math.Sqrt(-2 * Math.Log(u1)) * Math.Cos(2 * Math.PI * u2));
	}

	/// <summary>Queues a brush stamp: centre (m), radius (m), amount, mode (0 raise, 1 lower, 2 smooth, 3 material,
	/// 4 add water, 5 remove water, 6 vegetation), value (material erodibility / vegetation).</summary>
	public void Brush(float x, float y, float r, float amount, int mode, float value)
	{
		lock (_brushes) _brushes.Enqueue(new[] { x, y, r, amount, mode, value });
	}

	/// <summary>Runs <paramref name="steps"/> hydraulic steps. Morphology runs with every step (mdt = morfac dt).</summary>
	public void Frame(int steps)
	{
		if (!Ready) return;
		lock (_brushes)
		{
			while (_brushes.Count > 0)
			{
				var b = _brushes.Dequeue();
				Array.Copy(b, _brush, 6);
				UploadParams();
				long bl = _rd.ComputeListBegin();
				Dispatch(bl, "brush", (uint)Cells, _last);
				Dispatch(bl, "export", (uint)Cells, _last);
				_rd.ComputeListEnd();
			}
		}
		if (steps <= 0) return;
		// Time step from the fastest wave of the last read-back (CFL 0.3 for the explicit upwind fluxes).
		float c = Math.Max(WaveSpeed, Math.Max(MathF.Sqrt(9.81f * InletDepth()) + 1f, 1f));
		Dt = Math.Clamp(Par.Cfl * Dx / c, 1e-3f, 2f);
		float mdt = Dt * Par.Morfac * Throttle;
		_frameSteps = steps;
		Wander(steps * mdt);
		_rd.BufferClear(_stats, 0, 64);   // a pending read-back already holds its copy
		long cl = _rd.ComputeListBegin();
		for (int s = 0; s < steps; s++)
		{
			int set = 1 - _last;
			Dispatch(cl, "momentum", (uint)Math.Max(NU, NV), set, Dt, mdt);
			Dispatch(cl, "continuity", (uint)Cells, set, Dt, mdt, s == steps - 1 ? 1u : 0u);   // k = 1: count the stats
			if (Par.Morfac > 0)
			{
				Dispatch(cl, "sedcell", (uint)Cells, set, Dt, mdt);
				Dispatch(cl, "sedface", (uint)Math.Max(NU, NV), set, Dt, mdt);
				Dispatch(cl, "bed", (uint)Cells, set, Dt, mdt);
			}
			_last = set;
			HydroTime += Dt;
			MorphTime += mdt;
		}
		Dispatch(cl, "export", (uint)Cells, _last);
		Dispatch(cl, "thalweg", (uint)Nx, _last);
		_rd.ComputeListEnd();
		StepsTotal += steps;
		if (!_statsInFlight)
		{
			_statsInFlight = true;
			var err = _rd.BufferGetDataAsync(_stats, Callable.From<byte[]>(OnStats), 0, 64);
			if (err != Error.Ok) OnStats(_rd.BufferGetData(_stats, 0, 64));
		}
	}

	/// <summary>Asks for the thalweg (async); <see cref="Thalweg"/> is replaced when it arrives.</summary>
	public void RequestThalweg()
	{
		if (!Ready || _thalInFlight) return;
		_thalInFlight = true;
		double t = MorphTime;
		var err = _rd.BufferGetDataAsync(_thal, Callable.From<byte[]>(d => OnThalweg(d, t)), 0, (uint)(Nx * 16));
		if (err != Error.Ok) OnThalweg(_rd.BufferGetData(_thal, 0, (uint)(Nx * 16)), t);
	}

	void OnThalweg(byte[] d, double t)
	{
		_thalInFlight = false;
		var a = new float[Nx * 4];
		System.Buffer.BlockCopy(d, 0, a, 0, Math.Min(d.Length, a.Length * 4));
		Thalweg = a;
		ThalwegTime = t;
	}

	void OnStats(byte[] d)
	{
		_statsInFlight = false;
		if (d.Length < 32) return;
		float w = BitConverter.ToSingle(d, 0);
		WaveSpeed = float.IsFinite(w) && w > 0 ? w : 1f;
		NanCount += BitConverter.ToUInt32(d, 4);
		WetCells = BitConverter.ToUInt32(d, 8);
		WaterVolume = BitConverter.ToUInt32(d, 12) / 16f;
		MaxU = BitConverter.ToSingle(d, 16);
		MaxH = BitConverter.ToSingle(d, 20);
		uint clamped = BitConverter.ToUInt32(d, 24);
		Clamped += clamped;
		// Bed changing too fast for the explicit bed update: slow the landscape down until it is not, then recover.
		if (clamped > Math.Max(1, Cells * _frameSteps / 20000)) Throttle = Math.Max(Throttle * 0.7f, 0.01f);
		else Throttle = Math.Min(Throttle * 1.01f, 1f);
		SedimentOut += BitConverter.ToUInt32(d, 28) / 1e4 * Par.Morfac;
	}

	/// <summary>Synchronous read of the bed and water (tests, saving).</summary>
	public (float[] bed, float[] cell) ReadState()
	{
		var b = _rd.BufferGetData(_bed, 0, (uint)(Cells * 16));
		var c = _rd.BufferGetData(_last == 0 ? _cellB : _cellA, 0, (uint)(Cells * 16));
		var bf = new float[Cells * 4];
		var cf = new float[Cells * 4];
		System.Buffer.BlockCopy(b, 0, bf, 0, b.Length);
		System.Buffer.BlockCopy(c, 0, cf, 0, c.Length);
		return (bf, cf);
	}

	public void Free()
	{
		if (_rd == null) return;
		Ready = false;
		foreach (var k in _k.Values) _rd.FreeRid(k.Shader);
		_k.Clear();
		foreach (var r in new[] { _cellA, _cellB, _uA, _uB, _vA, _vB, _bed, _qs, _sfx, _sfy, _params, _stats, _tex0, _tex1, _thal })
			if (r.IsValid) _rd.FreeRid(r);
	}
}
