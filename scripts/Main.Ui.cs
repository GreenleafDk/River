using System;
using System.Globalization;
using Godot;

namespace RiverInABox;

/// <summary>UI: top bar, tool column with the brush panel, settings drawer, status chip. Touch first.</summary>
public partial class Main
{
	Control _ui = null!;
	Button? _playButton;
	Label _status = null!;
	PanelContainer _drawer = null!, _brushPanel = null!, _chip = null!;
	Label _fallLabel = null!, _flowLabel = null!;

	void PlaceChip()
	{
		if (_chip == null) return;
		float right = _drawer != null && _drawer.Visible ? -380 : -10;
		_chip.OffsetRight = right; _chip.OffsetLeft = right;
		_chip.OffsetBottom = -10; _chip.OffsetTop = -10;
	}
	VBoxContainer _drawerBox = null!;
	const float Touch = 44;

	void BuildUi()
	{
		var layer = new CanvasLayer();
		AddChild(layer);
		_ui = new Control { Theme = BoxTheme.Get(), MouseFilter = Control.MouseFilterEnum.Ignore };
		_ui.SetAnchorsPreset(Control.LayoutPreset.FullRect);
		layer.AddChild(_ui);
		layer.Visible = !_noUi;

		// Top bar.
		var top = new PanelContainer();
		top.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		// Spans the window and wraps onto more rows when narrow (a fixed row pushed SETTINGS off a smaller window).
		top.SetAnchorsPreset(Control.LayoutPreset.TopWide);
		top.OffsetLeft = 10; top.OffsetRight = -10; top.OffsetTop = 10;
		_ui.AddChild(top);
		var bar = new HFlowContainer();
		top.AddChild(bar);
		bar.AddChild(new Label { Text = "RIVER IN A BOX", ThemeTypeVariation = "HeaderLabel", VerticalAlignment = VerticalAlignment.Center });
		_playButton = Btn(_running ? "PAUSE" : "PLAY", () => SetRunning(!_running));
		_playButton.CustomMinimumSize = new Vector2(90, Touch);
		Tip(_playButton, "PLAY");
		bar.AddChild(_playButton);
		bar.AddChild(Tip(Btn("RESTART", NewSim), "RESTART"));
		var settings = new Button { Text = "SETTINGS", ToggleMode = true, CustomMinimumSize = new Vector2(0, Touch) };
		settings.Toggled += on => { _drawer.Visible = on; PlaceChip(); };
		bar.AddChild(Tip(settings, "SETTINGS"));
		bar.AddChild(Tip(Small("Speed"), "Speed"));
		bar.AddChild(Tip(Slider(0, 400, _stepsPerFrame, 1, v => _stepsPerFrame = (int)v, 140), "Speed"));
		var view = new OptionButton { CustomMinimumSize = new Vector2(150, Touch) };
		foreach (var n in new[] { "Natural", "Flow speed", "Water depth", "Soil and plants" }) view.AddItem(n);
		view.Selected = _view.ViewMode;
		view.ItemSelected += i => _view.ViewMode = (int)i;
		Tip(view, "View");
		bar.AddChild(view);
		// The general fall: only a driving force (the land is drawn flat), so it sits up here where its effect is easy to try.
		_fallLabel = Small(FallText());
		_fallLabel.CustomMinimumSize = new Vector2(96, 0);
		bar.AddChild(Tip(_fallLabel, "Fall"));
		var fall = Slider(0, 1, Math.Log(_land.SlopePercent / FallMin) / Math.Log(FallMax / FallMin), 0.001f, t =>
		{
			_land.SlopePercent = (float)(FallMin * Math.Pow(FallMax / FallMin, t));
			ApplyFall();
			_fallLabel.Text = FallText();
			_sim?.UploadParams();
		}, 130);
		bar.AddChild(Tip(fall, "Fall"));
		_flowLabel = Small(FlowText());
		_flowLabel.CustomMinimumSize = new Vector2(110, 0);
		bar.AddChild(Tip(_flowLabel, "Water"));
		bar.AddChild(Tip(Slider(0, 1, Math.Log(_par.Discharge / FlowMin) / Math.Log(FlowMax / FlowMin), 0.001f, t =>
		{
			_par.Discharge = (float)(FlowMin * Math.Pow(FlowMax / FlowMin, t));
			_flowLabel.Text = FlowText();
			_sim?.UploadParams();
		}, 130), "Water"));
		bar.AddChild(Tip(Small("Height x"), "Height x"));
		bar.AddChild(Tip(Slider(1, 20, _view.VScale, 0.5f, v =>
		{
			float old = GroundY(_target.X);
			_view.VScale = v;
			_target.Y += GroundY(_target.X) - old;   // the tilt scales with the heights: keep the camera on the land
			UpdateCamera();
		}, 110), "Height x"));

		// Tool column.
		var tools = new PanelContainer();

		tools.Position = new Vector2(10, 90);
		_ui.AddChild(tools);
		var col = new VBoxContainer();
		tools.AddChild(col);
		var group = new ButtonGroup();
		foreach (Tool t in Enum.GetValues(typeof(Tool)))
		{
			var b = new Button
			{
				Text = ToolName(t), ToggleMode = true, ButtonGroup = group, ThemeTypeVariation = "ToolButton",
				CustomMinimumSize = new Vector2(110, Touch), ButtonPressed = t == _tool, Alignment = HorizontalAlignment.Left,
			};
			var tt = t;
			b.Pressed += () => { _tool = tt; _brushPanel.Visible = tt != Tool.Look; };
			col.AddChild(Tip(b, ToolName(t)));
		}

		_brushPanel = new PanelContainer { Visible = false, Position = new Vector2(140, 90) };
		_ui.AddChild(_brushPanel);
		var bp = new VBoxContainer { CustomMinimumSize = new Vector2(220, 0) };
		_brushPanel.AddChild(bp);
		bp.AddChild(new Label { Text = "BRUSH", ThemeTypeVariation = "HeaderLabel" });
		AddSlider(bp, "Radius", "m", 2, 200, _brushRadius, v => _brushRadius = v, log: true);
		AddSlider(bp, "Strength", "", 0.05f, 20, _brushStrength, v => _brushStrength = v, log: true);

		// Settings drawer.
		_drawer = new PanelContainer { Visible = false };
		_drawer.SetAnchorsPreset(Control.LayoutPreset.RightWide);
		_drawer.OffsetLeft = -360; _drawer.OffsetRight = -10; _drawer.OffsetTop = 10; _drawer.OffsetBottom = -10;
		// Everything below the top bar moves down when the bar wraps onto more rows.
		top.Resized += () =>
		{
			float y = top.Size.Y + 20;
			tools.Position = new Vector2(10, y);
			_brushPanel.Position = new Vector2(140, y);
			_drawer.OffsetTop = y;
		};
		_ui.AddChild(_drawer);
		var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
		_drawer.AddChild(scroll);
		_drawerBox = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(_drawerBox);
		FillDrawer();

		// Status chip: bottom right (left of the settings drawer when it is open), clear of the tool column.
		_chip = new PanelContainer { ThemeTypeVariation = "Chip" };
		_chip.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
		_chip.GrowHorizontal = Control.GrowDirection.Begin;
		_chip.GrowVertical = Control.GrowDirection.Begin;
		PlaceChip();
		_ui.AddChild(_chip);
		_status = new Label();
		_chip.AddChild(_status);
	}

	const double FallMin = 0.02, FallMax = 15, FlowMin = 0.5, FlowMax = 2000;
	string FlowText() => $"Water {Fmt(_par.Discharge)} m³/s";
	string FallText() => $"Fall {Fmt(_land.SlopePercent)} %";

	static string ToolName(Tool t) => t switch
	{
		Tool.Look => "Look", Tool.Raise => "Raise", Tool.Lower => "Dig", Tool.Smooth => "Smooth", Tool.Rock => "Rock",
		Tool.Soil => "Soft soil", Tool.Plants => "Plants", Tool.Water => "Add water", Tool.Dry => "Remove water", _ => t.ToString(),
	};

	void FillDrawer()
	{
		foreach (var c in _drawerBox.GetChildren()) c.QueueFree();
		var b = _drawerBox;
		b.AddChild(new Label { Text = "LANDSCAPE", ThemeTypeVariation = "HeaderLabel" });
		var preset = new OptionButton { CustomMinimumSize = new Vector2(0, Touch) };
		foreach (var n in Landscape.PresetNames) preset.AddItem(n);
		preset.Selected = _land.Preset;
		preset.ItemSelected += i => { _land.ApplyPreset((int)i); FillDrawer(); NewSim(); };
		b.AddChild(Tip(preset, "Preset"));
		AddSlider(b, "Width", "m", 20, 2000, _land.Width, v => _land.Width = v, log: true);
		AddSlider(b, "Length", "m", 50, 8000, _land.Length, v => _land.Length = v, log: true);
		AddSlider(b, "Cells across", "", 32, 512, _land.CellsAcross, v => _land.CellsAcross = (int)v, step: 1);
		AddSlider(b, "Inlet width", "m", 1, 200, _land.InletWidth, v => { _land.InletWidth = v; _sim?.UploadParams(); }, log: true);
		AddSlider(b, "Inlet angle", "°", -45, 45, _land.InletAngle, v => { _land.InletAngle = v; _sim?.UploadParams(); });
		AddSlider(b, "Inlet position", "%", 5, 95, _land.InletY / _land.Width * 100, v => { _land.InletY = v / 100 * _land.Width; _sim?.UploadParams(); });
		AddSlider(b, "Starting channel", "m", 0, 5, _land.ChannelDepth, v => _land.ChannelDepth = v);
		AddSlider(b, "Bumps", "m", 0, 3, _land.Roughness, v => _land.Roughness = v);
		AddSlider(b, "Soil patchiness", "", 0, 1, _land.Patchiness, v => _land.Patchiness = v);
		AddSlider(b, "Plants at start", "", 0, 1, _land.VegetationStart, v => _land.VegetationStart = v);
		AddSlider(b, "Loose sand", "m", 0, 5, _land.LooseDepth, v => _land.LooseDepth = v);
		AddCheck(b, "Periodic sides", _land.PeriodicSides, on => { _land.PeriodicSides = on; _sim?.UploadParams(); });
		AddSlider(b, "Push from the walls", "%", 0, 10, _land.SidePush, v => { _land.SidePush = v; _sim?.UploadParams(); });
		AddSlider(b, "Wall zone width", "%", 0, 30, _land.SideWidth, v => { _land.SideWidth = v; _sim?.UploadParams(); });
		AddSlider(b, "Raised ground at walls", "m", 0, 10, _land.SideRise, v => { _land.SideRise = v; _sim?.UploadParams(); });
		AddSlider(b, "Wall zone grows back in", "days", 0, 30, _land.SideRecoverDays, v => { _land.SideRecoverDays = v; _sim?.UploadParams(); });
		AddCheck(b, "Hard ground at walls", _land.ProtectSides, on => _land.ProtectSides = on);
		AddCheck(b, "Sea at the outlet (new landscape)", _land.Sea, on => { _land.Sea = on; NewSim(); });
		AddSlider(b, "Sea level", "m", -3, 1, _land.SeaLevel, v => { _land.SeaLevel = v; _sim?.UploadParams(); });
		AddSlider(b, "Sea share of length", "%", 5, 60, _land.SeaFraction * 100, v => _land.SeaFraction = v / 100);
		var make = new Button { Text = "NEW LANDSCAPE", ThemeTypeVariation = "PrimaryButton", CustomMinimumSize = new Vector2(0, Touch) };
		make.Pressed += NewSim;
		b.AddChild(Tip(make, "NEW LANDSCAPE"));
		b.AddChild(new HSeparator());

		b.AddChild(new Label { Text = "VIEW", ThemeTypeVariation = "HeaderLabel" });
		AddSlider(b, "Smooth the view", "", 0, 1, _view.Smooth, v => _view.Smooth = v);
		AddCheck(b, "Show the fall", _showFall, on => { _showFall = on; ApplyFall(); });
		b.AddChild(new HSeparator());

		b.AddChild(new Label { Text = "RIVER", ThemeTypeVariation = "HeaderLabel" });
		AddSlider(b, "Landscape speed", "x", 0, 1000, _par.Morfac, v => _par.Morfac = v, log: true);
		AddSlider(b, "Grain size", "mm", 0.1f, 50, _par.GrainMm, v => _par.GrainMm = v, log: true);
		AddSlider(b, "Bank erosion", "", 0, 20, _par.BankErosion, v => _par.BankErosion = v);
		AddSlider(b, "Outer bend erosion", "", 0, 50, _par.OuterBank, v => _par.OuterBank = v);
		AddSlider(b, "Inner bend deposition", "x", 0, 4, _par.InnerBank, v => _par.InnerBank = v);
		AddSlider(b, "Bank holds below", "m/s", 0, 3, _par.BankSpeed, v => _par.BankSpeed = v);
		AddSlider(b, "Downhill pull", "", 0, 3, _par.SlopeEffect, v => _par.SlopeEffect = v);
		AddSlider(b, "Suspended load", "", 0, 10, _par.Suspended, v => _par.Suspended = v);
		AddSlider(b, "Settling speed", "mm/s", 0.1f, 200, _par.SettlingMm, v => _par.SettlingMm = v, log: true);
		AddSlider(b, "Plants grow in", "days", 0, 365, _par.VegetationDays, v => _par.VegetationDays = v);
		AddSlider(b, "Plant strength", "", 0, 1, _par.VegetationStrength, v => _par.VegetationStrength = v);
		AddSlider(b, "Sediment feed", "x", 0, 3, _par.Feed, v => _par.Feed = v);
		AddSlider(b, "Inlet wandering", "°", 0, 45, _par.InletWander, v => _par.InletWander = v);
		AddSlider(b, "Wandering pace", "days", 0.1f, 30, _par.WanderDays, v => _par.WanderDays = v, log: true);
		AddSlider(b, "Evaporation", "mm/h", 0, 100, _par.Evaporation, v => _par.Evaporation = v);
		AddSlider(b, "Roughness (Manning)", "", 0.01f, 0.1f, _par.Manning, v => _par.Manning = v);
		AddSlider(b, "Bed smoothing", "", 0, 10, _par.BedSmoothing, v => _par.BedSmoothing = v);
	}

	Button Btn(string text, Action a)
	{
		var b = new Button { Text = text, CustomMinimumSize = new Vector2(0, Touch) };
		b.Pressed += a;
		return b;
	}

	static Label Small(string t) => new() { Text = t, VerticalAlignment = VerticalAlignment.Center };

	/// <summary>Gives a control its tooltip (Tooltips.cs); labels get mouse events so theirs shows too.</summary>
	static T Tip<T>(T c, string key) where T : Control
	{
		c.TooltipText = Tooltips.Get(key);
		if (c is Label) c.MouseFilter = Control.MouseFilterEnum.Pass;
		return c;
	}

	static void AddCheck(VBoxContainer parent, string name, bool value, Action<bool> set)
	{
		var c = new CheckButton { Text = name, ButtonPressed = value, CustomMinimumSize = new Vector2(0, Touch), TooltipText = Tooltips.Get(name) };
		c.Toggled += on => set(on);
		parent.AddChild(c);
	}

	static HSlider Slider(float min, float max, double value, float step, Action<float> set, float width)
	{
		var s = new HSlider { MinValue = min, MaxValue = max, Step = step, Value = value, CustomMinimumSize = new Vector2(width, Touch), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter };
		s.ValueChanged += v => set((float)v);
		return s;
	}

	/// <summary>A labelled slider row; physics changes go to the GPU at once.</summary>
	void AddSlider(VBoxContainer parent, string name, string unit, float min, float max, float value, Action<float> set, bool log = false, float step = 0)
	{
		var label = Tip(new Label(), name);
		parent.AddChild(label);
		var s = Tip(new HSlider { CustomMinimumSize = new Vector2(0, 32) }, name);
		void Show(float v) => label.Text = $"{name}   {Fmt(v)} {unit}";
		if (log && min > 0)
		{
			s.MinValue = 0; s.MaxValue = 1; s.Step = 0.001;
			s.Value = Math.Log(Math.Clamp(value, min, max) / min) / Math.Log(max / min);
			s.ValueChanged += t => { float v = (float)(min * Math.Pow(max / min, t)); set(v); Show(v); _sim?.UploadParams(); };
		}
		else if (log)
		{
			// Log-ish with a zero at the left end.
			s.MinValue = 0; s.MaxValue = 1; s.Step = 0.001;
			s.Value = value <= 0 ? 0 : Math.Sqrt(value / max);
			s.ValueChanged += t => { float v = (float)(t * t * max); set(v); Show(v); _sim?.UploadParams(); };
		}
		else
		{
			s.MinValue = min; s.MaxValue = max; s.Step = step > 0 ? step : (max - min) / 1000;
			s.Value = value;
			s.ValueChanged += v => { set((float)v); Show((float)v); _sim?.UploadParams(); };
		}
		Show(value);
		parent.AddChild(s);
	}

	static string Fmt(float v) => Math.Abs(v) >= 100 ? v.ToString("F0", CultureInfo.InvariantCulture)
		: Math.Abs(v) >= 10 ? v.ToString("F1", CultureInfo.InvariantCulture)
		: Math.Abs(v) >= 1 ? v.ToString("F2", CultureInfo.InvariantCulture) : v.ToString("G2", CultureInfo.InvariantCulture);

	double _hudTimer;

	void UpdateHud()
	{
		if (_status == null || _sim == null) return;
		_hudTimer -= GetProcessDeltaTime();
		if (_hudTimer > 0) return;
		_hudTimer = 0.25;
		var a = Analyse();
		double days = _sim.MorphTime / 86400;
		string time = days < 2 ? $"{_sim.MorphTime / 3600:F1} hours" : days < 730 ? $"{days:F0} days" : $"{days / 365.25:F1} years";
		_status.Text = string.Format(CultureInfo.InvariantCulture,
			"Landscape time {0}   (water {1:F0} s, step {2:F2} s)\nFastest flow {3:F1} m/s   deepest {4:F1} m\nSinuosity {5:F2}   channels {6:F1}   width {7:F0} m",
			time, _sim.HydroTime, _sim.Dt, _sim.MaxU, _sim.MaxH, a.sinuosity, a.channels, a.width);
	}
}
