using System;
using System.Linq;
using Cribbage.Core;
using Renegade.Cards;
using Renegade.Core;
using Xunit;

namespace Cribbage.Tests;

public class ScoreTests
{
	private static Card C(int rank, Suit suit) => new(rank, suit);

	[Fact]
	public void TheBestHandScoresTwentyNine()
	{
		// Three fives and the jack of the starter's suit, with the fourth five turned up.
		var hand = new[] { C(5, Suit.Clubs), C(5, Suit.Diamonds), C(5, Suit.Hearts), C(Card.Jack, Suit.Spades) };
		Assert.Equal(29, Score.Hand(hand, C(5, Suit.Spades)));
	}

	[Fact]
	public void ADoubleRunOfThreeWithAFifteen()
	{
		// 4 5 5 6 + K: runs 4-5-6 twice (6), the pair (2), fifteens 5+K twice, 4+5+6 twice (8): 16.
		var hand = new[] { C(4, Suit.Clubs), C(5, Suit.Diamonds), C(5, Suit.Hearts), C(6, Suit.Spades) };
		Assert.Equal(16, Score.Hand(hand, C(Card.King, Suit.Clubs)));
	}

	[Fact]
	public void FlushesCountFourInTheHandButOnlyFiveInTheCrib()
	{
		var hand = new[] { C(2, Suit.Hearts), C(4, Suit.Hearts), C(8, Suit.Hearts), C(Card.Queen, Suit.Hearts) };
		Assert.Contains(Score.Items(hand, C(Card.King, Suit.Clubs)), i => i.Name == "Flush" && i.Points == 4);
		Assert.DoesNotContain(Score.Items(hand, C(Card.King, Suit.Clubs), crib: true), i => i.Name.StartsWith("Flush"));
		Assert.Contains(Score.Items(hand, C(Card.King, Suit.Hearts), crib: true), i => i.Points == 5);
	}

	[Fact]
	public void PeggingScoresFifteensPairsAndRuns()
	{
		Assert.Equal(2, Score.Peg(new[] { C(7, Suit.Clubs) }, C(8, Suit.Hearts)).Sum(i => i.Points));
		Assert.Equal(6, Score.Peg(new[] { C(4, Suit.Clubs), C(4, Suit.Hearts) }, C(4, Suit.Spades)).Sum(i => i.Points));
		// 3, 5, 4 makes a run of three (in any order).
		Assert.Equal(3, Score.Peg(new[] { C(3, Suit.Clubs), C(5, Suit.Hearts) }, C(4, Suit.Spades)).Sum(i => i.Points));
		Assert.Equal(2, Score.Peg(new[] { C(Card.King, Suit.Clubs), C(Card.Queen, Suit.Hearts), C(Card.Jack, Suit.Spades) }, C(Card.Ace, Suit.Spades)).Sum(i => i.Points) - 0);
	}
}

public class HandTests
{
	private static void Play(CribbageHand hand, CribbageAi[] ais, Random random)
	{
		foreach (var ai in ais)
			hand.Discard(ai.Player, ai.ChooseDiscard(hand), random.Next(40));
		int moves = 0;
		while (hand.Phase == CribPhase.Pegging && moves++ < 100)
			hand.Play(ais[hand.Current].ChoosePlay(hand));
		if (hand.Phase == CribPhase.Show)
			hand.Show();
	}

	[Fact]
	public void SixEachTwoToTheCribAndTheStarter()
	{
		var game = new CribbageGame();
		var hand = game.Deal(Deck.Shuffled(new Random(1)));
		Assert.Equal(6, hand.Hand(0).Count);
		var ais = new[] { new CribbageAi(0, Difficulty.Medium, new Random(1)), new CribbageAi(1, Difficulty.Medium, new Random(2)) };
		hand.Discard(0, ais[0].ChooseDiscard(hand));
		Assert.Equal(CribPhase.Discard, hand.Phase);
		hand.Discard(1, ais[1].ChooseDiscard(hand), cutAt: 5);
		Assert.Equal(4, hand.Crib.Count);
		Assert.NotNull(hand.Starter);
		Assert.Equal(CribPhase.Pegging, hand.Phase);
		Assert.Equal(hand.Pone, hand.Current);
	}

	[Theory]
	[InlineData(1)]
	[InlineData(2)]
	[InlineData(3)]
	public void GamesRunToOneTwentyOne(int seed)
	{
		var random = new Random(seed);
		var game = new CribbageGame();
		var ais = new[] { new CribbageAi(0, Difficulty.Hard, random), new CribbageAi(1, Difficulty.Easy, random) };
		while (!game.IsOver && game.Hands < 60)
		{
			var hand = game.Deal(Deck.Shuffled(random));
			Play(hand, ais, random);
			Assert.True(hand.Phase == CribPhase.Over);
			game.Finished();
		}
		Assert.True(game.IsOver);
		Assert.Equal(121, game.Scores.Max());
	}

	[Fact]
	public void TheCountNeverPassesThirtyOne()
	{
		var random = new Random(4);
		var ais = new[] { new CribbageAi(0, Difficulty.Easy, random), new CribbageAi(1, Difficulty.Easy, random) };
		for (int n = 0; n < 50; n++)
		{
			var hand = new CribbageHand(Deck.Shuffled(random), n % 2, new int[2]);
			foreach (var ai in ais)
				hand.Discard(ai.Player, ai.ChooseDiscard(hand), random.Next(40));
			while (hand.Phase == CribPhase.Pegging)
			{
				hand.Play(ais[hand.Current].ChoosePlay(hand));
				Assert.InRange(hand.Count, 0, 31);
			}
			Assert.Equal(CribPhase.Show, hand.Phase);
		}
	}

	[Fact]
	public void HardBeatsEasyMostGames()
	{
		int wins = 0;
		for (int seed = 0; seed < 30; seed++)
		{
			var random = new Random(seed);
			var game = new CribbageGame();
			var ais = new[] { new CribbageAi(0, Difficulty.Hard, random), new CribbageAi(1, Difficulty.Easy, random) };
			while (!game.IsOver)
			{
				var hand = game.Deal(Deck.Shuffled(random));
				Play(hand, ais, random);
				game.Finished();
			}
			if (game.Winner == 0)
				wins++;
		}
		Assert.True(wins >= 20, $"Hard won {wins} of 30");
	}
}

public class MorePlayersTests
{
	private static void Play(CribbageHand hand, CribbageAi[] ais, Random random)
	{
		foreach (var ai in ais)
			hand.Discard(ai.Player, ai.ChooseDiscard(hand), random.Next(30));
		int moves = 0;
		while (hand.Phase == CribPhase.Pegging && moves++ < 200)
		{
			hand.Play(ais[hand.Current].ChoosePlay(hand));
			Assert.InRange(hand.Count, 0, 31);
		}
		if (hand.Phase == CribPhase.Show)
			hand.Show();
	}

	[Fact]
	public void ThreePlayersGetFiveEachAndTheCribGetsOneFromTheDeck()
	{
		var game = new CribbageGame(3);
		var hand = game.Deal(Deck.Shuffled(new Random(1)));
		Assert.All(Enumerable.Range(0, 3), p => Assert.Equal(5, hand.Hand(p).Count));
		Assert.NotNull(hand.CribCard);
		Assert.Equal(1, hand.DiscardCount);
		var ais = Enumerable.Range(0, 3).Select(p => new CribbageAi(p, Difficulty.Medium, new Random(p))).ToArray();
		foreach (var ai in ais)
			hand.Discard(ai.Player, ai.ChooseDiscard(hand), 4);
		Assert.Equal(4, hand.Crib.Count);
		Assert.Contains(hand.CribCard!.Value, hand.Crib);
		Assert.All(Enumerable.Range(0, 3), p => Assert.Equal(4, hand.Kept(p).Count));
	}

	[Fact]
	public void FourPlayersAreTwoPartnershipsSharingAScore()
	{
		var game = new CribbageGame(4);
		Assert.Equal(2, game.Scores.Length);
		var hand = game.Deal(Deck.Shuffled(new Random(2)));
		Assert.Equal(hand.Side(0), hand.Side(2));
		Assert.NotEqual(hand.Side(0), hand.Side(1));
		Assert.All(Enumerable.Range(0, 4), p => Assert.Equal(5, hand.Hand(p).Count));
		Assert.Null(hand.CribCard);
	}

	[Theory]
	[InlineData(3, 1)]
	[InlineData(4, 2)]
	[InlineData(3, 3)]
	[InlineData(4, 4)]
	public void BigTablesPlayToOneTwentyOne(int players, int seed)
	{
		var random = new Random(seed);
		var game = new CribbageGame(players);
		var ais = Enumerable.Range(0, players).Select(p => new CribbageAi(p, p % 2 == 0 ? Difficulty.Hard : Difficulty.Medium, random)).ToArray();
		while (!game.IsOver && game.Hands < 80)
		{
			var hand = game.Deal(Deck.Shuffled(random));
			Play(hand, ais, random);
			Assert.Equal(CribPhase.Over, hand.Phase);
			game.Finished();
		}
		Assert.True(game.IsOver);
		Assert.Equal(121, game.Scores.Max());
	}

	[Fact]
	public void TheShowGoesRoundFromTheDealersLeftAndEndsWithTheCrib()
	{
		var random = new Random(6);
		var game = new CribbageGame(4);
		var hand = game.Deal(Deck.Shuffled(random));
		var ais = Enumerable.Range(0, 4).Select(p => new CribbageAi(p, Difficulty.Easy, random)).ToArray();
		foreach (var ai in ais)
			hand.Discard(ai.Player, ai.ChooseDiscard(hand), 3);
		while (hand.Phase == CribPhase.Pegging)
			hand.Play(ais[hand.Current].ChoosePlay(hand));
		if (hand.Phase != CribPhase.Show)
			return; // someone pegged out
		var shows = hand.Show();
		var expected = Enumerable.Range(1, 4).Select(k => (hand.Dealer + k) % 4).ToList();
		Assert.Equal(expected.Take(shows.Count), shows.Take(4).Select(s => s.Player));
		if (shows.Count == 5)
			Assert.Equal("crib", shows[4].What);
	}
}
