using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Cribbage.Core;

public enum CribPhase { Discard, Pegging, Show, Over }

/// <summary>Points scored, by whom and why.</summary>
public sealed record Pegged(int Player, int Points, string Why);

/// <summary>
/// One hand of six-card cribbage for two (the Bicycle rules). Six cards each; both put two
/// face down into the crib, which belongs to the dealer. The non-dealer cuts and the dealer turns
/// the starter: a jack scores the dealer 2 ("his heels"). In the play, starting with the
/// non-dealer, players lay cards alternately, keeping the count at 31 or less and pegging for
/// fifteens, thirty-ones, pairs and runs. A player who can't play says "go"; the other plays on
/// if they can, and the last card under 31 scores 1. The count then starts again. Then the hands
/// are shown and counted: the non-dealer first, then the dealer, then the crib. Scores are
/// pegged as they happen; the first to 121 wins at once.
/// </summary>
public sealed class CribbageHand
{
	public const int Target = 121;

	private readonly List<Card>[] _hands = { new(), new() };
	private readonly List<Card>[] _kept = { new(), new() };
	private readonly List<Card>?[] _discards = new List<Card>?[2];
	private readonly List<Card> _crib = new();
	private readonly List<Card> _sequence = new();
	private readonly List<Card> _played = new();
	private readonly int[] _scores;
	private readonly bool[] _said = new bool[2];
	private readonly IReadOnlyList<Card> _deck;

	/// <summary>Deals six each from a deck (top first), alternately from the non-dealer.</summary>
	public CribbageHand(IReadOnlyList<Card> deck, int dealer, int[] scores)
	{
		if (deck.Count != 52 || deck.Distinct().Count() != 52)
			throw new ArgumentException("Cribbage needs a full deck.", nameof(deck));
		_deck = deck;
		Dealer = dealer;
		_scores = scores;
		for (int i = 0; i < 12; i++)
			_hands[(1 - dealer + i) % 2].Add(deck[i]);
		Dealt = deck.Take(12).ToList();
	}

	public int Dealer { get; }
	public int Pone => 1 - Dealer;
	public IReadOnlyList<Card> Dealt { get; }
	public CribPhase Phase { get; private set; } = CribPhase.Discard;
	public Card? Starter { get; private set; }
	public int Current { get; private set; }
	public IReadOnlyList<Card> Hand(int player) => _hands[player];

	/// <summary>The four cards a player kept (for the show), whatever's left in their hand.</summary>
	public IReadOnlyList<Card> Kept(int player) => _kept[player];

	public IReadOnlyList<Card> Crib => _crib;
	public IReadOnlyList<Card> Sequence => _sequence;
	public int Count => _sequence.Sum(Score.Value);
	public int ScoreOf(int player) => _scores[player];
	public bool GameWon => _scores.Max() >= Target;
	public int Winner => _scores[0] >= Target ? 0 : _scores[1] >= Target ? 1 : -1;

	/// <summary>The cards remaining under the six dealt (for the cut); the starter comes from here.</summary>
	public IReadOnlyList<Card> Stock => _deck.Skip(12).ToList();

	public bool HasDiscarded(int player) => _discards[player] != null;

	private List<Pegged> Add(int player, int points, string why, List<Pegged> into)
	{
		if (points <= 0 || GameWon)
			return into;
		_scores[player] = Math.Min(Target, _scores[player] + points);
		into.Add(new Pegged(player, points, why));
		if (GameWon)
			Phase = CribPhase.Over;
		return into;
	}

	/// <summary>
	/// A player puts two cards into the crib. Once both have, the starter is cut (the given
	/// position in the stock) and the play begins. Returns his heels, if the starter is a jack.
	/// </summary>
	public List<Pegged> Discard(int player, IReadOnlyList<Card> two, int cutAt = 0)
	{
		if (Phase != CribPhase.Discard || _discards[player] != null)
			throw new InvalidOperationException("Not discarding now.");
		if (two.Count != 2 || two.Distinct().Count() != 2 || two.Any(c => !_hands[player].Contains(c)))
			throw new ArgumentException("Put two cards from your hand in the crib.", nameof(two));
		_discards[player] = two.ToList();
		foreach (var card in two)
			_hands[player].Remove(card);
		var events = new List<Pegged>();
		if (_discards.Any(d => d == null))
			return events;
		_crib.AddRange(_discards[Pone]!);
		_crib.AddRange(_discards[Dealer]!);
		for (int p = 0; p < 2; p++)
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
		_played.Add(card);
		int player = Current;
		if (Count == 31)
		{
			Reset(next: 1 - player);
			Finish(events);
			return events;
		}
		Advance(player, events);
		return events;
	}

	/// <summary>Moves the turn on after a card: to the other player if they can play; otherwise "go".</summary>
	private void Advance(int player, List<Pegged> events)
	{
		int other = 1 - player;
		if (Playable(other).Count > 0)
		{
			Current = other;
		}
		else if (Playable(player).Count > 0)
		{
			// The other says "go"; this player plays on.
			_said[other] = true;
			Current = player;
		}
		else
		{
			// Nobody can go on: the last card scores 1 (if the count isn't 31), and the count restarts.
			Add(player, 1, _hands.All(h => h.Count == 0) ? "Last card" : "Go", events);
			Reset(next: other);
		}
		Finish(events);
	}

	private void Reset(int next)
	{
		_sequence.Clear();
		Array.Clear(_said);
		// The player after the last card leads; if they have nothing, the other does.
		Current = _hands[next].Count > 0 ? next : 1 - next;
	}

	private void Finish(List<Pegged> events)
	{
		if (Phase == CribPhase.Over)
			return;
		if (_hands.All(h => h.Count == 0))
			Phase = CribPhase.Show;
	}

	/// <summary>Whether the opponent of the current player has said go (for showing it).</summary>
	public bool SaidGo(int player) => _said[player];

	/// <summary>
	/// The show: the non-dealer's hand, the dealer's, then the crib, each with the starter.
	/// Scoring stops the moment someone reaches 121. Returns each count, in order.
	/// </summary>
	public List<(int Player, string What, IReadOnlyList<ScoreItem> Items, int Points)> Show()
	{
		if (Phase != CribPhase.Show)
			throw new InvalidOperationException("Not time to show yet.");
		var shows = new List<(int, string, IReadOnlyList<ScoreItem>, int)>();
		var starter = Starter!.Value;
		foreach (var (player, what, cards, crib) in new[] { (Pone, "hand", _kept[Pone], false), (Dealer, "hand", _kept[Dealer], false), (Dealer, "crib", (List<Card>)_crib, true) })
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

/// <summary>A game to 121. A player who wins before the other reaches 91 has skunked them.</summary>
public sealed class CribbageGame
{
	public int[] Scores { get; } = new int[2];
	public int Hands { get; private set; }
	public int NextDealer { get; private set; } = 1;
	public bool IsOver => Scores.Max() >= CribbageHand.Target;
	public int Winner => Scores[0] >= CribbageHand.Target ? 0 : Scores[1] >= CribbageHand.Target ? 1 : -1;
	public bool Skunk => IsOver && Scores[1 - Winner] < 91;

	public CribbageHand Deal(IReadOnlyList<Card> deck) => new(deck, NextDealer, Scores);

	public void Finished()
	{
		Hands++;
		NextDealer = 1 - NextDealer;
	}
}
