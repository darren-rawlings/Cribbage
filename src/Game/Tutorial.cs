using System.Collections.Generic;
using Renegade.Ui;

namespace Cribbage.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The crib",
			"With two players you get six cards and put two in the crib; with three or four, five cards and one in (four players are two partnerships: you and Max). Tap your cards and press To the crib: the crib is an extra hand that scores for the dealer. The player on the dealer's right cuts the deck and the dealer turns up the starter (a jack scores the dealer 2).",
			"res://tutorial/crib.ogv"),
		new TutorialPage("The play",
			"Take turns laying a card, keeping the count to 31 or less. Score 2 for making 15 or 31, 2 for a pair (6 for three, 12 for four), and a point a card for runs. If you can't play, it's a go: the last card scores 1.",
			"res://tutorial/pegging.ogv"),
		new TutorialPage("The show",
			"Each hand is counted with the starter: 2 for every fifteen, 2 for every pair, a point a card for runs, 4 for a flush, and 1 for the jack of the starter's suit. Then the dealer counts the crib. A hand worth nothing is called \"nineteen\" (you can't make 19).",
			"res://tutorial/show.ogv"),
		new TutorialPage("The board",
			"Every point moves your pegs along the board on your right: up the first column, down the second, up the third, the back peg leaping over the front one. First to 121 wins the game, and moves their peg along the GAMES row at the bottom: a skunk (winning before the others pass the skunk line, 90) counts two games, and a double skunk (before the double-skunk line, 60) three. First to seven games wins the match!",
			"res://tutorial/pegging.ogv"),
	};
}
