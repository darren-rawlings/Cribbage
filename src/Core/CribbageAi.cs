using System;
using System.Collections.Generic;
using System.Linq;
using Renegade.Cards;
using Renegade.Core;

namespace Cribbage.Core;

/// <summary>
/// The computer's cribbage. Discarding: Easy throws two at random; Medium keeps the four that
/// score most on their own; Hard keeps the four worth most on average over every possible
/// starter, adding what the two thrown are worth to its own crib (or taking it off, in yours).
/// Pegging: Easy plays any card it can; Medium takes the most points now and avoids leaving
/// the count on 5 or 21 (where a ten makes 15 or 31) or on a card you could pair; Hard also
/// weighs your best reply among the cards it can't see.
/// </summary>
public sealed class CribbageAi
{
	private readonly Random _random;

	public CribbageAi(int player, Difficulty difficulty, Random random)
	{
		Player = player;
		Difficulty = difficulty;
		_random = random;
	}

	public int Player { get; }
	public Difficulty Difficulty { get; }

	/// <summary>A rough value of two cards thrown into a crib (fives, pairs, fifteens and near-runs help it).</summary>
	private static double CribValue(Card a, Card b)
	{
		double v = 0;
		if (a.Rank == 5) v += 1.5;
		if (b.Rank == 5) v += 1.5;
		if (a.Rank == b.Rank) v += 2;
		if (Score.Value(a) + Score.Value(b) == 15) v += 2;
		if (Math.Abs(a.Rank - b.Rank) is 1 or 2) v += 0.8;
		if (a.Suit == b.Suit) v += 0.2;
		return v + 3.8; // the crib's other three cards are worth about this on average
	}

	/// <summary>A rough value of one card thrown into a crib (with three or four players).</summary>
	private static double CribValue(Card a)
	{
		double v = a.Rank == 5 ? 1.5 : Score.Value(a) == 10 ? 0.4 : a.Rank is 7 or 8 ? 0.3 : 0;
		return v + 3.4;
	}

	public List<Card> ChooseDiscard(CribbageHand hand)
	{
		var cards = hand.Hand(Player).ToList();
		int throwing = hand.DiscardCount;
		if (Difficulty == Difficulty.Easy)
			return cards.OrderBy(_ => _random.Next()).Take(throwing).ToList();
		// The crib is ours if we (or, with four, our partner) dealt.
		bool myCrib = hand.Side(hand.Dealer) == hand.Side(Player);
		var unseen = Deck.Standard().Where(c => !cards.Contains(c)).ToList();
		double best = double.MinValue;
		List<Card> choice = cards.Take(throwing).ToList();
		foreach (var thrown in Choose(cards.Count, throwing))
		{
			var keep = cards.Where((_, k) => !thrown.Contains(k)).ToList();
			double value;
			if (Difficulty == Difficulty.Medium)
				value = Score.Items(keep, null).Sum(x => x.Points);
			else
			{
				value = unseen.Average(starter => Score.Hand(keep, starter));
				double crib = throwing == 2 ? CribValue(cards[thrown[0]], cards[thrown[1]]) : CribValue(cards[thrown[0]]);
				value += (myCrib ? 1 : -1) * crib;
			}
			if (value > best)
			{
				best = value;
				choice = thrown.Select(k => cards[k]).ToList();
			}
		}
		return choice;
	}

	/// <summary>Every way to pick one or two positions out of n.</summary>
	private static IEnumerable<int[]> Choose(int n, int k)
	{
		for (int i = 0; i < n; i++)
		{
			if (k == 1)
			{
				yield return new[] { i };
				continue;
			}
			for (int j = i + 1; j < n; j++)
				yield return new[] { i, j };
		}
	}

	public Card ChoosePlay(CribbageHand hand)
	{
		var options = hand.Playable();
		if (options.Count == 1 || Difficulty == Difficulty.Easy)
			return options[_random.Next(options.Count)];
		var seen = new HashSet<Card>(hand.Hand(Player).Concat(hand.Sequence).Concat(hand.Kept(Player)));
		if (hand.Starter is { } s)
			seen.Add(s);
		var unseen = Deck.Standard().Where(c => !seen.Contains(c)).ToList();
		double Value(Card card)
		{
			double now = Score.Peg(hand.Sequence, card).Sum(i => i.Points);
			int count = hand.Count + Score.Value(card);
			var after = hand.Sequence.Append(card).ToList();
			double risk = 0;
			if (count == 31)
				return now + 0.5;
			if (count is 5 or 21)
				risk += 1.2;
			if (Difficulty == Difficulty.Hard)
			{
				// Your best reply, averaged over the cards you might hold.
				var replies = unseen.Where(c => count + Score.Value(c) <= 31).ToList();
				if (replies.Count > 0)
					risk = replies.Average(r => (double)Score.Peg(after, r).Sum(i => i.Points)) * 1.6;
			}
			else
			{
				// A card you could pair is a risk unless we hold the third.
				if (hand.Hand(Player).Count(c => c.Rank == card.Rank) < 2)
					risk += 0.6;
			}
			// Keep low cards for later go points; play high ones while the count is low.
			double keep = count < 15 ? Score.Value(card) * 0.02 : -Score.Value(card) * 0.02;
			return now - risk + keep;
		}
		return options.OrderByDescending(Value).First();
	}
}
