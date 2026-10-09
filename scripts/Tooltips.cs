using System.Collections.Generic;

namespace RiverInABox;

/// <summary>
/// Every control's tooltip, keyed by its label (sliders and switches by their name, buttons by their text).
/// New controls need an entry here; <see cref="Get"/> returns "" for a missing one.
/// </summary>
public static class Tooltips
{
	static readonly Dictionary<string, string> Tips = new()
	{
		// Top bar
		["PLAY"] = "Start or pause the simulation (Space).",
		["RESTART"] = "Start again from the landscape as it was built (the same settings, a fresh river).",
		["Speed"] = "How many water time steps are computed per frame. Higher = faster, until the graphics card cannot keep up.",
		["View"] = "What the colours show: Natural; Flow speed (blue slow, red fast); Water depth (blue shallow, red deep); Soil and plants (sand yellow, rock blue-grey, plants green).",
		["Fall"] = "How much the landscape falls along its length, in %. It drives the water downhill but is not drawn: the land is shown flat. Steep land gives fast, straight, braided rivers; flat land gives slow, winding ones and floods.",
		["Water"] = "How much water the inlet lets in, in cubic metres per second. More water makes a bigger, wider river.",
		["Height x"] = "Vertical exaggeration of the view only. Real river landscapes are very flat; this makes banks and channels visible.",
		["SETTINGS"] = "Open the landscape and river settings.",

		// Tools
		["Look"] = "Drag to turn the camera, wheel or pinch to zoom, right drag to turn, shift+right or middle drag to pan.",
		["Raise"] = "Paint to raise the ground (hills, dams, levees).",
		["Dig"] = "Paint to dig the ground down (channels, lakes).",
		["Smooth"] = "Paint to even out the ground.",
		["Rock"] = "Paint hard rock into the ground: water can hardly erode it, so the river has to go around.",
		["Soft soil"] = "Paint soft, easily eroded soil (removes rock).",
		["Plants"] = "Paint plants: their roots hold the soil, so the banks erode much more slowly, and they slow the water.",
		["Add water"] = "Pour water onto the landscape.",
		["Remove water"] = "Take water away (dry up a lake or puddle).",
		["Radius"] = "Size of the brush in metres.",
		["Strength"] = "How fast the brush works.",

		// Landscape
		["Preset"] = "Ready-made landscapes. Choosing one builds it at once.",
		["Width"] = "Width of the landscape across the river, in metres. Takes effect with NEW LANDSCAPE.",
		["Length"] = "Length of the landscape downstream, in metres. Takes effect with NEW LANDSCAPE.",
		["Cells across"] = "Resolution: grid cells across the width. More = finer detail but slower. Takes effect with NEW LANDSCAPE.",
		["Inlet width"] = "Width of the incoming water and of the starting channel, in metres.",
		["Inlet angle"] = "Direction of the incoming water, degrees from straight downstream. An angled inflow kicks off the first bend.",
		["Inlet position"] = "Where across the upstream edge the water comes in, % of the width.",
		["Starting channel"] = "Depth of the straight groove cut for the river at the start (0 = none: the water finds its own way). NEW LANDSCAPE.",
		["Bumps"] = "Random unevenness of the ground in metres. NEW LANDSCAPE.",
		["Soil patchiness"] = "How much the soil's erodibility varies from place to place (0 = the same everywhere). Uneven soil gives the river reasons to bend. NEW LANDSCAPE.",
		["Plants at start"] = "How overgrown the land is at the start (0 = bare, 1 = fully overgrown). NEW LANDSCAPE.",
		["Loose sand"] = "Depth of loose sand over the whole landscape at the start (it erodes freely). NEW LANDSCAPE.",
		["Periodic sides"] = "On: the two long sides are joined, a river leaving one side comes in at the other (no walls to get stuck on). Off: the sides are walls, with an invisible push away from them (below).",
		["Push from the walls"] = "Only without periodic sides: an invisible slope (in %) near each wall that pushes water and sand back towards the middle, so the river does not stick to a wall. Like the fall it is a force only: the ground stays flat and nothing can erode it away. Acts at once.",
		["Wall zone width"] = "How far in from each wall the push (and the hard ground) reaches, % of the width.",
		["Raised ground at walls"] = "Target height of the ground in the wall zone, rising to this many metres at the wall (0 = flat). If the river digs into it, it grows back (see below), so the river is slowly pushed out again.",
		["Wall zone grows back in"] = "How many landscape days the wall zone takes to grow back to its target height after the river has dug into it (0 = never).",
		["Hard ground at walls"] = "The wall zone is hard rock, harder towards the wall, so the river cannot eat its way to the wall. NEW LANDSCAPE.",
		["Sea at the outlet (new landscape)"] = "Put a sea at the downstream end. Its level is fixed, so the river slows down there, drops its sand and builds a delta. Builds a new landscape.",
		["Sea level"] = "Level of the sea relative to the plain at the outlet, in metres (negative = below the plain).",
		["Sea share of length"] = "How much of the length is sea. NEW LANDSCAPE.",
		["NEW LANDSCAPE"] = "Build a new landscape with the settings above and start the river again.",

		// River
		["Smooth the view"] = "Draws the ground and water a little smoothed, so the grid cells do not show as steps on steep banks (0 = the raw cells). Only the picture changes, not the simulation.",
		["Landscape speed"] = "How much faster the landscape changes than the water flows (morphological acceleration). Real rivers take years to move; this lets you watch it in minutes. Too high and the ground changes in jumps (it is slowed automatically then).",
		["Grain size"] = "Size of the sand and gravel grains in mm. Fine sand moves easily; coarse gravel needs fast water.",
		["Bank erosion"] = "How fast the banks are worn away where water runs past them.",
		["Outer bend erosion"] = "Extra bank erosion on the outside of bends, where the water is fastest and runs into the bank. This is what makes the bends grow and wander: the main driver of meandering.",
		["Inner bend deposition"] = "Strength of the spiral flow in bends, which sweeps sand along the bottom towards the inside of the bend where it settles as point bars. More = sharper bends that move outwards faster.",
		["Bank holds below"] = "Water slower than this (m/s) does not erode the banks at all.",
		["Downhill pull"] = "How strongly sand rolls downhill on the river bed. More = smoother, wider channels; less = deep narrow ones.",
		["Suspended load"] = "How much fine sediment fast water lifts into suspension (muddy water). It settles again where the water slows: on floodplains, in lakes and the sea.",
		["Settling speed"] = "How fast suspended sediment sinks out of slow water (mm/s). Slow = mud travels far; fast = it drops near the river.",
		["Plants grow in"] = "How many landscape days it takes plants to cover dry ground (0 = no plants). Plants on new sand bars fix them, which keeps the river in one channel.",
		["Plant strength"] = "How strongly plants protect the soil from erosion (0 = not at all, 1 = fully).",
		["Sediment feed"] = "How much sand comes in with the water at the inlet, compared with what the river can carry (1 = balanced). Less: the river digs in and stays put; more: it builds up, spills over and braids into a fan.",
		["Inlet wandering"] = "Random slow swinging of the inflow direction in degrees, like the hose at a sand table. A little randomness starts the meanders.",
		["Wandering pace"] = "How quickly the inlet's random swinging changes (landscape days).",
		["Evaporation"] = "Water lost to evaporation and seepage (mm per hour), so puddles left on the land dry up.",
		["Roughness (Manning)"] = "Friction of the river bed (Manning's n). Rougher = slower, deeper water.",
		["Bed smoothing"] = "Evens out small bumps on the river bed under water. Too little: the sand gets a pitted, dotted look with puddles; too much: bars and pools are smeared out.",
	};

	public static string Get(string key) => Tips.TryGetValue(key, out var t) ? t : "";
}
