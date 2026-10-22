using System.Collections.Generic;
using Godot;
using Renegade.Cards;
using Renegade.Looks;

namespace Cribbage.Game;

/// <summary>
/// A wooden cribbage board standing lengthways to your right. Each side has a track of three
/// columns in the traditional S: up the first for holes 1–50, down the second for 51–90, and up
/// the third for 91–120, with the game hole (121) at the top. The double-skunk line (past 60) and
/// the skunk line (past 90) are marked across the board. Each side has two pegs; to score, the
/// back peg leaps over the front one to the new total, so the gap always shows the last score.
/// </summary>
public partial class Board : Node3D
{
	private const float Length = 3.75f, Thick = 0.07f, TrackWidth = 0.33f, ColumnGap = 0.092f, Step = 0.059f, GroupGap = 0.03f;
	private static readonly Color[] PegColours = { new("e8b33a"), new("3f7fd6"), new("4fbf6a") };

	private MeshInstance3D[,] _pegs = null!;
	private int[,] _at = null!;
	private int[] _front = null!;

	/// <summary>How many tracks (sides): two, or three for a three-player game.</summary>
	public int Tracks { get; set; } = 2;

	private float Width => 0.14f + Tracks * TrackWidth;

	public override void _Ready()
	{
		_pegs = new MeshInstance3D[Tracks, 2];
		_at = new int[Tracks, 2];
		_front = new int[Tracks];
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Width, Thick, Length) },
			Position = new Vector3(0, Thick / 2, 0),
			MaterialOverride = Materials.For(Styles.BoardLook(Style.Wood)),
		});

		// The holes: dark dimples, drawn as one batch.
		var holes = new List<Vector3>();
		for (int t = 0; t < Tracks; t++)
		{
			for (int score = 1; score <= 121; score++)
				holes.Add(Hole(t, score));
			holes.Add(Start(t, 0));
			holes.Add(Start(t, 1));
		}
		var mesh = new CylinderMesh { TopRadius = 0.017f, BottomRadius = 0.017f, Height = 0.004f, RadialSegments = 10, Rings = 1 };
		var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = holes.Count };
		for (int i = 0; i < holes.Count; i++)
			multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, holes[i] + new Vector3(0, 0.001f, 0)));
		AddChild(new MultiMeshInstance3D
		{
			Multimesh = multi,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1a0e07"), Roughness = 0.9f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		// Each track's colour, as a thin inlay along its side.
		for (int t = 0; t < Tracks; t++)
			AddChild(new MeshInstance3D
			{
				Mesh = new BoxMesh { Size = new Vector3(0.012f, 0.004f, Length - 0.3f) },
				Position = new Vector3(Column(t, 0) - 0.045f, Thick + 0.001f, 0),
				MaterialOverride = new StandardMaterial3D { AlbedoColor = PegColours[t] with { A = 1 }, Roughness = 0.5f },
			});

		// The skunk lines: past 60 (double skunk) on the way down, past 90 (skunk) at the turn.
		Line(Level(60) - 0.5f, new[] { 1 }, "SS", new Color("d9a441"));
		Line(Level(90) - 0.5f, new[] { 1, 2 }, "S", new Color("c9452e"));

		// The pegs.
		for (int t = 0; t < Tracks; t++)
		{
			var material = new StandardMaterial3D { AlbedoColor = PegColours[t], Metallic = 0.6f, Roughness = 0.3f };
			for (int k = 0; k < 2; k++)
			{
				var peg = new MeshInstance3D { Mesh = PegMesh(), MaterialOverride = material };
				AddChild(peg);
				_pegs[t, k] = peg;
				peg.Position = Start(t, k);
			}
		}
	}

	/// <summary>A marker line across the given columns of every track at a level, with a small label beside the board.</summary>
	private void Line(float level, int[] columns, string label, Color colour)
	{
		float z = Z(level);
		var material = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.6f };
		for (int t = 0; t < Tracks; t++)
			foreach (int c in columns)
				AddChild(new MeshInstance3D
				{
					Mesh = new BoxMesh { Size = new Vector3(ColumnGap * 0.9f, 0.004f, 0.008f) },
					Position = new Vector3(Column(t, c), Thick + 0.002f, z),
					MaterialOverride = material,
				});
		AddChild(new Label3D
		{
			Text = label,
			FontSize = 40,
			PixelSize = 0.004f,
			Modulate = colour,
			OutlineSize = 6,
			OutlineModulate = new Color(0.1f, 0.06f, 0.03f),
			Position = new Vector3(Width / 2 - 0.035f, Thick + 0.003f, z),
			RotationDegrees = new Vector3(-90, 0, 0),
		});
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

	private float Column(int track, int column) => -Width / 2 + 0.1f + track * TrackWidth + column * ColumnGap;

	/// <summary>How far up the board (in holes from the bottom) a score sits, along the S.</summary>
	private static float Level(int score) => score switch
	{
		<= 50 => score - 1,
		<= 90 => 49 - (score - 51),
		<= 120 => 10 + (score - 91),
		_ => 51.5f,
	};

	private static int ColumnOf(int score) => score <= 50 ? 0 : score <= 90 ? 1 : score <= 120 ? 2 : 1;

	/// <summary>The board runs away from you: higher levels are further up the screen.</summary>
	private static float Z(float level) => Length / 2 - 0.22f - level * Step - Mathf.Floor(Mathf.Max(0, level) / 5) * GroupGap;

	/// <summary>Where a score's hole is, on the board (121 is the game hole).</summary>
	public Vector3 Hole(int track, int score)
	{
		float top = Thick + 0.001f;
		if (score <= 0)
			return Start(track, 0);
		return new Vector3(Column(track, ColumnOf(score)), top, Z(Level(score)));
	}

	/// <summary>The two start holes below the first column.</summary>
	private Vector3 Start(int track, int k) => new(Column(track, k), Thick + 0.001f, Length / 2 - 0.09f);

	/// <summary>Pegs a new total for a side: the back peg leaps over the front one. Returns the leap.</summary>
	public Tween? Peg(int track, int total)
	{
		int back = 1 - _front[track];
		if (total <= _at[track, _front[track]])
			return null;
		_at[track, back] = total;
		_front[track] = back;
		var peg = _pegs[track, back];
		var target = new Transform3D(Basis.Identity, ToGlobal(Hole(track, total)));
		return CardMotion.Fly(peg, target, 0.45f, lift: 0.22f);
	}

	/// <summary>Both pegs of every side back to the start (a new game).</summary>
	public void Reset()
	{
		for (int t = 0; t < Tracks; t++)
		{
			_front[t] = 0;
			for (int k = 0; k < 2; k++)
			{
				_at[t, k] = 0;
				if (_pegs[t, k] != null)
					_pegs[t, k].Position = Start(t, k);
			}
		}
	}
}
