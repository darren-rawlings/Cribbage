using System.Threading.Tasks;
using Godot;

namespace Cribbage.Game;

/// <summary>
/// "Nineteen": the score you can't make, said of a hand worth nothing. The screen dims and a big
/// gold 19 in a glowing ring appears in the middle, spinning on its axis, slowly turning and
/// pulsing, with whose hand it was beneath. It stays until tapped (<see cref="Closed"/>).
/// </summary>
public partial class Nineteen : Control
{
	private static readonly Color Gold = new("f2c14e");
	private static readonly Color Deep = new("3a2410");

	private readonly TaskCompletionSource _closed = new();
	private readonly string _caption;
	private float _t;
	private bool _closing;

	public Nineteen(string caption) => _caption = caption;

	/// <summary>Completes once the player has tapped it away.</summary>
	public Task Closed => _closed.Task;

	public override void _Ready()
	{
		SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
		MouseFilter = MouseFilterEnum.Stop;
		MoveToFront();
		Modulate = new Color(1, 1, 1, 0);
		CreateTween().TweenProperty(this, "modulate:a", 1f, 0.35f);
	}

	public override void _Process(double delta)
	{
		_t += (float)delta;
		QueueRedraw();
	}

	public override void _GuiInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { Pressed: true } or InputEventScreenTouch { Pressed: true })
		{
			AcceptEvent();
			Close();
		}
	}

	public void Close()
	{
		if (_closing)
			return;
		_closing = true;
		var tween = CreateTween();
		tween.TweenProperty(this, "modulate:a", 0f, 0.25f);
		tween.TweenCallback(Callable.From(() =>
		{
			_closed.TrySetResult();
			QueueFree();
		}));
	}

	public override void _Draw()
	{
		if (Size.X < 10 || Size.Y < 10)
			return;
		// The screen dims behind it.
		DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.02f, 0.01f, 0, 0.7f));
		var centre = Size / 2 + new Vector2(0, -60);
		float radius = Mathf.Min(Size.X, Size.Y) * 0.22f;
		float pulse = 0.6f + 0.4f * Mathf.Sin(_t * 4f);
		// Grows in at the start.
		float grow = Mathf.Min(1, _t / 0.45f);
		grow = 1 - (1 - grow) * (1 - grow);

		// The glow: soft rings of gold, breathing.
		for (int i = 12; i >= 1; i--)
			DrawCircle(centre, radius * grow * (1 + i * 0.09f), Gold with { A = 0.07f * pulse * (13 - i) / 12f });

		// The medal spins on its upright axis (squashing sideways as it turns) and slowly rolls round.
		float spin = Mathf.Cos(_t * 2.4f);
		float squash = Mathf.Max(0.12f, Mathf.Abs(spin));
		DrawSetTransform(centre, Mathf.Sin(_t * 0.8f) * 0.25f, new Vector2(squash, 1) * grow);
		DrawCircle(Vector2.Zero, radius, Deep);
		DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 96, Gold, radius * 0.11f, true);
		DrawArc(Vector2.Zero, radius * 0.82f, 0, Mathf.Tau, 96, Gold with { A = 0.55f }, 3, true);
		var font = ThemeDB.FallbackFont;
		int size = (int)(radius * 1.05f);
		var text = font.GetStringSize("19", HorizontalAlignment.Left, -1, size);
		var at = new Vector2(-text.X / 2, text.Y * 0.32f);
		// A gleam that sweeps across as it turns.
		DrawString(font, at + new Vector2(3, 3), "19", HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.5f));
		DrawString(font, at, "19", HorizontalAlignment.Left, -1, size, Gold.Lerp(Colors.White, 0.35f * pulse * (spin > 0 ? 1 : 0.4f)));
		DrawSetTransform(Vector2.Zero, 0, Vector2.One);

		// Whose hand it was, and how to go on.
		void Line(string s, int fontSize, float y, Color colour)
		{
			var w = font.GetStringSize(s, HorizontalAlignment.Left, -1, fontSize).X;
			DrawString(font, new Vector2(Size.X / 2 - w / 2 + 2, y + 2), s, HorizontalAlignment.Left, -1, fontSize, new Color(0, 0, 0, 0.6f));
			DrawString(font, new Vector2(Size.X / 2 - w / 2, y), s, HorizontalAlignment.Left, -1, fontSize, colour);
		}
		Line(_caption, 40, centre.Y + radius * 1.45f + 30, new Color("fff3e0"));
		Line("Tap to continue", 28, centre.Y + radius * 1.45f + 90, Gold with { A = 0.5f + 0.5f * pulse });
	}
}
