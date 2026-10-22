using System.Collections.Generic;
using Godot;
using Renegade.Cards;
using Renegade.Looks;

namespace Cribbage.Game;

/// <summary>
/// A wooden cribbage board: for each player three streets of 40 holes (grouped in fives), run
/// out and back and out again, a start and the game hole at 121. Each player has two pegs; to
/// score, the back peg leaps over the front one to the new total, so the gap always shows the
/// last score. You are on the near side (gold pegs), Rosie on the far side (blue).
/// </summary>
public partial class Board : Node3D
{
	private const float Length = 3.75f, Depth = 0.6f, Thick = 0.07f;
	private readonly MeshInstance3D[,] _pegs = new MeshInstance3D[2, 2];
	private readonly int[,] _at = new int[2, 2];
	private readonly int[] _front = new int[2];

	public override void _Ready()
	{
		var wood = new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Length, Thick, Depth) },
			Position = new Vector3(0, Thick / 2, 0),
			MaterialOverride = Materials.For(Styles.BoardLook(Style.Wood)),
		};
		AddChild(wood);

		// The holes: dark dimples, drawn as one batch.
		var holes = new List<Vector3>();
		for (int p = 0; p < 2; p++)
		{
			for (int score = 1; score <= 120; score++)
				holes.Add(Hole(p, score));
			holes.Add(Hole(p, 0) + new Vector3(0, 0, -0.03f));
			holes.Add(Hole(p, 0) + new Vector3(0, 0, 0.03f));
		}
		holes.Add(Hole(0, 121));
		var mesh = new CylinderMesh { TopRadius = 0.014f, BottomRadius = 0.014f, Height = 0.004f, RadialSegments = 10, Rings = 1 };
		var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = holes.Count };
		for (int i = 0; i < holes.Count; i++)
			multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, holes[i] + new Vector3(0, 0.001f, 0)));
		AddChild(new MultiMeshInstance3D
		{
			Multimesh = multi,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1a0e07"), Roughness = 0.9f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		// The pegs.
		var colours = new[] { new Color("e8b33a"), new Color("3f7fd6") };
		for (int p = 0; p < 2; p++)
		{
			var material = new StandardMaterial3D { AlbedoColor = colours[p], Metallic = 0.6f, Roughness = 0.3f };
			for (int k = 0; k < 2; k++)
			{
				var peg = new MeshInstance3D { Mesh = PegMesh(), MaterialOverride = material };
				AddChild(peg);
				_pegs[p, k] = peg;
				peg.Position = Hole(p, 0) + new Vector3(0, 0, k == 0 ? -0.03f : 0.03f);
			}
		}
	}

	private static Mesh PegMesh()
	{
		var shaft = new CylinderMesh { TopRadius = 0.017f, BottomRadius = 0.017f, Height = 0.1f, RadialSegments = 10 };
		var head = new SphereMesh { Radius = 0.028f, Height = 0.056f, RadialSegments = 12, Rings = 6 };
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		st.AppendFrom(shaft, 0, new Transform3D(Basis.Identity, new Vector3(0, 0.04f, 0)));
		st.AppendFrom(head, 0, new Transform3D(Basis.Identity, new Vector3(0, 0.1f, 0)));
		st.GenerateNormals();
		return st.Commit();
	}

	/// <summary>Where a score's hole is, on the board (0 is the start, 121 the game hole).</summary>
	public static Vector3 Hole(int player, int score)
	{
		float top = Thick + 0.001f;
		if (score >= 121)
			return new Vector3(Length / 2 - 0.08f, top, 0);
		if (score <= 0)
			return new Vector3(-Length / 2 + 0.09f, top, player == 0 ? 0.17f : -0.17f);
		int row = (score - 1) / 40, i = (score - 1) % 40;
		int col = row % 2 == 0 ? i : 39 - i;
		float x = -Length / 2 + 0.2f + col * 0.08f + col / 5 * 0.036f;
		float side = player == 0 ? 1 : -1;
		float z = side * (0.07f + row * 0.075f);
		return new Vector3(x, top, z);
	}

	/// <summary>Pegs a new total: the back peg leaps over the front one. Returns the leap.</summary>
	public Tween? Peg(int player, int total)
	{
		int back = 1 - _front[player];
		if (total <= _at[player, _front[player]])
			return null;
		_at[player, back] = total;
		_front[player] = back;
		var peg = _pegs[player, back];
		var target = new Transform3D(Basis.Identity, ToGlobal(Hole(player, total)));
		return CardMotion.Fly(peg, target, 0.45f, lift: 0.22f);
	}

	/// <summary>Both pegs back to the start (a new game).</summary>
	public void Reset()
	{
		for (int p = 0; p < 2; p++)
		{
			_front[p] = 0;
			for (int k = 0; k < 2; k++)
			{
				_at[p, k] = 0;
				if (_pegs[p, k] != null)
					_pegs[p, k].Position = Hole(p, 0) + new Vector3(0, 0, k == 0 ? -0.03f : 0.03f);
			}
		}
	}
}
