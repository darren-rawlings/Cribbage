using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Cribbage.Core;

public enum CribPhase { Discard, Pegging, Show, Over }

/// <summary>Points scored, by whom and why.</summary>
public sealed record Pegged(int Player, int Points, string Why);

/// <summary>
/// One hand of cribbage for two, three or four (the Bicycle rules). Two players get six cards
/// each and put two into the crib; three get five each, put one in, and the crib gets one more
/// from the top of the deck; four play as two partnerships (across the table), get five each and
/// put one in. The crib belongs to the dealer. The player on the dealer's right cuts and the dealer
/// turns the starter: a jack scores the dealer 2 ("his heels"). In the play, starting left of the
/// dealer, players lay cards in turn, keeping the count at 31 or less and pegging for fifteens,
/// thirty-ones, pairs and runs. A player who can't play says "go"; the others play on if they can,
/// and the last card under 31 scores 1. The count then starts again with the next player. Then the
/// hands are shown and counted round the table from the dealer's left, the dealer last, then the
/// crib. Scores are pegged as they happen (partners share one score); the first to 121 wins at once.
/// </summary>
public sealed class CribbageHand
{
	public const int Target = 121;

	private readonly List<Card>[] _hands;
	private readonly List<Card>[] _kept;
	private readonly List<Card>?[] _discards;
	private readonly List<Card> _crib = new();
	private readonly List<Card> _sequence = new();
	private readonly int[] _scores;
	private readonly bool[] _said;
	private readonly IReadOnlyList<Card> _deck;
	private readonly int _dealtCount;

	/// <summary>Deals from a deck (top first), one at a time round the table from the dealer's left.</summary>
	public CribbageHand(IReadOnlyList<Card> deck, int dealer, int[] scores, int players = 2)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Cribbage needs a full deck.", nameof(deck));
		if (players is < 2 or > 4)
			throw new ArgumentOutOfRangeException(nameof(players));
		_deck = deck;
		Players = players;
		Dealer = dealer;
		_scores = scores;
		_hands = Enumerable.Range(0, players).Select(_ => new List<Card>()).ToArray();
		_kept = Enumerable.Range(0, players).Select(_ => new List<Card>()).ToArray();
		_discards = new List<Card>?[players];
		_said = new bool[players];
		int dealt = HandSize * players;
		for (int i = 0; i < dealt; i++)
			_hands[(dealer + 1 + i) % players].Add(deck[i]);
		Dealt = deck.Take(dealt).ToList();
		_dealtCount = dealt;
		// Three players: the crib gets a card straight from the deck.
		if (players == 3)
		{
			CribCard = deck[dealt];
			_dealtCount++;
		}
	}

	public int Players { get; }

	/// <summary>Four players are two partnerships (0 and 2, 1 and 3); otherwise everyone plays alone.</summary>
	public int Sides => Players == 4 ? 2 : Players;

	/// <summary>Which score a player pegs on.</summary>
	public int Side(int player) => Players == 4 ? player % 2 : player;

	/// <summary>Six cards each for two players, five for three or four.</summary>
	public int HandSize => Players == 2 ? 6 : 5;

	/// <summary>How many cards each player puts into the crib.</summary>
	public int DiscardCount => Players == 2 ? 2 : 1;

	public int Dealer { get; }

	/// <summary>The player on the dealer's left: leads the play and is counted first.</summary>
	public int Pone => (Dealer + 1) % Players;

	/// <summary>The player on the dealer's right, who cuts for the starter (with two players, the other one).</summary>
	public int Cutter => (Dealer - 1 + Players) % Players;

	public IReadOnlyList<Card> Dealt { get; }

	/// <summary>With three players, the card dealt straight into the crib.</summary>
	public Card? CribCard { get; }

	public CribPhase Phase { get; private set; } = CribPhase.Discard;
	public Card? Starter { get; private set; }
	public int Current { get; private set; }
	public IReadOnlyList<Card> Hand(int player) => _hands[player];

	/// <summary>The four cards a player kept (for the show), whatever's left in their hand.</summary>
	public IReadOnlyList<Card> Kept(int player) => _kept[player];

	public IReadOnlyList<Card> Crib => _crib;
	public IReadOnlyList<Card> Sequence => _sequence;
	public int Count => _sequence.Sum(Score.Value);

	/// <summary>A player's score (their side's, with partners).</summary>
	public int ScoreOf(int player) => _scores[Side(player)];

	public bool GameWon => _scores.Max() >= Target;

	/// <summary>The winning side (or -1).</summary>
	public int Winner => Array.FindIndex(_scores, s => s >= Target);

	/// <summary>The cards left under those dealt (for the cut); the starter comes from here.</summary>
	public IReadOnlyList<Card> Stock => _deck.Skip(_dealtCount).ToList();

	public bool HasDiscarded(int player) => _discards[player] != null;

	private List<Pegged> Add(int player, int points, string why, List<Pegged> into)
	{
		if (points <= 0 || GameWon)
			return into;
		_scores[Side(player)] = Math.Min(Target, _scores[Side(player)] + points);
		into.Add(new Pegged(player, points, why));
		if (GameWon)
			Phase = CribPhase.Over;
		return into;
	}

	/// <summary>
	/// A player puts their card (or two) into the crib. Once everyone has, the starter is cut (the
	/// given position in the stock) and the play begins. Returns his heels, if the starter is a jack.
	/// </summary>
	public List<Pegged> Discard(int player, IReadOnlyList<Card> cards, int cutAt = 0)
	{
		if (Phase != CribPhase.Discard || _discards[player] != null)
			throw new InvalidOperationException("Not discarding now.");
		if (cards.Count != DiscardCount || cards.Distinct().Count() != DiscardCount || cards.Any(c => !_hands[player].Contains(c)))
			throw new ArgumentException($"Put {DiscardCount} card{(DiscardCount > 1 ? "s" : "")} from your hand in the crib.", nameof(cards));
		_discards[player] = cards.ToList();
		foreach (var card in cards)
			_hands[player].Remove(card);
		var events = new List<Pegged>();
		if (_discards.Any(d => d == null))
			return events;
		if (CribCard is { } extra)
			_crib.Add(extra);
		for (int k = 1; k <= Players; k++)
			_crib.AddRange(_discards[(Dealer + k) % Players]!);
		for (int p = 0; p < Players; p++)
			_kept[p].AddRange(_hands[p]);
		var stock = Stock;
		Starter = stock[Math.Clamp(cutAt, 0, stock.Count - 1)];
		Phase = CribPhase.Pegging;
		Current = Pone;
		if (Starter.Value.Rank == Card.Jack)
			Add(Dealer, 2, "His heels", events);
		return events;
	}

	/// <summary>The cards the current player can lay without passing 31.</summary>
	public List<Card> Playable() => Playable(Current);

	public List<Card> Playable(int player) => _hands[player].Where(c => Count + Score.Value(c) <= 31).ToList();

	/// <summary>Lays a card in the play; returns what it pegged (and any "go" or last card that follows).</summary>
	public List<Pegged> Play(Card card)
	{
		if (Phase != CribPhase.Pegging)
			throw new InvalidOperationException("Not pegging now.");
		if (!Playable().Contains(card))
			throw new InvalidOperationException($"{card} would take the count past 31.");
		var events = new List<Pegged>();
		foreach (var item in Score.Peg(_sequence, card))
			Add(Current, item.Points, item.Name, events);
		_hands[Current].Remove(card);
		_sequence.Add(card);
		int player = Current;
		if (Count == 31)
		{
			Reset(next: (player + 1) % Players);
			Finish();
			return events;
		}
		Advance(player, events);
		return events;
	}

	/// <summary>
	/// Moves the turn on after a card: to the next player round the table who can play (those
	/// skipped, holding cards, have said "go"); if nobody can, the last card scores 1 and the count
	/// starts again with the next player.
	/// </summary>
	private void Advance(int player, List<Pegged> events)
	{
		for (int k = 1; k <= Players; k++)
		{
			int q = (player + k) % Players;
			if (Playable(q).Count > 0)
			{
				Current = q;
				Finish();
				return;
			}
			if (q != player && _hands[q].Count > 0)
				_said[q] = true;
		}
		Add(player, 1, _hands.All(h => h.Count == 0) ? "Last card" : "Go", events);
		Reset(next: (player + 1) % Players);
		Finish();
	}

	private void Reset(int next)
	{
		_sequence.Clear();
		Array.Clear(_said);
		// The next player with cards leads.
		for (int k = 0; k < Players; k++)
			if (_hands[(next + k) % Players].Count > 0)
			{
				Current = (next + k) % Players;
				return;
			}
		Current = next;
	}

	private void Finish()
	{
		if (Phase == CribPhase.Over)
			return;
		if (_hands.All(h => h.Count == 0))
			Phase = CribPhase.Show;
	}

	/// <summary>Whether a player has said go since the count last started (for showing it).</summary>
	public bool SaidGo(int player) => _said[player];

	/// <summary>
	/// The show: each hand from the dealer's left round to the dealer, then the crib, each with the
	/// starter. Scoring stops the moment someone reaches 121. Returns each count, in order.
	/// </summary>
	public List<(int Player, string What, IReadOnlyList<ScoreItem> Items, int Points)> Show()
	{
		if (Phase != CribPhase.Show)
			throw new InvalidOperationException("Not time to show yet.");
		var shows = new List<(int, string, IReadOnlyList<ScoreItem>, int)>();
		var starter = Starter!.Value;
		var order = Enumerable.Range(1, Players).Select(k => ((Dealer + k) % Players, "hand", (IReadOnlyList<Card>)_kept[(Dealer + k) % Players], false)).ToList();
		order.Add((Dealer, "crib", _crib, true));
		foreach (var (player, what, cards, crib) in order)
		{
			var items = Score.Items(cards, starter, crib);
			int points = items.Sum(i => i.Points);
			shows.Add((player, what, items, points));
			Add(player, points, what, new List<Pegged>());
			if (GameWon)
				break;
		}
		Phase = CribPhase.Over;
		return shows;
	}
}

/// <summary>
/// A game to 121. A side that wins before another reaches 91 has skunked it (a double skunk
/// below 61).
/// </summary>
public sealed class CribbageGame
{
	public CribbageGame(int players = 2)
	{
		Players = players;
		Scores = new int[players == 4 ? 2 : players];
		NextDealer = 1 % players;
	}

	public int Players { get; }
	public int[] Scores { get; }
	public int Hands { get; private set; }
	public int NextDealer { get; private set; }
	public bool IsOver => Scores.Max() >= CribbageHand.Target;

	/// <summary>The winning side (with four players, 0 is you and your partner), or -1.</summary>
	public int Winner => Array.FindIndex(Scores, s => s >= CribbageHand.Target);

	/// <summary>The best score among the losing sides.</summary>
	private int RunnerUp => Scores.Where((_, i) => i != Winner).Max();

	public bool Skunk => IsOver && RunnerUp < 91;
	public bool DoubleSkunk => IsOver && RunnerUp < 61;

	public CribbageHand Deal(IReadOnlyList<Card> deck) => new(deck, NextDealer, Scores, Players);

	public void Finished()
	{
		Hands++;
		NextDealer = (NextDealer + 1) % Players;
	}
}
