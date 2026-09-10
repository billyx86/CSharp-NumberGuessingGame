# CSharp-NumberGuessingGame

A light and basic number guessing game written in C# (single-file, .NET Framework / Mono).

## Requirements

- **Mono** (`mcs` + `mono`) on Linux, or
- **Visual Studio / MSBuild** on Windows (the project targets .NET Framework 4.7.2).

## Build & run

With Mono (matching the CI workflow):

```sh
mkdir -p BasicGuessingGame/bin
mcs -warn:4 -out:BasicGuessingGame/bin/Guess.exe BasicGuessingGame/Program.cs
mono BasicGuessingGame/bin/Guess.exe            # 10 guesses per round (default)
mono BasicGuessingGame/bin/Guess.exe 15         # 15 guesses per round
```

With MSBuild (Visual Studio / `dotnet build` on Windows):

```sh
msbuild BasicGuessingGame/BasicGuessingGame.csproj /p:Configuration=Release
```

`<maxGuesses>` (the program argument) is optional and defaults to **10**.

## Gameplay

The computer picks a random whole number between **0 and 99** (inclusive). Each
round you get a limited number of guesses (the "difficulty"):

- Guess too low / too high → you're told which direction to go.
- Guess correctly → you win the round.
- Run out of guesses → you lose, and the number is revealed.
- After a round you're asked to play again (`y`/`n`).

## Guess validation

The input handling is intentional and covered by the CI smoke tests:

- **Whole numbers only.** Non-numeric input (e.g. `abc`) prints
  `Invalid number. Please enter a whole number.` and does **not** count as a
  guess — it is no longer silently treated as `0`.
- **Range enforced (0–99).** Out-of-range guesses (e.g. `150`, `-5`) print
  `Please enter a number between 0 and 99.` and do **not** count as a guess.
  They are no longer played as a legitimate "too high"/"too low" guess.
- **Only valid, in-range guesses count** against the limit, so typos don't
  cost you a turn.
- **EOF** (closed input) exits cleanly with `Input closed. Goodbye!` instead of
  looping forever.

## Tests

CI (`.github/workflows/ci.yml`) compiles the single file and runs a suite of
`printf`-driven smoke tests: invalid input, EOF, guaranteed win, out-of-range
rejection (both directions), the loss path, and the play-again prompt.
