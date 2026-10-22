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
/// Cribbage against Rosie, a game to 121 on a wooden board with pegs. Tap two cards and press
/// "To the crib"; then in the play, tap a card to lay it (the count is shown; cards that would
/// pass 31 are greyed). At the show, each hand is counted aloud and pegged.
/// </summary>
public partial class Main : CardGame
{
	private readonly Dictionary<Card, Card3D> _nodes = new();
	private CribbageGame _game = new();
	private CribbageHand? _hand;
	private CribbageAi _ai = null!;
	private Board _board = null!;
	private Button _crib = null!;
	private readonly int[] _laid = new int[2];
	private readonly List<Card3D> _turned = new();

	protected override string Title => "Cribbage";
	protected override string Hint => "Pick two for the crib, then play";
	protected override string NewGameText => _game.IsOver ? "New game" : "Next hand";
	protected override IReadOnlyList<TutorialPage> TutorialPages => Tutorial.Pages;
	protected override bool InProgress => !_game.IsOver && (_game.Hands > 0 || _hand is { Phase: not CribPhase.Over });

	private Vector3 StockAt => Table.Centre + new Vector3(-1.45f, 0, -0.05f);
	private Vector3 CribAt => Table.Centre + new Vector3(1.45f, 0, -0.05f);

	protected override void LevelChanged(Difficulty difficulty) => _ai = new CribbageAi(1, difficulty, Random);

	protected override void AddSettings()
	{
		_board = new Board { Position = new Vector3(0, CardTable.FeltY, -1.45f) };
		AddChild(_board);
		_crib = Hud.AddButton("To the crib", true, Control.LayoutPreset.CenterBottom, -170, 170, -192, -128, 28);
		_crib.Visible = false;
		_crib.Pressed += () =>
		{
			if (!Busy && _hand is { Phase: CribPhase.Discard } && Fans[0].RaisedCards.Count == 2)
				Run(Discard(Fans[0].RaisedCards.Select(n => n.Card).ToList()));
		};
	}

	protected override async Task NewRound()
	{
		int round = Round;
		_crib.Visible = false;
		if (_game.IsOver)
		{
			_game = new CribbageGame();
			_board.Reset();
		}
		_ai = new CribbageAi(1, Settings.Difficulty, new Random(Random.Next()));
		_nodes.Clear();
		_turned.Clear();
		Array.Clear(_laid);
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
		await DealRound(nodes.Take(12).ToList(), Enumerable.Range(0, 12).Select(i => (hand.Pone + i) % 2).ToList(), spacing: 0.12f);
		if (round != Round)
			return;
		// The rest becomes the stock, off to one side.
		var stock = nodes.Skip(12).ToList();
		Tween? last = null;
		for (int i = 0; i < stock.Count; i++)
			last = CardMotion.Slide(stock[i], Dealer.InStack(StockAt, stock.Count - 1 - i), 0.25f);
		await Await(last);
		await PickUpAll(p => hand.Hand(p).Select(c => _nodes[c]));
		Hud.SetRestartVisible(true);
		if (Demo)
		{
			await Discard(new CribbageAi(0, Difficulty.Medium, Random).ChooseDiscard(hand));
			return;
		}
		Busy = false;
		_crib.Visible = true;
		_crib.Disabled = true;
		Hud.SetStatus($"Choose two cards for {(hand.Dealer == 0 ? "your" : "Rosie's")} crib: tap them.", Bright);
	}

	private async Task Discard(IReadOnlyList<Card> mine)
	{
		var hand = _hand!;
		Busy = true;
		_crib.Visible = false;
		Fans[0].LowerAll();
		var theirs = _ai.ChooseDiscard(hand);
		hand.Discard(0, mine);
		var heels = hand.Discard(1, theirs, Random.Next(hand.Stock.Count - 4) + 2);
		// Both slide two cards face down onto the crib.
		var moves = new List<Task>();
		int k = 0;
		foreach (var (p, cards) in new[] { (0, mine), (1, (IReadOnlyList<Card>)theirs) })
		{
			var nodes = cards.Select(c => _nodes[c]).ToList();
			foreach (var node in nodes)
				Fans[p].Remove(node);
			Fans[p].Settle();
			var targets = nodes.Select(_ => Dealer.InStack(CribAt, k++, yaw: 0.1f * k)).ToList();
			moves.Add(Hands[p].Carry(nodes, targets, seconds: 0.4f, lift: 0.25f));
		}
		await Task.WhenAll(moves);
		// The cut: the starter is turned face up on the stock.
		var starter = _nodes[hand.Starter!.Value];
		Hud.SetStatus(hand.Pone == 0 ? "You cut; Rosie turns the starter." : "Rosie cuts; you turn the starter.", Muted);
		await Await(CardMotion.Fly(starter, FaceUpAt(StockAt + new Vector3(0, 0.25f, 0), 0.15f), 0.45f, lift: 0.3f, landed: () => Sounds.PlaySlap(StockAt)));
		await Pegs(heels);
		await NextPlay();
	}

	/// <summary>Moves the pegs for whatever was scored, and says it.</summary>
	private async Task Pegs(IEnumerable<Pegged> scored)
	{
		foreach (var peg in scored)
		{
			string who = peg.Player == 0 ? "You" : "Rosie";
			Say(peg.Player == 1 ? 1 : 0, $"{peg.Why} for {peg.Points}");
			Hud.SetStatus($"{who}: {peg.Why.ToLowerInvariant()} for {peg.Points}.", Bright);
			await Await(_board.Peg(peg.Player, _hand!.ScoreOf(peg.Player)));
			SetScore();
			await Pause(0.4f);
		}
		SayNone();
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
		if (p == 0 && !Demo)
		{
			DimUnplayable(hand.Playable());
			Busy = false;
			Hud.SetStatus($"The count is {hand.Count}. Your card.", Bright);
			return;
		}
		Busy = true;
		await Pause(0.7f);
		if (round != Round)
			return;
		var ai = p == 1 ? _ai : new CribbageAi(0, Difficulty.Medium, Random);
		await Lay(p, ai.ChoosePlay(hand));
	}

	private async Task Lay(int p, Card card)
	{
		var hand = _hand!;
		Busy = true;
		DimUnplayable(null);
		int before = hand.Sequence.Count;
		var scored = hand.Play(card);
		var node = _nodes[card];
		// Each player lays their cards in a row in front of them.
		var at = new Vector3(-0.85f + _laid[p] * 0.24f, CardTable.FeltY + 0.002f * _laid[p], p == 0 ? 0.25f : -0.85f);
		_laid[p]++;
		await PlayToTable(p, node, FaceUpAt(at, ((float)Random.NextDouble() - 0.5f) * 0.1f));
		_turned.Add(node);
		Hud.SetStatus($"{(p == 0 ? "You" : "Rosie")}: {Card.RankName(card.Rank).ToLowerInvariant()}, {(hand.Sequence.Count == 0 ? "31" : hand.Count.ToString())}.", Muted);
		await Pegs(scored);
		// "Go": the other player couldn't play.
		if (hand.Phase == CribPhase.Pegging && hand.SaidGo(1 - p))
		{
			Say(1 - p == 1 ? 1 : 0, "Go");
			await Pause(0.6f);
			SayNone();
		}
		// The count went back to zero: the cards played so far are turned face down.
		if (hand.Sequence.Count == 0 && hand.Phase != CribPhase.Over)
		{
			await Pause(0.4f);
			foreach (var turned in _turned)
				CardMotion.Slide(turned, FaceDownAt(turned.GlobalPosition, 0.05f), 0.2f);
			_turned.Clear();
			Hud.SetStatus("The count starts again from zero.", Muted);
			await Pause(0.4f);
		}
		await NextPlay();
	}

	private async Task Show()
	{
		var hand = _hand!;
		var starter = hand.Starter!.Value;
		var shows = hand.Show();
		foreach (var (player, what, items, points) in shows)
		{
			// Turn the cards being counted face up, together, in front of their player.
			var cards = what == "crib" ? hand.Crib : hand.Kept(player);
			float z = what == "crib" ? -0.3f : player == 0 ? 0.25f : -0.85f;
			for (int i = 0; i < cards.Count; i++)
				CardMotion.Fly(_nodes[cards[i]], FaceUpAt(new Vector3(-0.85f + i * 0.24f, CardTable.FeltY + 0.01f + 0.002f * i, z)), 0.35f, lift: 0.2f);
			await Pause(0.6f);
			string whose = player == 0 ? (what == "crib" ? "Your crib" : "Your hand") : (what == "crib" ? "Rosie's crib" : "Rosie's hand");
			// Counted aloud a phrase at a time, the way players say it.
			var phrases = Score.Phrases(items);
			if (phrases.Count == 0)
				Hud.SetStatus($"{whose}: nineteen (nothing).", Bright);
			for (int i = 0; i < phrases.Count; i++)
			{
				var shown = phrases.Skip(Math.Max(0, i - 1)).Take(Math.Min(2, i + 1));
				Hud.SetStatus($"{whose}: {string.Join(", ", shown)}", Bright);
				await Pause(0.55f);
			}
			if (phrases.Count > 0)
				Hud.SetStatus($"{whose}: {points}.", Bright);
			Say(player == 1 ? 1 : 0, points == 0 ? "Nineteen!" : $"{points}");
			await Await(_board.Peg(player, hand.ScoreOf(player)));
			SetScore();
			await Pause(2.2f);
			SayNone();
			if (hand.GameWon)
				break;
		}
		EndHand();
	}

	private void EndHand()
	{
		var hand = _hand!;
		_game.Finished();
		SetScore();
		if (_game.IsOver)
		{
			int w = _game.Winner;
			Sparkle.Burst(this, _board.ToGlobal(Board.Hole(w, 121)) + Vector3.Up * 0.2f);
			string skunk = _game.Skunk ? " A skunk!" : "";
			Finish(w == 0 ? $"You peg out and win, 121 to {_game.Scores[1]}!{skunk}" : $"Rosie pegs out and wins, 121 to {_game.Scores[0]}.{skunk}",
				w, _game.Skunk ? hand.Kept(w).Select(c => _nodes[c]) : null);
		}
		else
		{
			Finish($"You {_game.Scores[0]}, Rosie {_game.Scores[1]}. {(hand.Dealer == 0 ? "Rosie" : "You")} deal{(hand.Dealer == 0 ? "s" : "")} next.", -1);
		}
	}

	private void SetScore()
	{
		var scores = _hand != null ? new[] { _hand.ScoreOf(0), _hand.ScoreOf(1) } : _game.Scores;
		Hud.SetScore($"To 121: you {scores[0]} · {scores[1]} Rosie   ·   Wins {Wins.First}–{Wins.Second}");
	}

	protected override void Tapped(Vector3 origin, Vector3 direction, Vector2 screen)
	{
		if (_hand is not { } hand || Fans[0].At(origin, direction) is not { } card)
			return;
		if (hand.Phase == CribPhase.Discard)
		{
			bool raise = !Fans[0].IsRaised(card);
			if (raise && Fans[0].RaisedCards.Count >= 2)
				return;
			Fans[0].RaiseCard(card, raise);
			Fans[0].Settle(0.12f);
			_crib.Disabled = Fans[0].RaisedCards.Count != 2;
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
