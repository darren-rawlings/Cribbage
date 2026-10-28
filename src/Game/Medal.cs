using System.Threading.Tasks;
using Godot;

namespace Cribbage.Game;

/// <summary>
/// A medal for a hand worth remembering: bronze for a 19 (the score you can't make, said of a hand
/// worth nothing), silver for a 28, gold for a 29 (the best there is). The screen dims and the big
/// medal appears in the middle, glowing, spinning on its upright axis like a coin (its rim shows as
/// it turns edge-on, then its back, the number reading backwards) and slowly rocking, with whose
/// hand it was beneath. It stays until tapped (<see cref="Closed"/>).
/// </summary>
public partial class Medal : Control
{
	private readonly TaskCompletionSource _closed = new();
	private readonly string _number, _caption;
	private readonly Color _metal, _deep, _rim;
	private float _t;
	private bool _closing;

	public Medal(int points, string caption)
	{
		_number = points.ToString();
		_caption = caption;
		(_metal, _deep, _rim) = points switch
		{
			29 => (new Color("f2c14e"), new Color("3a2410"), new Color("a8781f")),
			28 => (new Color("dfe4ea"), new Color("2c3138"), new Color("8d969f")),
			_ => (new Color("d08a4c"), new Color("3a1e0c"), new Color("8a5226")),
		};
	}

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

		// The glow: soft rings of the metal, breathing.
		for (int i = 12; i >= 1; i--)
			DrawCircle(centre, radius * grow * (1 + i * 0.09f), _metal with { A = 0.07f * pulse * (13 - i) / 12f });

		// It spins like a coin: squashing sideways as it turns, the rim showing beside it, the back
		// (the number reversed) when it's turned away. It rocks slowly as it goes.
		float angle = _t * 2.4f;
		float facing = Mathf.Cos(angle);
		float squash = Mathf.Max(0.04f, Mathf.Abs(facing));
		float roll = Mathf.Sin(_t * 0.8f) * 0.25f;
		float thickness = radius * 0.16f * Mathf.Abs(Mathf.Sin(angle));
		var across = new Vector2(Mathf.Cos(roll), Mathf.Sin(roll));
		// The rim: the coin's edge, seen beside its face as it turns.
		var edge = centre - across * thickness * Mathf.Sign(Mathf.Sin(angle) * facing) * grow;
		DrawSetTransform(edge, roll, new Vector2(squash, 1) * grow);
		DrawCircle(Vector2.Zero, radius, _rim);
		for (int i = 0; i < 6; i++)
		{
			var step = (centre - edge) * (i / 6f);
			DrawSetTransform(edge + step, roll, new Vector2(squash, 1) * grow);
			DrawCircle(Vector2.Zero, radius, _rim.Lerp(_deep, 0.25f));
		}
		DrawSetTransform(centre, roll, new Vector2(squash * (facing >= 0 ? 1 : -1), 1) * grow);
		bool back = facing < 0;
		DrawCircle(Vector2.Zero, radius, back ? _deep.Lerp(_rim, 0.15f) : _deep);
		DrawArc(Vector2.Zero, radius, 0, Mathf.Tau, 96, _metal, radius * 0.11f, true);
		DrawArc(Vector2.Zero, radius * 0.82f, 0, Mathf.Tau, 96, _metal with { A = 0.55f }, 3, true);
		var font = ThemeDB.FallbackFont;
		int size = Mathf.Max(8, (int)(radius * 1.05f));
		var text = font.GetStringSize(_number, HorizontalAlignment.Left, -1, size);
		var at = new Vector2(-text.X / 2, text.Y * 0.32f);
		// A gleam that sweeps across as it turns; the back is a little duller.
		float gleam = back ? 0.1f : 0.35f * pulse;
		DrawString(font, at + new Vector2(3, 3), _number, HorizontalAlignment.Left, -1, size, new Color(0, 0, 0, 0.5f));
		DrawString(font, at, _number, HorizontalAlignment.Left, -1, size, _metal.Lerp(Colors.White, gleam));
		DrawSetTransform(Vector2.Zero, 0, Vector2.One);

		// Whose hand it was, and how to go on.
		void Line(string s, int fontSize, float y, Color colour)
		{
			var w = font.GetStringSize(s, HorizontalAlignment.Left, -1, fontSize).X;
			DrawString(font, new Vector2(Size.X / 2 - w / 2 + 2, y + 2), s, HorizontalAlignment.Left, -1, fontSize, new Color(0, 0, 0, 0.6f));
			DrawString(font, new Vector2(Size.X / 2 - w / 2, y), s, HorizontalAlignment.Left, -1, fontSize, colour);
		}
		Line(_caption, 40, centre.Y + radius * 1.45f + 30, new Color("fff3e0"));
		Line("Tap to continue", 28, centre.Y + radius * 1.45f + 90, _metal with { A = 0.5f + 0.5f * pulse });
	}
}
