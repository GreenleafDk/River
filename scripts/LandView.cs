using System;
using Godot;

namespace RiverInABox;

/// <summary>
/// Draws the landscape and the water: two grid meshes with one vertex per sim cell whose shaders read the sim's
/// textures (heights are displaced on the GPU, nothing comes back to the CPU). World x = downstream, z = across,
/// y = up (heights times the vertical exaggeration).
/// </summary>
public partial class LandView : Node3D
{
	MeshInstance3D _ground = null!, _water = null!;
	ShaderMaterial _groundMat = null!, _waterMat = null!;
	Texture2Drd _t0 = null!, _t1 = null!;
	float _vscale = 4f;
	int _mode;

	public float VScale
	{
		get => _vscale;
		set { _vscale = value; _groundMat?.SetShaderParameter("vscale", value); _waterMat?.SetShaderParameter("vscale", value); }
	}

	public int ViewMode
	{
		get => _mode;
		set { _mode = value; _groundMat?.SetShaderParameter("view_mode", value); _waterMat?.SetShaderParameter("view_mode", value); }
	}

	public float Contour { set => _groundMat?.SetShaderParameter("contour", value); }

	public void Build(RiverSim sim)
	{
		foreach (var c in GetChildren()) c.QueueFree();
		var mesh = GridMesh(sim.Nx, sim.Ny, sim.Dx);
		_t0 = new Texture2Drd { TextureRdRid = sim.Tex0 };
		_t1 = new Texture2Drd { TextureRdRid = sim.Tex1 };
		_groundMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/terrain.gdshader") };
		_waterMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://shaders/water.gdshader"), RenderPriority = 1 };
		foreach (var m in new[] { _groundMat, _waterMat })
		{
			m.SetShaderParameter("tex0", _t0);
			m.SetShaderParameter("tex1", _t1);
			m.SetShaderParameter("dx", sim.Dx);
			m.SetShaderParameter("vscale", _vscale);
			m.SetShaderParameter("view_mode", _mode);
		}
		_ground = new MeshInstance3D { Mesh = mesh, MaterialOverride = _groundMat, Name = "Ground" };
		_water = new MeshInstance3D { Mesh = mesh, MaterialOverride = _waterMat, Name = "Water", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
		AddChild(_ground);
		AddChild(_water);
	}

	static ArrayMesh GridMesh(int nx, int ny, float dx)
	{
		var verts = new Vector3[nx * ny];
		var uvs = new Vector2[nx * ny];
		for (int j = 0; j < ny; j++)
			for (int i = 0; i < nx; i++)
			{
				int v = j * nx + i;
				verts[v] = new Vector3((i + 0.5f) * dx, 0, (j + 0.5f) * dx);
				uvs[v] = new Vector2(i, j);
			}
		var idx = new int[(nx - 1) * (ny - 1) * 6];
		int k = 0;
		for (int j = 0; j < ny - 1; j++)
			for (int i = 0; i < nx - 1; i++)
			{
				int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1;
				idx[k++] = a; idx[k++] = b; idx[k++] = c;
				idx[k++] = b; idx[k++] = d; idx[k++] = c;
			}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = idx;
		var mesh = new ArrayMesh();
		mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		// Heights are set in the shader: give the culling a box that holds any relief.
		mesh.CustomAabb = new Aabb(new Vector3(0, -200, 0), new Vector3(nx * dx, 400, ny * dx));
		return mesh;
	}
}
