using System.Collections.Generic;
using Godot;
using Renegade.Cards;

namespace Cribbage.Game;

/// <summary>
/// A pale wooden cribbage board standing lengthways to your right, in the style of the classic
/// three-track boards: each side has a painted lane (in its player's colour) with a row of holes.
/// The lanes run side by side up the left (holes 1–35), round the top (36–45), down the right
/// (46–85), round a turn at the bottom (86–90) and up the middle (91–120) to the game hole marked
/// FINISH. Numbers mark every five holes; the start holes sit in a box at the bottom left. Thin
/// lines mark the double skunk (past 60) and the skunk (past 90). Each side has two pegs; to score,
/// the back peg leaps over the front one to the new total, so the gap always shows the last score.
/// </summary>
public partial class Board : Node3D
{
	private const float Thick = 0.07f, Lane = 0.072f, Step = 0.061f, GroupGap = 0.03f;

	private MeshInstance3D[,] _pegs = null!;
	private int[,] _at = null!;
	private int[] _front = null!;

	/// <summary>How many lanes (sides): two, or three for a three-player game.</summary>
	public int Tracks { get; set; } = 2;

	/// <summary>Each lane's colour: its player's (their name's colour in the log, and their place setting's).</summary>
	public Color[] Colours { get; set; } = { TableTalk.SeatColour(0), TableTalk.SeatColour(1), TableTalk.SeatColour(2) };

	/// <summary>Half the distance between the left and right columns.</summary>
	private float C => Tracks * Lane + 0.16f;

	private float Width => 2 * C + Tracks * Lane + 0.2f;

	// Where the straight runs start and end, along the board (z; up the board is towards -z).
	private const float Top = -1.42f;
	private static float Run(int holes) => holes * Step + (holes - 1) / 5 * GroupGap;
	private static float RightBottom => Top + Run(40);
	private float LeftBottom => Top + Run(35);
	private float Length => (RightBottom + C / 2 + 0.3f) - (Top - C - 0.22f);
	private float Middle => ((RightBottom + C / 2 + 0.3f) + (Top - C - 0.22f)) / 2;

	public override void _Ready()
	{
		_pegs = new MeshInstance3D[Tracks, 2];
		_at = new int[Tracks, 2];
		_front = new int[Tracks];

		// The board: pale pine with a darker edge.
		var pine = new StandardMaterial3D { AlbedoColor = new Color("e2c38f"), Roughness = 0.7f };
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Width, Thick, Length) },
			Position = new Vector3(0, Thick / 2, Middle),
			MaterialOverride = pine,
		});
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Width + 0.03f, Thick * 0.9f, Length + 0.03f) },
			Position = new Vector3(0, Thick * 0.45f - 0.001f, Middle),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("a77b45"), Roughness = 0.8f },
		});

		// The painted lanes, each a strip following the track.
		for (int t = 0; t < Tracks; t++)
			AddChild(new MeshInstance3D
			{
				Mesh = Strip(t),
				MaterialOverride = new StandardMaterial3D { AlbedoColor = Colours[t] * 0.92f, Roughness = 0.55f },
				CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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
			multi.SetInstanceTransform(i, new Transform3D(Basis.Identity, holes[i] + new Vector3(0, 0.0015f, 0)));
		AddChild(new MultiMeshInstance3D
		{
			Multimesh = multi,
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("1a0e07"), Roughness = 0.9f },
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
		});

		// Numbers every five, beside the lanes; START and FINISH.
		var ink = new Color("5a3418");
		for (int n = 5; n <= 120; n += 5)
		{
			var (at, _, outward) = Path(n);
			Text(n.ToString(), at + outward * (Tracks * Lane / 2 + 0.065f), 30, ink);
		}
		Text("START", new Vector3(-C, Thick, LeftBottom + 0.36f), 26, ink);
		Text("FINISH", Hole(0, 121) + new Vector3(0, 0, -0.1f) with { X = 0 }, 26, ink);

		// The skunk lines: past 60 (double skunk) and past 90 (skunk).
		Mark(60.5f, new Color("d9a441"));
		Mark(90.5f, new Color("c9452e"));

		// The pegs, in each side's colour.
		for (int t = 0; t < Tracks; t++)
		{
			var material = new StandardMaterial3D { AlbedoColor = Colours[t], Metallic = 0.6f, Roughness = 0.3f };
			for (int k = 0; k < 2; k++)
			{
				var peg = new MeshInstance3D { Mesh = PegMesh(), MaterialOverride = material };
				AddChild(peg);
				_pegs[t, k] = peg;
				peg.Position = Start(t, k);
			}
		}
	}

	/// <summary>
	/// A point on the track's centre line for a hole number (fractions in between), with the
	/// direction of travel and the side to put its number on.
	/// </summary>
	private (Vector3 At, Vector3 Along, Vector3 Outward) Path(float n)
	{
		float y = Thick;
		Vector3 Up(float x, float z) => new(x, y, z);
		if (n <= 35)
		{
			// Up the left column.
			float z = LeftBottom - Offset(n - 1);
			return (Up(-C, z), Vector3.Forward, Vector3.Left);
		}
		if (n <= 45)
		{
			// Round the top, left to right.
			float a = Mathf.Pi - (n - 35.5f) / 10f * Mathf.Pi;
			var centre = Up(0, Top - 0.03f);
			var radial = new Vector3(Mathf.Cos(a), 0, -Mathf.Sin(a));
			return (centre + radial * C, new Vector3(Mathf.Sin(a), 0, Mathf.Cos(a)), radial);
		}
		if (n <= 85)
		{
			// Down the right column.
			float z = Top + Offset(n - 46) + 0.03f;
			return (Up(C, z), Vector3.Back, Vector3.Right);
		}
		if (n <= 90)
		{
			// Round the turn at the bottom, from the right column to the middle.
			float a = (n - 85.5f) / 5f * Mathf.Pi;
			var centre = Up(C / 2, RightBottom + 0.06f);
			var radial = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			return (centre + radial * (C / 2), new Vector3(-Mathf.Sin(a), 0, Mathf.Cos(a)), radial);
		}
		// Up the middle to the finish.
		float zm = RightBottom + 0.06f - Offset(n - 91) - 0.04f;
		return (Up(0, zm), Vector3.Forward, Vector3.Right);
	}

	/// <summary>Distance along a straight run for a hole index (0 the first), with a gap every five.</summary>
	private static float Offset(float i) => i * Step + Mathf.Floor(i / 5) * GroupGap;

	/// <summary>A lane's sideways place: lanes sit side by side across the track, the first outermost.</summary>
	private float LaneOffset(int track) => (track - (Tracks - 1) / 2f) * Lane;

	/// <summary>Where a score's hole is, on the board (121 is the game hole, FINISH).</summary>
	public Vector3 Hole(int track, int score)
	{
		if (score <= 0)
			return Start(track, 0);
		if (score >= 121)
			return Path(120).At + new Vector3(LaneOffset(track), 0.001f, -0.13f);
		var (at, along, _) = Path(score);
		var side = along.Cross(Vector3.Up).Normalized();
		return at - side * LaneOffset(track) + Vector3.Up * 0.001f;
	}

	/// <summary>The two start holes for a lane, in the box below the first column.</summary>
	private Vector3 Start(int track, int k) => new(-C + LaneOffset(track), Thick + 0.001f, LeftBottom + 0.1f + k * 0.07f);

	/// <summary>A lane's painted strip, from the start box to the finish.</summary>
	private Mesh Strip(int track)
	{
		var st = new SurfaceTool();
		st.Begin(Mesh.PrimitiveType.Triangles);
		var points = new List<(Vector3 At, Vector3 Side)>();
		points.Add((Start(track, 1) with { Y = Thick }, Vector3.Right));
		for (float n = 1; n <= 120.01f; n += 0.25f)
		{
			var (at, along, _) = Path(n);
			var side = along.Cross(Vector3.Up).Normalized();
			points.Add((at - side * LaneOffset(track), side));
		}
		points.Add((Hole(track, 121) with { Y = Thick }, Vector3.Right));
		float half = Lane * 0.44f;
		for (int i = 1; i < points.Count; i++)
		{
			var (a, sa) = points[i - 1];
			var (b, sb) = points[i];
			var lift = Vector3.Up * 0.0006f;
			Vector3 a0 = a - sa * half + lift, a1 = a + sa * half + lift, b0 = b - sb * half + lift, b1 = b + sb * half + lift;
			foreach (var v in new[] { a0, b0, a1, a1, b0, b1 })
			{
				st.SetNormal(Vector3.Up);
				st.AddVertex(v);
			}
		}
		return st.Commit();
	}

	/// <summary>A thin line across every lane just past a hole (a skunk line).</summary>
	private void Mark(float n, Color colour)
	{
		var (at, along, _) = Path(n);
		AddChild(new MeshInstance3D
		{
			Mesh = new BoxMesh { Size = new Vector3(Tracks * Lane + 0.03f, 0.003f, 0.012f) },
			Position = at + Vector3.Up * 0.002f,
			Rotation = new Vector3(0, Mathf.Atan2(along.X, along.Z), 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = colour, Roughness = 0.5f },
		});
	}

	private void Text(string text, Vector3 at, int size, Color colour) => AddChild(new Label3D
	{
		Text = text,
		FontSize = size,
		PixelSize = 0.0032f,
		Modulate = colour,
		OutlineSize = 0,
		Position = at + Vector3.Up * 0.002f,
		RotationDegrees = new Vector3(-90, 0, 0),
	});

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
