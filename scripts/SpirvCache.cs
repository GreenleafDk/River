using System.Collections.Generic;
using Godot;

namespace RiverInABox;

/// <summary>
/// Compiled compute shaders, fetched once and kept. The C# wrappers must stay alive: a restart that fetches the same
/// RDShaderSpirV again right after the GC finalized its old wrapper fails in the interop (seen in Planets in a Box).
/// </summary>
public static class SpirvCache
{
	static readonly Dictionary<string, RDShaderSpirV> Cache = new();

	public static RDShaderSpirV Get(string path, string version)
	{
		string key = path + ":" + version;
		lock (Cache)
		{
			if (!Cache.TryGetValue(key, out var spirv))
				Cache[key] = spirv = GD.Load<RDShaderFile>(path).GetSpirV(version);
			return spirv;
		}
	}
}
