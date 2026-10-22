using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;

namespace Cribbage.Core;

/// <summary>One scoring item ("fifteen two", "a pair", "a run of three"), for showing the count.</summary>
public readonly record struct ScoreItem(string Name, int Points, IReadOnlyList<Card> Cards);

/// <summary>
/// Counting a cribbage hand: the four cards with the starter. Every combination adding to 15
/// scores 2; every pair 2 (so three of a kind is 6 and four 12); every run of three or more
/// scores its length (each different run counted, so a double run of three is 8 with its pair);
/// a flush of four in the hand scores 4, or 5 with the starter (a crib only counts a flush of all
/// five); and the jack of the starter's suit in the hand ("his nobs") scores 1.
/// </summary>
public static class Score
{
	/// <summary>A card's counting value: aces 1, picture cards 10.</summary>
	public static int Value(Card card) => Math.Min(card.Rank, 10);

	public static int Hand(IReadOnlyList<Card> hand, Card starter, bool crib = false) => Items(hand, starter, crib).Sum(i => i.Points);

	public static List<ScoreItem> Items(IReadOnlyList<Card> hand, Card? starter, bool crib = false)
	{
		var cards = starter is { } s ? hand.Append(s).ToList() : hand.ToList();
		var items = new List<ScoreItem>();
		int n = cards.Count;

		// Fifteens: every subset adding to 15.
		for (int mask = 1; mask < 1 << n; mask++)
		{
			var subset = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => cards[i]).ToList();
			if (subset.Count >= 2 && subset.Sum(Value) == 15)
				items.Add(new ScoreItem("Fifteen", 2, subset));
		}
		// Pairs.
		for (int i = 0; i < n; i++)
			for (int j = i + 1; j < n; j++)
				if (cards[i].Rank == cards[j].Rank)
					items.Add(new ScoreItem("Pair", 2, new[] { cards[i], cards[j] }));
		// Runs: the longest length that has any run; count every combination of that length.
		for (int length = n; length >= 3; length--)
		{
			var runs = new List<List<Card>>();
			for (int mask = 1; mask < 1 << n; mask++)
			{
				var subset = Enumerable.Range(0, n).Where(i => (mask & (1 << i)) != 0).Select(i => cards[i]).ToList();
				if (subset.Count != length)
					continue;
				var ranks = subset.Select(c => c.Rank).OrderBy(r => r).ToList();
				if (ranks.Zip(ranks.Skip(1)).All(p => p.Second == p.First + 1))
					runs.Add(subset);
			}
			if (runs.Count > 0)
			{
				foreach (var run in runs)
					items.Add(new ScoreItem($"Run of {length}", length, run.OrderBy(c => c.Rank).ToList()));
				break;
			}
		}
		// Flush.
		if (hand.Count == 4 && hand.Select(c => c.Suit).Distinct().Count() == 1)
		{
			bool five = starter is { } st && st.Suit == hand[0].Suit;
			if (five)
				items.Add(new ScoreItem("Flush of five", 5, cards));
			else if (!crib)
				items.Add(new ScoreItem("Flush", 4, hand.ToList()));
		}
		// His nobs.
		if (starter is { } top)
			foreach (var card in hand.Where(c => c.Rank == Card.Jack && c.Suit == top.Suit))
				items.Add(new ScoreItem("His nobs", 1, new[] { card }));
		return items;
	}

	/// <summary>How the count is said aloud, a phrase per item: "fifteen 2", "fifteen 4", "a pair is 6", "a run of three is 9".</summary>
	public static List<string> Phrases(IReadOnlyList<ScoreItem> items)
	{
		var phrases = new List<string>();
		int total = 0;
		foreach (var item in items)
		{
			total += item.Points;
			string what = item.Name switch
			{
				"Fifteen" => $"fifteen {total}",
				"Pair" => $"a pair is {total}",
				"His nobs" => $"his nobs is {total}",
				"Flush" or "Flush of five" => $"a flush is {total}",
				_ when item.Name.StartsWith("Run of ") => $"a run of {Word(item.Points)} is {total}",
				_ => $"{item.Name.ToLowerInvariant()} is {total}",
			};
			phrases.Add(what);
		}
		return phrases;
	}

	private static string Word(int n) => n switch { 3 => "three", 4 => "four", 5 => "five", _ => n.ToString() };

	/// <summary>The count in one line (the last few phrases, so it fits).</summary>
	public static string Say(IReadOnlyList<ScoreItem> items)
	{
		if (items.Count == 0)
			return "nineteen (nothing)";
		var phrases = Phrases(items);
		return string.Join(", ", phrases.Skip(Math.Max(0, phrases.Count - 3)));
	}

	/// <summary>
	/// The points for playing a card in the pegging, given the cards already played since the
	/// count last went back to zero: 15 or 31 score 2; a pair with the cards just before scores
	/// 2, three of a kind 6, four 12; a run among the most recent cards scores its length.
	/// </summary>
	public static List<ScoreItem> Peg(IReadOnlyList<Card> sequence, Card card)
	{
		var items = new List<ScoreItem>();
		var all = sequence.Append(card).ToList();
		int count = all.Sum(Value);
		if (count == 15)
			items.Add(new ScoreItem("Fifteen", 2, all));
		if (count == 31)
			items.Add(new ScoreItem("Thirty-one", 2, all));
		int same = 1;
		for (int i = all.Count - 2; i >= 0 && all[i].Rank == card.Rank; i--)
			same++;
		if (same >= 2)
			items.Add(new ScoreItem(same switch { 2 => "Pair", 3 => "Pair royal", _ => "Double pair royal" }, same switch { 2 => 2, 3 => 6, _ => 12 }, all.Skip(all.Count - same).ToList()));
		for (int length = all.Count; length >= 3; length--)
		{
			var last = all.Skip(all.Count - length).Select(c => c.Rank).OrderBy(r => r).ToList();
			if (last.Zip(last.Skip(1)).All(p => p.Second == p.First + 1))
			{
				items.Add(new ScoreItem($"Run of {length}", length, all.Skip(all.Count - length).ToList()));
				break;
			}
		}
		return items;
	}
}
