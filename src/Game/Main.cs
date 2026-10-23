using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cribbage.Core;
using Godot;
using Renegade.Cards;
using Renegade.Core;
using Renegade.Play;
using Renegade.Ui;

namespace Cribbage.Game;

/// <summary>
/// Cribbage for two to four (four as partnerships: you and Max against Rosie and Lena), a game
/// to 121 on a wooden board standing to your right. Tap your card (or two) and press "To the
/// crib"; the player on the dealer's right cuts the deck and the dealer turns the starter. In the
/// play, tap a card to lay it (cards that would pass 31 are greyed). At the show, each hand is
/// counted aloud and pegged; a hand worth nothing ("nineteen") spins a 19.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private CribbageGame _game = new();
	private CribbageHand? _hand;
	private readonly CribbageAi?[] _ais = new CribbageAi?[4];
	private Board _board = null!;
	private Button _crib = null!;
	private readonly int[] _laid = new int[4];
	private readonly bool[] _goSaid = new bool[4];
	private readonly List<Card3D> _turned = new();
	private List<Card3D> _stock = new();

	protected override string Title => "Cribbage";
	protected override string Hint => "Pick your crib cards, then play";
	protected override int MinPlayers => 2;
	protected override int MaxPlayers => 4;
	protected override string NewGameText => _game.IsOver ? "New game" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_game.IsOver && (_game.Hands > 0 || _hand is { Phase: not CribPhase.Over });

	/// <summary>The deck sits beside the board, out of the way of the play.</summary>
	private static Vector3 StockAt => new(1.0f, CardTable.FeltY, 0.75f);

	/// <summary>The crib sits beside the dealer's place setting (it's their extra hand).</summary>
	private Vector3 CribAt => CribFor(_hand?.Dealer ?? 0);

	/// <summary>
	/// Beside a seat's place setting: on its right, else in front of it, else on its left, whichever is
	/// clear of the board, the middle of the table, the deck and the other place settings (failing all
	/// those, on the far end of its own).
	/// </summary>
	private Vector3 CribFor(int dealer)
	{
		if (dealer == 0)
			return new Vector3(-1.65f, CardTable.FeltY, 0.85f);
		var mat = Table.Pile[dealer];
		var toward = Table.Centre - mat;
		// Seats face straight across the table: their facing is along one axis.
		var f = Mathf.Abs(toward.X) > Mathf.Abs(toward.Z) ? new Vector3(Mathf.Sign(toward.X), 0, 0) : new Vector3(0, 0, Mathf.Sign(toward.Z));
		var r = f.Cross(Vector3.Up);
		float half = f.Z == 0 ? 0.875f : 1.0f;
		bool Clear(Vector3 p)
		{
			if (Mathf.Abs(p.X) > 2.3f || p.X > 1.05f || p.DistanceTo(Table.Centre) < 0.8f || p.DistanceTo(StockAt) < 0.9f)
				return false;
			if (Mathf.Abs(p.X) < 2.15f && p.Z > 0.2f)
				return false; // your place setting
			for (int s = 1; s < Players; s++)
				if (s != dealer && p.DistanceTo(Table.Pile[s]) < 1.25f)
					return false;
			return true;
		}
		foreach (var spot in new[] { mat + r * (half + 0.42f), mat + f * 0.95f, mat - r * (half + 0.42f) })
			if (Clear(spot))
				return spot;
		// Crowded (four players, with the board): on the far end of their own place setting.
		return mat + r * (half - 0.35f);
	}

	/// <summary>Partners share a name in the score: "you and Max".</summary>
	private string SideName(int side) => Players == 4 ? (side == 0 ? "You and Max" : "Rosie and Lena") : NameOf(side);

	protected override void LevelChanged(Difficulty difficulty) => MakeAis(difficulty);

	private void MakeAis(Difficulty difficulty)
	{
		for (int s = 0; s < Players; s++)
			_ais[s] = s > 0 ? new CribbageAi(s, difficulty, new Random(Random.Next())) : Demo ? new CribbageAi(0, Difficulty.Medium, new Random(Random.Next())) : null;
	}

	protected override void AddSettings()
	{
		// The board stands lengthways to your right, clear of the cards in play.
		_board = new Board { Tracks = Players == 4 ? 2 : Players, Position = new Vector3(1.85f, CardTable.FeltY, -0.65f) };
		AddChild(_board);
		_crib = Hud.AddButton("To the crib", true, Control.LayoutPreset.CenterBottom, -170, 170, -192, -128, 28);
		_crib.Visible = false;
		_crib.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: CribPhase.Discard } hand && Fans[0].RaisedCards.Count == hand.DiscardCount)
				Run(Discard(Fans[0].RaisedCards.Select(n => n.Card).ToList()));
		};
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_crib.Visible = false;
		if (_game.IsOver || _game.Hands == 0)
		{
			_game = new CribbageGame(Players);
			_board.Reset();
		}
		MakeAis(Settings.Difficulty);
		_nodes.Clear();
		_turned.Clear();
		Array.Clear(_laid);
		Array.Clear(_goSaid);
		_hand = null;
		SetScore();
		var order = Deck.Shuffled(Random);
		var nodes = await ShuffleOnTable(order);
		if (round != Round)
			return;
		for (int i = 0; i < order.Count; i++)
			_nodes[order[i]] = nodes[i];
		var hand = _game.Deal(order);
		_hand = hand;
		int dealt = hand.Dealt.Count;
		await DealRound(nodes.Take(dealt).ToList(), Enumerable.Range(0, dealt).Select(i => (hand.Dealer + 1 + i) % Players).ToList(), spacing: 0.1f);
		if (round != Round)
			return;
		var rest = nodes.Skip(dealt).ToList();
		// Three players: one card goes straight to the crib.
		if (hand.CribCard is { } extra)
		{
			await Await(Dealer.Deal(_nodes[extra], CribAt, 0, delay: 0));
			rest.Remove(_nodes[extra]);
		}
		// The rest becomes the stock, off to one side.
		_stock = rest;
		Tween? last = null;
		for (int i = 0; i < _stock.Count; i++)
			last = CardMotion.Slide(_stock[i], Dealer.InStack(StockAt, _stock.Count - 1 - i), 0.25f);
		await Await(last);
		await PickUpAll(p => hand.Hand(p).Select(c => _nodes[c]));
		Hud.SetRestartVisible(true);
		News($"{NameOf(hand.Dealer)} {(hand.Dealer == 0 ? "deal" : "deals")}: the crib is {(hand.Dealer == 0 ? "yours" : Names[hand.Dealer] + "'s")}.");
		if (Demo)
		{
			await Discard(_ais[0]!.ChooseDiscard(hand));
			return;
		}
		Busy = false;
		_crib.Visible = true;
		_crib.Disabled = true;
		string what = hand.DiscardCount == 2 ? "two cards" : "a card";
		Hud.SetStatus($"Choose {what} for {(hand.Dealer == 0 ? "your" : Names[hand.Dealer] + "'s")} crib: tap {(hand.DiscardCount == 2 ? "them" : "it")}.", Bright);
	}

	private async Task Discard(IReadOnlyList<Card> mine)
	{
		var hand = _hand!;
		Busy = true;
		_crib.Visible = false;
		Fans[0].LowerAll();
		var choices = new List<(int Player, IReadOnlyList<Card> Cards)> { (0, mine) };
		for (int p = 1; p < Players; p++)
			choices.Add((p, _ais[p]!.ChooseDiscard(hand)));
		int cutAt = Random.Next(4, Math.Max(5, hand.Stock.Count - 4));
		List<Pegged> heels = new();
		foreach (var (p, cards) in choices)
			heels = hand.Discard(p, cards, cutAt);
		// Everyone slides their cards face down onto the crib.
		var moves = new List<Task>();
		int k = hand.CribCard != null ? 1 : 0;
		foreach (var (p, cards) in choices)
		{
			var nodes = cards.Select(c => _nodes[c]).ToList();
			foreach (var node in nodes)
				Fans[p].Remove(node);
			Fans[p].Settle();
			var targets = nodes.Select(_ => Dealer.InStack(CribAt, k++, yaw: 0.1f * k)).ToList();
			moves.Add(Hands[p].Carry(nodes, targets, seconds: 0.4f, lift: 0.25f));
		}
		await Task.WhenAll(moves);
		await Cut(cutAt);
		await Pegs(heels);
		await NextPlay();
	}

	/// <summary>
	/// The cut: the player on the dealer's right lifts the top of the stock aside; the dealer turns
	/// up the top card of what's left; the cut goes back and the starter goes on top, face up.
	/// </summary>
	private async Task Cut(int cutAt)
	{
		var hand = _hand!;
		int cutter = hand.Cutter, dealer = hand.Dealer;
		var starter = _nodes[hand.Starter!.Value];
		var stock = hand.Stock.Select(c => _nodes[c]).ToList(); // top first
		var top = stock.Take(cutAt).ToList();
		Say(cutter, "Cut.", dealer);
		// Top part aside, towards the cutter.
		var aside = StockAt + new Vector3(0, 0, 0.62f);
		await Hands[cutter].Carry(top.AsEnumerable().Reverse().ToList(), top.AsEnumerable().Reverse().Select((_, i) => Dealer.InStack(aside, i)).ToList(), seconds: 0.35f, lift: 0.2f);
		// The dealer turns the top of the lower part face up, beside it.
		var shown = FaceUpAt(StockAt + new Vector3(-0.68f, 0.01f, 0.62f), 0.1f);
		await Hands[dealer].Carry(new[] { starter }, new[] { shown }, seconds: 0.4f, lift: 0.3f);
		News($"The starter is the {Describe(hand.Starter.Value)}.");
		// The cut goes back on the pack, and the starter on top, face up.
		var under = stock.Skip(cutAt + 1).ToList();
		var back = top.Concat(under).ToList();
		Tween? last = null;
		for (int i = 0; i < back.Count; i++)
			last = CardMotion.Slide(back[i], Dealer.InStack(StockAt, back.Count - 1 - i), 0.25f);
		await Await(last);
		await Await(CardMotion.Fly(starter, new Transform3D(Card3D.FaceUp(0.1f), StockAt + Vector3.Up * (Dealer.CardThickness * (back.Count + 2))), 0.35f, lift: 0.15f, landed: () => Sounds.PlaySlap(StockAt)));
	}

	/// <summary>Moves the pegs for whatever was scored, and says it.</summary>
	private async Task Pegs(IEnumerable<Pegged> scored)
	{
		foreach (var peg in scored)
		{
			Say(peg.Player, $"{peg.Why} for {peg.Points}.");
			await Await(_board.Peg(_hand!.Side(peg.Player), _hand.ScoreOf(peg.Player)));
			SetScore();
			await Pause(0.4f);
		}
	}

	private async Task NextPlay()
	{
		var hand = _hand!;
		int round = Round;
		SetScore();
		if (hand.Phase == CribPhase.Over)
		{
			EndHand();
			return;
		}
		if (hand.Phase == CribPhase.Show)
		{
			await Show();
			return;
		}
		int p = hand.Current;
		ShowTurn(p);
		if (_ais[p] == null)
		{
			DimUnplayable(hand.Playable());
			Busy = false;
			Hud.SetStatus($"The count is {hand.Count}. Your card.", Bright);
			return;
		}
		Busy = true;
		Hud.SetStatus($"{(p == 0 ? "Your" : Names[p] + "'s")} turn. The count is {hand.Count}.", Muted);
		await Pause(0.7f);
		if (round != Round)
			return;
		await Lay(p, _ais[p]!.ChoosePlay(hand));
	}

	/// <summary>Where a player's cards go in the play: in a row in front of them, towards the middle.</summary>
	private Vector3 LaySpot(int p, int k)
	{
		var toward = Table.Centre - Table.Pile[p];
		toward.Y = 0;
		var dir = toward.Normalized();
		var right = dir.Cross(Vector3.Up).Normalized();
		float reach = p == 0 ? 0.95f : Players == 2 ? 0.95f : 0.45f;
		return Table.Pile[p] + dir * reach + right * (-0.36f + k * 0.22f) + Vector3.Up * (CardTable.FeltY + 0.002f * (k + 1));
	}

	private float Facing(int p)
	{
		var toward = Table.Pile[p] - Table.Centre;
		return p == 0 ? 0 : Mathf.Atan2(toward.X, toward.Z);
	}

	private async Task Lay(int p, Card card)
	{
		var hand = _hand!;
		Busy = true;
		DimUnplayable(null);
		int count = hand.Count + Score.Value(card);
		var scored = hand.Play(card);
		var node = _nodes[card];
		var at = LaySpot(p, _laid[p]++);
		await PlayToTable(p, node, FaceUpAt(at, Facing(p) + ((float)Random.NextDouble() - 0.5f) * 0.1f));
		_turned.Add(node);
		Say(p, $"{Card.RankName(card.Rank)}: {count}.");
		await Pegs(scored);
		// "Go": whoever couldn't play says so (once per count).
		for (int q = 0; q < Players; q++)
			if (hand.Phase == CribPhase.Pegging && hand.SaidGo(q) && !_goSaid[q])
			{
				_goSaid[q] = true;
				Say(q, "Go.");
				await Pause(0.5f);
			}
		// The count went back to zero: the cards played so far are turned face down.
		if (hand.Sequence.Count == 0 && hand.Phase != CribPhase.Over)
		{
			Array.Clear(_goSaid);
			await Pause(0.4f);
			foreach (var turned in _turned)
				CardMotion.Slide(turned, FaceDownAt(turned.GlobalPosition, 0.05f), 0.2f);
			_turned.Clear();
			News("The count starts again from zero.");
			await Pause(0.4f);
		}
		await NextPlay();
	}

	private async Task Show()
	{
		var hand = _hand!;
		ShowTurn(-1);
		var shows = hand.Show();
		foreach (var (player, what, items, points) in shows)
		{
			// Turn the cards being counted face up, together, in front of their player (the crib in the middle).
			var cards = what == "crib" ? hand.Crib : hand.Kept(player);
			for (int i = 0; i < cards.Count; i++)
			{
				var at = what == "crib" ? Table.Centre + new Vector3(-0.36f + i * 0.24f, CardTable.FeltY + 0.01f + 0.002f * i, 0.55f) : LaySpot(player, i) + Vector3.Up * 0.01f;
				CardMotion.Fly(_nodes[cards[i]], FaceUpAt(at, what == "crib" ? 0 : Facing(player)), 0.35f, lift: 0.2f);
			}
			await Pause(0.6f);
			string whose = what == "crib" ? (player == 0 ? "Your crib" : $"{Names[player]}'s crib") : (player == 0 ? "Your hand" : $"{Names[player]}'s hand");
			// Counted aloud a phrase at a time, the way players say it.
			var phrases = Score.Phrases(items);
			Hud.SetStatus($"Counting {(player == 0 ? whose.ToLowerInvariant() : whose)}…", Muted);
			if (points == 0)
			{
				Hud.SetStatus($"{whose}: nineteen (nothing).", Bright);
				Say(player, what == "crib" ? "Nineteen in the crib!" : "Nineteen!");
				Nineteen(what == "crib" ? Table.Centre + new Vector3(0, 0, 0.55f) : LaySpot(player, 1.5f));
			}
			else
			{
				Say(player, $"{(what == "crib" ? "In the crib: " : "")}{string.Join(", ", phrases)}.");
				// Give the count time to be read before it's pegged.
				await Pause(0.35f * phrases.Count);
				Hud.SetStatus($"{whose}: {points}.", Bright);
			}
			await Await(_board.Peg(hand.Side(player), hand.ScoreOf(player)));
			SetScore();
			await Pause(2.0f);
			if (hand.GameWon)
				break;
		}
		EndHand();
	}

	private Vector3 LaySpot(int p, float k) => LaySpot(p, (int)k) + (LaySpot(p, (int)k + 1) - LaySpot(p, (int)k)) * (k - (int)k);

	/// <summary>
	/// "Nineteen": the score you can't make, said of a hand worth nothing. A gold ring with 19 in it
	/// rises over the cards and spins.
	/// </summary>
	private void Nineteen(Vector3 at)
	{
		var sign = new Node3D { Position = at + Vector3.Up * 0.5f };
		AddChild(sign);
		sign.AddChild(new MeshInstance3D
		{
			Mesh = new TorusMesh { InnerRadius = 0.24f, OuterRadius = 0.29f, Rings = 32, RingSegments = 12 },
			RotationDegrees = new Vector3(90, 0, 0),
			MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("e8b33a"), Metallic = 0.7f, Roughness = 0.25f, EmissionEnabled = true, Emission = new Color("5a3d08") },
		});
		sign.AddChild(new Label3D
		{
			Text = "19",
			FontSize = 120,
			PixelSize = 0.0035f,
			Modulate = new Color("fff3e0"),
			OutlineSize = 18,
			OutlineModulate = new Color("5a2a10"),
			DoubleSided = true,
		});
		// Face the camera, then spin round twice and fade away.
		sign.LookAt(Table.Camera.GlobalPosition, Vector3.Up);
		sign.RotateObjectLocal(Vector3.Up, Mathf.Pi);
		sign.Scale = Vector3.One * 0.2f;
		var tween = sign.CreateTween();
		tween.TweenProperty(sign, "scale", Vector3.One, 0.3f).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
		var spin = sign.CreateTween();
		float turned = 0;
		spin.TweenMethod(Callable.From<float>(t =>
		{
			sign.RotateObjectLocal(Vector3.Up, t - turned);
			turned = t;
		}), 0f, Mathf.Tau * 2, 1.6f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
		spin.TweenProperty(sign, "scale", Vector3.One * 0.01f, 0.25f);
		spin.TweenCallback(Callable.From(sign.QueueFree));
	}

	private void EndHand()
	{
		var hand = _hand!;
		_game.Finished();
		SetScore();
		if (_game.IsOver)
		{
			int w = _game.Winner;
			Sparkle.Burst(this, _board.ToGlobal(_board.Hole(w, 121)) + Vector3.Up * 0.2f);
			string skunk = _game.DoubleSkunk ? " A double skunk!" : _game.Skunk ? " A skunk!" : "";
			int runnerUp = _game.Scores.Where((_, i) => i != w).Max();
			bool us = w == 0;
			string who = SideName(w);
			Finish(us ? $"{who} peg out and win, 121 to {runnerUp}!{skunk}" : $"{who} {(Players == 4 ? "peg" : "pegs")} out and {(Players == 4 ? "win" : "wins")}, 121 to {runnerUp}.{skunk}",
				us ? 0 : 1, _game.Skunk ? hand.Kept(w).Select(c => _nodes[c]) : null);
		}
		else
		{
			int next = (hand.Dealer + 1) % Players;
			Finish($"{Standing()}. {(next == 0 ? "You deal" : $"{Names[next]} deals")} next.", -1);
		}
	}

	private string Standing() => string.Join(", ", Enumerable.Range(0, _game.Scores.Length).Select(s => $"{SideName(s)} {_game.Scores[s]}"));

	private void SetScore()
	{
		var scores = _game.Scores;
		string line = Players == 4
			? $"To 121: us {scores[0]} · them {scores[1]}"
			: $"To 121: {string.Join(" · ", Enumerable.Range(0, Players).Select(s => $"{(s == 0 ? "you" : Names[s])} {scores[s]}"))}";
		Hud.SetScore($"{line}   ·   Wins {Wins.First}–{Wins.Second}");
	}

	private static string Describe(Card card) => $"{Card.RankName(card.Rank).ToLowerInvariant()} of {card.Suit.ToString().ToLowerInvariant()}";

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_hand is not { } hand || Fans[0].At(origin, direction) is not { } card)
			return;
		if (hand.Phase == CribPhase.Discard)
		{
			bool raise = !Fans[0].IsRaised(card);
			if (raise && Fans[0].RaisedCards.Count >= hand.DiscardCount)
				return;
			Fans[0].RaiseCard(card, raise);
			Fans[0].Settle(0.12f);
			_crib.Disabled = Fans[0].RaisedCards.Count != hand.DiscardCount;
			return;
		}
		if (hand.Phase != CribPhase.Pegging || hand.Current != 0)
			return;
		if (!hand.Playable().Contains(card.Card))
		{
			Hud.SetStatus($"That would take the count past 31 (it's {hand.Count}).", Bright);
			return;
		}
		Hud.HideHint();
		Run(Lay(0, card.Card));
	}
}
