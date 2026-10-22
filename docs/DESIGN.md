# Cribbage: design

A Renegade card game on the card kit ([renegade-cards](https://github.com/darren-rawlings/renegade-cards)).

## The game
Six-card cribbage for two, you against Rosie (the Bicycle rules), a game to 121. Two cards each
to the dealer's crib; the starter (a jack: his heels, 2 to the dealer). The play: alternate
cards keeping the count to 31, pegging 15s and 31s (2), pairs (2/6/12), runs, go and last card
(1); the count restarts after 31 or a go. The show: non-dealer, dealer, then the crib, with the
starter: fifteens, pairs, runs, flushes (crib only with all five), his nobs. First to 121 wins at
once; under 91 is a skunk. Rules in `src/Core` (tested, including the 29 hand and whole games).

## Feel and the scoring device
- A wooden cribbage board along the far side of the felt, with three streets of holes per
  player (in fives) and two pegs each that leapfrog to every new score.
- The count is said aloud, a phrase at a time, the way players count ("fifteen 2, fifteen 4,
  a pair is 6"). Cards that would pass 31 are greyed.

## Computer levels
Easy (random throws and plays), Medium (keeps the best four, pegs for points, avoids 5 and 21),
Hard (keeps the four worth most over every starter, valuing the crib; weighs your best reply).

## Scope for version 1
Everything above. Later: muggins (claiming points the other forgot), three-player cribbage.
