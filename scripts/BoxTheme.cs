using Godot;

namespace RiverInABox;

/// <summary>
/// The "in a Box" family theme, ported from Planets' SciFiTheme: translucent dark panels with chamfered corners, thin
/// accent borders with a soft glow (zero-offset shadows), brighter on hover, filled on press, tabular digits.
/// River uses a river-green teal accent. Panels are a little more opaque than
/// Planets' because the landscape behind them is bright. Everything is built in code (no assets).
/// Rules as in Planets: use the theme, no local style overrides; add a type variation here instead.
/// Variations: HeaderLabel, PrimaryButton, ToolButton (tool palette), Chip (small readout panels), SolidPanel (opaque).
/// </summary>
public static class BoxTheme
{
    public static readonly Color Accent = new(0.36f, 0.86f, 0.78f);
    public static readonly Color Text = new(0.88f, 0.92f, 0.96f);
    public static readonly Color TextBright = new(1.0f, 0.98f, 0.94f);
    public static readonly Color Dim = new(0.62f, 0.69f, 0.76f);
    private static readonly Color Panel = new(0.035f, 0.06f, 0.10f, 0.86f);
    private static readonly Color PanelSolid = new(0.035f, 0.06f, 0.10f, 0.97f);

    private static Theme? _theme;
    public static Theme Get() => _theme ??= Build();

    private static Color A(Color c, float alpha) => new(c.R, c.G, c.B, alpha);

    /// <summary>Chamfered box (corner_detail 1 cuts corners at 45°) with a border and an outer glow.</summary>
    public static StyleBoxFlat Box(Color bg, Color border, int borderWidth = 1, float glow = 0.25f, int glowSize = 8,
        int chamfer = 8, int padX = 12, int padY = 7, Color? glowColor = null)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = border,
            BorderWidthLeft = borderWidth, BorderWidthRight = borderWidth, BorderWidthTop = borderWidth, BorderWidthBottom = borderWidth,
            CornerRadiusTopLeft = chamfer, CornerRadiusBottomRight = chamfer,
            CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2,
            CornerDetail = 1,
            ShadowColor = A(glowColor ?? Accent, glow),
            ShadowSize = glow > 0 ? glowSize : 0,
            ContentMarginLeft = padX, ContentMarginRight = padX, ContentMarginTop = padY, ContentMarginBottom = padY,
            AntiAliasing = true,
        };
    }

    private static Theme Build()
    {
        var font = new FontVariation
        {
            BaseFont = new SystemFont { FontNames = new[] { "Bahnschrift", "Segoe UI", "Roboto", "Noto Sans", "sans-serif" } },
            OpentypeFeatures = new Godot.Collections.Dictionary { [TextServerManager.GetPrimaryInterface().NameToTag("tnum")] = 1 },
        };
        var t = new Theme { DefaultFont = font, DefaultFontSize = 16 };

        t.SetColor("font_color", "Label", Text);
        t.SetColor("font_shadow_color", "Label", new Color(0, 0, 0, 0.35f));
        t.SetConstant("shadow_outline_size", "Label", 3);
        t.SetConstant("shadow_offset_x", "Label", 0);
        t.SetConstant("shadow_offset_y", "Label", 0);
        t.SetTypeVariation("HeaderLabel", "Label");
        t.SetColor("font_color", "HeaderLabel", Accent);
        t.SetColor("font_shadow_color", "HeaderLabel", A(Accent, 0.3f));
        t.SetFontSize("font_size", "HeaderLabel", 14);

        var normal = Box(A(Panel, 0.8f), A(Accent, 0.4f), glow: 0.10f, glowSize: 4);
        var hover = Box(new Color(0.10f, 0.10f, 0.10f, 0.9f), Accent, glow: 0.35f, glowSize: 8);
        var pressed = Box(A(Accent, 0.30f), TextBright, borderWidth: 2, glow: 0.5f, glowSize: 10);
        var disabled = Box(A(Panel, 0.5f), A(Accent, 0.15f), glow: 0);
        foreach (var type in new[] { "Button", "OptionButton", "CheckButton" })
        {
            t.SetStylebox("normal", type, normal);
            t.SetStylebox("hover", type, hover);
            t.SetStylebox("pressed", type, pressed);
            t.SetStylebox("hover_pressed", type, pressed);
            t.SetStylebox("disabled", type, disabled);
            t.SetStylebox("focus", type, new StyleBoxEmpty());
            t.SetColor("font_color", type, Text);
            t.SetColor("font_hover_color", type, TextBright);
            t.SetColor("font_pressed_color", type, TextBright);
            t.SetColor("font_hover_pressed_color", type, TextBright);
            t.SetColor("font_disabled_color", type, A(Text, 0.35f));
            t.SetColor("icon_normal_color", type, Accent);
            t.SetColor("icon_hover_color", type, TextBright);
            t.SetColor("icon_pressed_color", type, TextBright);
        }
        // Switches are rows, not buttons: no box until hovered.
        t.SetStylebox("normal", "CheckButton", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4 });
        t.SetStylebox("pressed", "CheckButton", new StyleBoxEmpty { ContentMarginLeft = 4, ContentMarginRight = 4 });
        t.SetIcon("checked", "CheckButton", Switch(true));
        t.SetIcon("unchecked", "CheckButton", Switch(false));

        t.SetTypeVariation("PrimaryButton", "Button");
        t.SetStylebox("normal", "PrimaryButton", Box(A(Accent, 0.18f), Accent, glow: 0.35f, glowSize: 10));
        t.SetStylebox("hover", "PrimaryButton", Box(A(Accent, 0.3f), TextBright, glow: 0.55f, glowSize: 14));

        // Tool palette buttons: no frame at rest (the palette panel is the frame), a lit plate when chosen.
        t.SetTypeVariation("ToolButton", "Button");
        t.SetStylebox("normal", "ToolButton", Box(new Color(0, 0, 0, 0), new Color(0, 0, 0, 0), borderWidth: 0, glow: 0, padX: 4, padY: 4));
        t.SetStylebox("hover", "ToolButton", Box(A(Accent, 0.10f), A(Accent, 0.5f), glow: 0.2f, padX: 4, padY: 4));
        t.SetStylebox("pressed", "ToolButton", Box(A(Accent, 0.24f), Accent, glow: 0.45f, glowSize: 10, padX: 4, padY: 4));
        t.SetStylebox("hover_pressed", "ToolButton", Box(A(Accent, 0.3f), TextBright, glow: 0.5f, glowSize: 10, padX: 4, padY: 4));

        t.SetStylebox("panel", "PopupMenu", Box(PanelSolid, Accent, glow: 0.35f, glowSize: 10, padX: 6, padY: 6));
        t.SetStylebox("hover", "PopupMenu", Box(A(Accent, 0.22f), A(Accent, 0.8f), glow: 0, chamfer: 4, padX: 6, padY: 4));
        t.SetColor("font_color", "PopupMenu", Text);
        t.SetColor("font_hover_color", "PopupMenu", TextBright);
        t.SetConstant("v_separation", "PopupMenu", 10);

        t.SetStylebox("panel", "PanelContainer", Box(Panel, A(Accent, 0.35f), glow: 0.15f, glowSize: 8, chamfer: 12, padX: 12, padY: 10));
        // SolidPanel: an opaque panel for what lies over other panels (the scenario card).
        t.SetTypeVariation("SolidPanel", "PanelContainer");
        t.SetStylebox("panel", "SolidPanel", Box(PanelSolid, A(Accent, 0.5f), glow: 0.25f, glowSize: 10, chamfer: 12, padX: 12, padY: 10));
        t.SetTypeVariation("GlassPanel", "PanelContainer"); // see-through: the profile editor over the view (Match sim)
        t.SetStylebox("panel", "GlassPanel", Box(new Color(0.035f, 0.06f, 0.10f, 0.45f), A(Accent, 0.35f), glow: 0.1f, chamfer: 12, padX: 12, padY: 10));
        t.SetTypeVariation("Chip", "PanelContainer");
        t.SetStylebox("panel", "Chip", Box(Panel, A(Accent, 0.25f), glow: 0, chamfer: 8, padX: 10, padY: 5));
        t.SetTypeVariation("ModalBackdrop", "PanelContainer");
        t.SetStylebox("panel", "ModalBackdrop", new StyleBoxFlat { BgColor = A(PanelSolid, 0.7f) });
        t.SetStylebox("panel", "TooltipPanel", Box(PanelSolid, Accent, glow: 0.25f, padX: 10, padY: 6));
        t.SetColor("font_color", "TooltipLabel", Text);
        t.SetStylebox("separator", "HSeparator", new StyleBoxLine { Color = A(Accent, 0.35f), Thickness = 1 });

        // Tabs (the settings drawer).
        var tabSel = Box(A(Accent, 0.22f), Accent, glow: 0.3f, glowSize: 6, chamfer: 6, padX: 12, padY: 6);
        var tabOff = Box(new Color(0, 0, 0, 0), A(Accent, 0.25f), glow: 0, chamfer: 6, padX: 12, padY: 6);
        t.SetStylebox("tab_selected", "TabContainer", tabSel);
        t.SetStylebox("tab_unselected", "TabContainer", tabOff);
        t.SetStylebox("tab_hovered", "TabContainer", Box(A(Accent, 0.12f), A(Accent, 0.6f), glow: 0, chamfer: 6, padX: 12, padY: 6));
        t.SetStylebox("panel", "TabContainer", new StyleBoxEmpty { ContentMarginTop = 8 });
        t.SetColor("font_selected_color", "TabContainer", TextBright);
        t.SetColor("font_unselected_color", "TabContainer", Dim);
        t.SetColor("font_hovered_color", "TabContainer", Text);
        t.SetConstant("side_margin", "TabContainer", 0);

        var field = Box(A(PanelSolid, 0.9f), A(Accent, 0.45f), glow: 0.1f, glowSize: 4, chamfer: 6, padX: 8, padY: 5);
        t.SetStylebox("normal", "LineEdit", field);
        t.SetStylebox("focus", "LineEdit", Box(A(Accent, 0.1f), Accent, glow: 0.4f, glowSize: 8, chamfer: 6, padX: 8, padY: 5));
        t.SetColor("font_color", "LineEdit", TextBright);
        t.SetColor("caret_color", "LineEdit", Accent);
        t.SetColor("selection_color", "LineEdit", A(Accent, 0.35f));

        t.SetStylebox("slider", "HSlider", Track(A(Accent, 0.22f), 0));
        t.SetStylebox("grabber_area", "HSlider", Track(A(Accent, 0.8f), 0.4f));
        t.SetStylebox("grabber_area_highlight", "HSlider", Track(Accent, 0.6f));
        t.SetIcon("grabber", "HSlider", Dot(24, 0.85f));
        t.SetIcon("grabber_highlight", "HSlider", Dot(24, 1f));
        t.SetIcon("grabber_disabled", "HSlider", Dot(24, 0.3f));
        // Pace slider (ThemeTypeVariation = "PaceSlider", as in Planets): the glowing part up to the dot as usual, but no
        // track of its own: under it a wider ProgressBar is the track, its fill the speed achieved.
        t.SetTypeVariation("PaceSlider", "HSlider");
        t.SetStylebox("slider", "PaceSlider", new StyleBoxEmpty { ContentMarginTop = 2, ContentMarginBottom = 2 });
        t.SetStylebox("background", "ProgressBar", Track(A(Accent, 0.12f), 0));

        var grab = new StyleBoxFlat { BgColor = A(Accent, 0.35f) }; grab.SetCornerRadiusAll(3);
        foreach (var sb in new[] { "VScrollBar", "HScrollBar" })
        {
            t.SetStylebox("grabber", sb, grab);
            t.SetStylebox("grabber_highlight", sb, grab);
            t.SetStylebox("grabber_pressed", sb, grab);
            t.SetStylebox("scroll", sb, new StyleBoxEmpty { ContentMarginLeft = 3, ContentMarginRight = 3, ContentMarginTop = 3, ContentMarginBottom = 3 });
        }
        t.SetConstant("separation", "VBoxContainer", 6);
        t.SetConstant("separation", "HBoxContainer", 6);
        return t;
    }

    private static StyleBoxFlat Track(Color c, float glow) => new()
    {
        BgColor = c,
        ContentMarginTop = 2, ContentMarginBottom = 2,
        CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
        ShadowColor = A(Accent, glow * 0.6f),
        ShadowSize = glow > 0 ? 5 : 0,
    };

    private static ImageTexture Dot(int size, float intensity)
    {
        var img = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float r = new Vector2(x - c, y - c).Length() / c;
                float core = Mathf.Clamp((0.42f - r) * 8f, 0, 1);
                float halo = Mathf.Pow(Mathf.Clamp(1 - r, 0, 1), 2.2f) * 0.8f;
                var col = Accent.Lerp(TextBright, core);
                img.SetPixel(x, y, new Color(col.R, col.G, col.B, Mathf.Clamp(core + halo, 0, 1) * intensity));
            }
        return ImageTexture.CreateFromImage(img);
    }

    /// <summary>On/off switch drawn as a small chamfer-free pill with a dot.</summary>
    private static ImageTexture Switch(bool on)
    {
        int w = 44, h = 24;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var track = on ? A(Accent, 0.55f) : new Color(0.3f, 0.35f, 0.42f, 0.7f);
        var knob = on ? TextBright : new Color(0.75f, 0.8f, 0.86f);
        float r = h / 2f - 1, kx = on ? w - h / 2f : h / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, h / 2f, w - h / 2f);
                float dTrack = new Vector2(px - cx, py - h / 2f).Length() - r;
                float dKnob = new Vector2(px - kx, py - h / 2f).Length() - (r - 3);
                var col = new Color(0, 0, 0, 0);
                float aT = Mathf.Clamp(0.5f - dTrack, 0, 1);
                if (aT > 0) col = new Color(track.R, track.G, track.B, track.A * aT);
                float aK = Mathf.Clamp(0.5f - dKnob, 0, 1);
                if (aK > 0) col = col.Lerp(new Color(knob.R, knob.G, knob.B, 1), aK);
                img.SetPixel(x, y, col);
            }
        return ImageTexture.CreateFromImage(img);
    }
}
