using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace RiverInABox;

/// <summary>
/// The scene: environment, camera, the sim and its view, the frame loop, brushes and test flags. UI in Main.Ui.cs.
/// </summary>
public partial class Main : Node3D
{
	public enum Tool { Look, Raise, Lower, Smooth, Rock, Soil, Plants, Water, Dry }

	Landscape _land = new();
	SimParams _par = new();
	RiverSim? _sim;
	LandView _view = null!;
	Camera3D _cam = null!;
	DirectionalLight3D _sun = null!;
	MeshInstance3D _brushRing = null!;

	bool _running = true;
	int _stepsPerFrame = 60;
	Tool _tool = Tool.Look;
	float _brushRadius = 15f, _brushStrength = 0.5f;

	// Orbit camera: target point, yaw/pitch (degrees), distance.
	Vector3 _target;
	float _yaw = -35, _pitch = 35, _dist = 900;

	// Test flags.
	double _selftest = -1, _shotAt = -1, _shotEvery = -1, _nextShot;
	string _shotPath = "";
	int _shotIndex;
	double _realTime, _nextPrint = 1;
	bool _top, _noUi, _showFall = true;
	int _viewMode;
	Vector3? _camArg;   // yaw, pitch, distance as a fraction of the length
	readonly Dictionary<string, string> _sets = new();

	public override void _Ready()
	{
		ParseArgs();
		var env = new Godot.Environment
		{
			BackgroundMode = Godot.Environment.BGMode.Sky,
			Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial
			{
				SkyTopColor = new Color(0.22f, 0.42f, 0.72f), SkyHorizonColor = new Color(0.68f, 0.78f, 0.88f),
				GroundBottomColor = new Color(0.07f, 0.09f, 0.12f), GroundHorizonColor = new Color(0.45f, 0.52f, 0.6f),
			} },
			AmbientLightSource = Godot.Environment.AmbientSource.Sky,
			AmbientLightEnergy = 0.7f,
			TonemapMode = Godot.Environment.ToneMapper.Filmic,
		};
		AddChild(new WorldEnvironment { Environment = env });
		_sun = new DirectionalLight3D { ShadowEnabled = true, LightEnergy = 1.2f, RotationDegrees = new Vector3(-40, -30, 0) };
		AddChild(_sun);
		_cam = new Camera3D { Near = 0.5f, Far = 20000f, Fov = 50 };
		AddChild(_cam);
		_view = new LandView { ViewMode = _viewMode };
		AddChild(_view);
		_brushRing = new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.94f, OuterRadius = 1f, Rings = 64, RingSegments = 4 },
			MaterialOverride = new StandardMaterial3D
			{
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, AlbedoColor = new Color(0.4f, 1f, 0.9f, 0.8f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true,
			},
			Visible = false,
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		};
		AddChild(_brushRing);
		NewSim();
		BuildUi();
	}

	/// <summary>Builds the landscape and a fresh sim (on the render thread, which is this thread).</summary>
	void NewSim()
	{
		_sim?.Free();
		_land.Build();
		_sim = new RiverSim(_land, _par);
		_sim.Initialize();
		_view.Build(_sim);
		ApplyFall();
		_sun.DirectionalShadowMaxDistance = _land.Length * 2f;
		FitCamera();
		GD.Print($"[river] {_land.Nx}x{_land.Ny} cells, dx {_land.Dx:F2} m, {_land.Length}x{_land.Width} m, slope {_land.SlopePercent}%, inlet depth {_sim.InletDepth():F2} m");
	}

	void FitCamera()
	{
		_target = new Vector3(_land.Length * 0.5f, GroundY(_land.Length * 0.5f), _land.Width * 0.5f);
		if (_top) { _yaw = 0; _pitch = 89.9f; _dist = Math.Max(_land.Length * 0.62f, _land.Width * 1.2f) / MathF.Tan(Mathf.DegToRad(25)) * 0.5f; }
		else { _yaw = -30; _pitch = 32; _dist = _land.Length * 0.75f; }
		if (_camArg is Vector3 c) { _yaw = c.X; _pitch = c.Y; _dist = c.Z * _land.Length; }
		UpdateCamera();
	}

	void UpdateCamera()
	{
		_pitch = Math.Clamp(_pitch, -10f, 89.9f);
		_dist = Math.Clamp(_dist, 5f, 15000f);
		var dir = new Vector3(
			MathF.Cos(Mathf.DegToRad(_pitch)) * MathF.Sin(Mathf.DegToRad(_yaw)),
			MathF.Sin(Mathf.DegToRad(_pitch)),
			MathF.Cos(Mathf.DegToRad(_pitch)) * MathF.Cos(Mathf.DegToRad(_yaw)));
		_cam.Position = _target + dir * _dist;
		var up = _pitch > 89f ? new Vector3(-MathF.Sin(Mathf.DegToRad(_yaw)), 0, -MathF.Cos(Mathf.DegToRad(_yaw))) : Vector3.Up;
		_cam.LookAt(_target, up);
		_cam.Far = Math.Max(20000f, _dist * 4f);
	}

	public override void _Process(double delta)
	{
		_realTime += delta;
		if (_sim == null) return;
		ApplyHeldBrush((float)delta);
		_sim.Frame(_running ? _stepsPerFrame : 0);
		if (_running) _sim.RequestThalweg();
		UpdateHud();

		if (_selftest > 0 && _realTime >= _nextPrint)
		{
			_nextPrint += 1;
			PrintStatus();
		}
		if (_shotEvery > 0 && _realTime >= _nextShot)
		{
			_nextShot += _shotEvery;
			Shot($"{_shotPath}_{_shotIndex++:D3}.png");
		}
		if (_shotAt > 0 && _realTime >= _shotAt)
		{
			Shot(_shotPath);
			_shotAt = -1;
			if (_selftest <= 0) GetTree().Quit();
		}
		if (_selftest > 0 && _realTime >= _selftest)
		{
			PrintStatus();
			GetTree().Quit(_sim.NanCount > 0 ? 1 : 0);
		}
	}

	void Shot(string path)
	{
		var img = GetViewport().GetTexture().GetImage();
		string abs = path.StartsWith("res://") || path.StartsWith("user://") ? ProjectSettings.GlobalizePath(path) : path;
		DirAccess.MakeDirRecursiveAbsolute(System.IO.Path.GetDirectoryName(abs) ?? ".");
		img.SavePng(abs);
		GD.Print($"[shot] {abs}");
	}

	public (float sinuosity, float channels, float width, float amplitude) Analyse()
	{
		var t = _sim?.Thalweg;
		if (t == null || t.Length < 16) return (0, 0, 0, 0);
		int nx = t.Length / 4;
		int i0 = nx / 10;   // skip the inlet reach
		var ys = new List<float>();
		float ch = 0, w = 0; int n = 0;
		for (int i = i0; i < nx; i++)
		{
			if (t[i * 4 + 1] < 0.1f) continue;
			float y = t[i * 4];
			// Periodic sides: unwrap the thalweg across the seam.
			if (_land.PeriodicSides && ys.Count > 0)
			{
				float prev = ys[^1];
				y += MathF.Round((prev - y) / _land.Width) * _land.Width;
			}
			ys.Add(y);
			ch += t[i * 4 + 3]; w += t[i * 4 + 2]; n++;
		}
		if (n < 8) return (0, 0, 0, 0);
		// Smooth the thalweg over about one channel width so the zigzag between cells does not count.
		int win = Math.Max(1, (int)(_land.InletWidth / _land.Dx * 0.5f));
		var sm = new float[ys.Count];
		for (int i = 0; i < ys.Count; i++)
		{
			float s = 0; int c = 0;
			for (int k = Math.Max(0, i - win); k <= Math.Min(ys.Count - 1, i + win); k++) { s += ys[k]; c++; }
			sm[i] = s / c;
		}
		float len = 0, mean = 0;
		for (int i = 1; i < sm.Length; i++) len += MathF.Sqrt(_land.Dx * _land.Dx + (sm[i] - sm[i - 1]) * (sm[i] - sm[i - 1]));
		foreach (var y in sm) mean += y;
		mean /= sm.Length;
		float var = 0;
		foreach (var y in sm) var += (y - mean) * (y - mean);
		return (len / ((sm.Length - 1) * _land.Dx), ch / n, w / n, MathF.Sqrt(var / sm.Length));
	}

	void PrintStatus()
	{
		var s = _sim!;
		var a = Analyse();
		GD.Print(string.Format(CultureInfo.InvariantCulture,
			"[t {0:F0}s] morph {1:F1} d, hydro {2:F0} s, dt {3:F3}, steps {4}, maxU {5:F2}, maxH {6:F2}, vol {7:F0} m3, wet {8}, NaN {9}, clamp {10}, sedout {11:F0} m3 | sinuosity {12:F3}, channels {13:F2}, width {14:F1} m, amplitude {15:F1} m, throttle {16:F2}, fall {17}%, Q {18}",
			_realTime, s.MorphTime / 86400, s.HydroTime, s.Dt, s.StepsTotal, s.MaxU, s.MaxH, s.WaterVolume, s.WetCells, s.NanCount, s.Clamped, s.SedimentOut,
			a.sinuosity, a.channels, a.width, a.amplitude, s.Throttle, _land.SlopePercent, _par.Discharge));
	}

	// ------------------------------------------------------------------ input: camera and brushes

	readonly Dictionary<int, Vector2> _touches = new();
	bool _orbiting, _panning, _painting;
	Vector2 _pointer;
	float _pinchDist;

	public override void _UnhandledInput(InputEvent e)
	{
		switch (e)
		{
			case InputEventScreenTouch st:
				if (st.Pressed) _touches[st.Index] = st.Position; else _touches.Remove(st.Index);
				if (_touches.Count >= 2) { _painting = false; _orbiting = false; _pinchDist = PinchDist(); }
				break;
			case InputEventScreenDrag sd when _touches.Count >= 2:
			{
				var old = Centroid();
				_touches[sd.Index] = sd.Position;
				var now = Centroid();
				float d = PinchDist();
				if (_pinchDist > 0 && d > 0) _dist *= _pinchDist / d;
				_pinchDist = d;
				Pan(now - old);
				UpdateCamera();
				break;
			}
			case InputEventScreenDrag sd:
				_touches[sd.Index] = sd.Position;
				break;
			case InputEventMouseButton mb:
				_pointer = mb.Position;
				if (_touches.Count >= 2) break;
				if (mb.ButtonIndex == MouseButton.WheelUp && mb.Pressed) { _dist *= 0.9f; UpdateCamera(); }
				else if (mb.ButtonIndex == MouseButton.WheelDown && mb.Pressed) { _dist /= 0.9f; UpdateCamera(); }
				else if (mb.ButtonIndex == MouseButton.Left)
				{
					if (_tool == Tool.Look) _orbiting = mb.Pressed;
					else _painting = mb.Pressed;
				}
				else if (mb.ButtonIndex is MouseButton.Right or MouseButton.Middle)
				{
					if (mb.ButtonIndex == MouseButton.Right && !mb.ShiftPressed) _orbiting = mb.Pressed;
					else _panning = mb.Pressed;
				}
				break;
			case InputEventMouseMotion mm:
				_pointer = mm.Position;
				if (_touches.Count >= 2) break;
				if (_orbiting) { _yaw -= mm.Relative.X * 0.3f; _pitch += mm.Relative.Y * 0.3f; UpdateCamera(); }
				else if (_panning) { Pan(mm.Relative); UpdateCamera(); }
				break;
			case InputEventMagnifyGesture mg:
				_dist /= mg.Factor; UpdateCamera();
				break;
			case InputEventPanGesture pg:
				_dist *= 1 + pg.Delta.Y * 0.05f; UpdateCamera();
				break;
			case InputEventKey { Pressed: true } k:
				if (k.Keycode == Key.Space) SetRunning(!_running);
				else if (k.Keycode == Key.F) FitCamera();
				else if (k.Keycode == Key.T) { _top = !_top; FitCamera(); }
				break;
		}
	}

	float PinchDist()
	{
		if (_touches.Count < 2) return 0;
		var e = _touches.Values.GetEnumerator();
		e.MoveNext(); var a = e.Current; e.MoveNext(); var b = e.Current;
		return a.DistanceTo(b);
	}

	Vector2 Centroid()
	{
		var s = Vector2.Zero;
		foreach (var p in _touches.Values) s += p;
		return s / Math.Max(_touches.Count, 1);
	}

	void Pan(Vector2 rel)
	{
		float k = _dist * 0.0015f;
		var right = _cam.GlobalBasis.X; right.Y = 0; right = right.Normalized();
		var fwd = new Vector3(-MathF.Sin(Mathf.DegToRad(_yaw)), 0, -MathF.Cos(Mathf.DegToRad(_yaw)));
		_target += (-right * rel.X + fwd * rel.Y) * k;
	}

	/// <summary>The point of the landscape under the pointer (on the flat reference plane), in sim metres.</summary>
	bool PickGround(Vector2 screen, out Vector2 sim)
	{
		sim = default;
		var from = _cam.ProjectRayOrigin(screen);
		var dir = _cam.ProjectRayNormal(screen);
		// The reference plane y = -k x (tilted when the fall is shown).
		float k = _view.Fall * _view.VScale;
		float den = dir.Y + k * dir.X;
		if (MathF.Abs(den) < 1e-5f) return false;
		float t = -(from.Y + k * from.X) / den;
		if (t < 0) return false;
		var p = from + dir * t;
		sim = new Vector2(p.X, p.Z);
		return sim.X >= -_brushRadius && sim.Y >= -_brushRadius && sim.X <= _land.Length + _brushRadius && sim.Y <= _land.Width + _brushRadius;
	}

	void ApplyHeldBrush(float delta)
	{
		bool brushTool = _tool != Tool.Look;
		bool hit = PickGround(_pointer, out var p);
		_brushRing.Visible = brushTool && hit;
		if (hit)
		{
			_brushRing.Position = new Vector3(p.X, GroundY(p.X) + 0.5f, p.Y);
			_brushRing.Scale = new Vector3(_brushRadius, _brushRadius * 0.5f, _brushRadius);
		}
		if (!_painting || !brushTool || !hit || _sim == null) return;
		float s = _brushStrength;
		switch (_tool)
		{
			case Tool.Raise: _sim.Brush(p.X, p.Y, _brushRadius, 2f * s * delta, 0, 0); break;
			case Tool.Lower: _sim.Brush(p.X, p.Y, _brushRadius, 2f * s * delta, 1, 0); break;
			case Tool.Smooth: _sim.Brush(p.X, p.Y, _brushRadius, 6f * s * delta, 2, 0); break;
			case Tool.Rock: _sim.Brush(p.X, p.Y, _brushRadius, 5f * s * delta, 3, 0.03f); break;
			case Tool.Soil: _sim.Brush(p.X, p.Y, _brushRadius, 5f * s * delta, 3, 1f); break;
			case Tool.Plants: _sim.Brush(p.X, p.Y, _brushRadius, 5f * s * delta, 6, 1f); break;
			case Tool.Water: _sim.Brush(p.X, p.Y, _brushRadius, 1f * s * delta, 4, 0); break;
			case Tool.Dry: _sim.Brush(p.X, p.Y, _brushRadius, 2f * s * delta, 5, 0); break;
		}
	}

	/// <summary>Drawn height of the flat reference plane at x (the fall, when it is shown).</summary>
	float GroundY(float x) => -_view.Fall * _view.VScale * x;

	void ApplyFall()
	{
		float old = GroundY(_target.X);
		_view.Fall = _showFall ? _land.SlopePercent / 100f : 0f;
		_target.Y += GroundY(_target.X) - old;   // keep looking at the same spot
		if (_cam != null) UpdateCamera();
	}

	void SetRunning(bool on)
	{
		_running = on;
		if (_playButton != null) _playButton.Text = on ? "PAUSE" : "PLAY";
	}

	// ------------------------------------------------------------------ command line

	void ParseArgs()
	{
		var a = OS.GetCmdlineUserArgs();
		for (int i = 0; i < a.Length; i++)
		{
			string arg = a[i];
			string? Next() => i + 1 < a.Length ? a[++i] : null;
			float F(string? s) => float.Parse(s ?? "0", CultureInfo.InvariantCulture);
			switch (arg)
			{
				case "--selftest": _selftest = F(Next()); break;
				case "--steps": _stepsPerFrame = (int)F(Next()); break;
				case "--shot": _shotPath = Next() ?? "out/shot.png"; _shotAt = F(Next()); break;
				case "--shotevery": _shotPath = Next() ?? "out/seq"; _shotEvery = F(Next()); _nextShot = _shotEvery; break;
				case "--top": _top = true; break;
				case "--noui": _noUi = true; break;
				case "--view": _viewMode = (int)F(Next()); break;
				case "--cam": _camArg = new Vector3(F(Next()), F(Next()), F(Next())); break;
				case "--preset": _land.ApplyPreset((int)F(Next())); break;
				case "--res": _land.CellsAcross = (int)F(Next()); break;
				case "--seed": _land.Seed = (int)F(Next()); break;
				case "--set":
				{
					var kv = (Next() ?? "").Split('=', 2);
					if (kv.Length == 2) _sets[kv[0]] = kv[1];
					break;
				}
			}
		}
		foreach (var (k, v) in _sets)
		{
			bool done = false;
			foreach (object o in new object[] { _par, _land })
			{
				var f = o.GetType().GetField(k);
				if (f == null) continue;
				f.SetValue(o, Convert.ChangeType(v, f.FieldType, CultureInfo.InvariantCulture));
				done = true;
			}
			GD.Print(done ? $"[set] {k} = {v}" : $"[set] unknown field {k}");
		}
		if (!_sets.ContainsKey("InletY")) _land.InletY = _land.Width * 0.5f;
	}
}
