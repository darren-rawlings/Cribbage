using System.Collections.Generic;
using Renegade.Ui;

namespace Cribbage.Game;

/// <summary>The guided tour behind the Demo button: a few short pages.</summary>
public static class Tutorial
{
	public static readonly IReadOnlyList<TutorialPage> Pages = new[]
	{
		new TutorialPage("The crib",
			"You and Rosie get six cards each. Tap two and press To the crib: the crib is an extra hand that scores for the dealer. Then a starter card is turned up (a jack scores the dealer 2)."),
		new TutorialPage("The play",
			"Take turns laying a card, keeping the count to 31 or less. Score 2 for making 15 or 31, 2 for a pair (6 for three, 12 for four), and a point a card for runs. If you can't play, it's a go: the last card scores 1."),
		new TutorialPage("The show",
			"Each hand is counted with the starter: 2 for every fifteen, 2 for every pair, a point a card for runs, 4 for a flush, and 1 for the jack of the starter's suit. Then the dealer counts the crib."),
		new TutorialPage("The board",
			"Every point moves your pegs along the board: the back peg leaps over the front one. First to 121 wins; win before the other reaches 91 and it's a skunk!"),
	};
}
