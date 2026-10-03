# CSharp-NumberGuessingGame

A light number-guessing game in C# targeting .NET Framework 4.7.2 (builds with
Mono on Linux / MSBuild on Windows). The game logic lives in `GameEngine.cs`
behind a small input/output/RNG seam, so the whole suite can be unit-tested
deterministically; see the [Layout](#layout) section.

## Requirements

- **Mono** (`mcs` + `mono`) on Linux, or
- **Visual Studio / MSBuild** on Windows (the project targets .NET Framework 4.7.2).

## Build & run

With Mono (matching the CI workflow):

```sh
mkdir -p BasicGuessingGame/bin
mcs -warn:4 -out:BasicGuessingGame/bin/Guess.exe BasicGuessingGame/Program.cs BasicGuessingGame/GameEngine.cs
mono BasicGuessingGame/bin/Guess.exe              # 10 guesses per round (default)
mono BasicGuessingGame/bin/Guess.exe 15           # 15 guesses per round
mono BasicGuessingGame/bin/Guess.exe 15 42        # 15 guesses, deterministic (seed 42)
mono BasicGuessingGame/bin/Guess.exe 15 42 10 50  # 15 guesses, seed 42, secret in 10-50
```

With MSBuild (Visual Studio / `dotnet build` on Windows):

```sh
msbuild BasicGuessingGame/BasicGuessingGame.csproj /p:Configuration=Release
```

The command line is `Guess.exe [maxGuesses] [seed] [min] [max]`. All
arguments are optional:

- `<maxGuesses>` defaults to **10** and is clamped to the size of the range
  (the cap for the default 0–99 range is **100**): a bigger limit than there
  are possible numbers would be a guaranteed win, not a difficulty. An
  invalid value (non-numeric, zero, or negative) is **not** silently
  ignored — the program prints a notice and uses the default of 10
  (issue #8).
- `<seed>` (optional) makes the round deterministic for testing and
  reproducible play: `new Random(seed)`. With no seed the number is
  clock-seeded exactly as before (issue #7). An unparseable seed falls back
  to a clock-seeded round with a notice.
- `<min>` / `<max>` (optional, issue #12) set a custom guess range
  `[min, max]` inclusive — e.g. `10 50` or `1 1`. The pair is given
  together (a lone value is not a range and falls back to the default 0–99
  with a notice); `min` must be `>= 0` and `max > min`. The bounds message,
  out-of-range rejection, and the guess-limit cap all track the active
  range, so the game stays consistent for any range.

## Run the tests locally (issue #13)

The same build + test pipeline CI runs is available locally, so contributors
don't have to push to see whether their change broke anything:

- **Linux/macOS (Mono):** `sh run-tests.sh` (or `make test` / `make build`).
- **Windows (MSBuild):** `.\run-tests.ps1` from a Developer Command Prompt.

Both compile the game and the unit-test exe, run the deterministic unit
suite (76 assertions, seeded for reproducibility), and run the same
`printf`/scripted-stdin smoke tests the CI workflow runs. They exit non-zero
on the first failure.

## Layout

The solution has two projects (plus the test project), so it is no longer a
single file (issue #11):

- `BasicGuessingGame/` — the game project (a .NET Framework 4.7.2 exe):
  - `GameEngine.cs` — all game logic (issue #9): range checking, guess
    counting, win/lose, and command-line parsing, including the optional
    custom range (issue #12). It takes its input, output, RNG, guess limit,
    and range bounds through interfaces/constructor arguments instead of
    touching `Console` or hard-coded constants, so tests can drive a full
    round with scripted input.
  - `Program.cs` — thin console adapter: parses the command line, creates the
    (single) `Random` once per session, and runs the play-again loop.
  - `BasicGuessingGame.csproj` — MSBuild project file (Windows/Visual
    Studio).
- `BasicGuessingGame.Tests/` — dependency-free console-assert unit tests for
  the engine (no NUnit/xUnit, so it builds with plain `mcs` under Mono).
  Compiles `GameEngine.cs` directly (not `Program.cs`) so it has its own
  `Main`.
- `BasicGuessingGame.sln` — the solution tying the two projects together.
- `run-tests.sh` / `Makefile` — local build + test runner for Linux/macOS
  (issue #13).
- `run-tests.ps1` — the same for Windows via MSBuild (issue #13).
- `.github/workflows/ci.yml` — the CI workflow (Mono on a Linux runner).

## Gameplay

By default the computer picks a random whole number between **0 and 99**
(inclusive) — or between `[min]` and `[max]` if those optional arguments are
supplied (issue #12). Each round you get a limited number of guesses (the
"difficulty"):

- Guess too low / too high → you're told which direction to go.
- Guess correctly → you win the round.
- Run out of guesses → you lose, and the number is revealed.
- After a round you're asked to play again (`y`/`n`).

## Guess validation

The input handling is intentional and covered by the CI smoke tests:

- **Whole numbers only.** Non-numeric input (e.g. `abc`) prints
  `Invalid number. Please enter a whole number.` and does **not** count as a
  guess — it is no longer silently treated as `0`.
- **Range enforced (0–99 by default).** Out-of-range guesses (e.g. `150`,
  `-5`) print `Please enter a number between 0 and 99.` (with the active
  range's bounds when a custom `[min] [max]` is in effect, e.g.
  `Please enter a number between 10 and 50.`) and do **not** count as a
  guess. They are no longer played as a legitimate "too high"/"too low"
  guess.
- **Only valid, in-range guesses count** against the limit, so typos don't
  cost you a turn.
- **EOF** (closed input) exits cleanly with `Input closed. Goodbye!` instead
  of looping forever.

## Tests

CI (`.github/workflows/ci.yml`) compiles the game and the unit-test projects
and runs the full pipeline: a deterministic unit suite of **76 assertions**
(driven through the engine's injected seed, covering win/lose, hints,
out-of-range rejection, invalid input, EOF, command-line parsing, and custom
range bounds) plus a set of `printf`-driven smoke tests against the real
console program: invalid input, EOF, guaranteed win, out-of-range rejection
(both directions), the loss path, seeded deterministic rounds, custom range
rounds, guess-limit fallback/clamping, and the play-again prompt.

The same pipeline runs locally via `run-tests.sh` / `make test`
(Linux/macOS, Mono) or `run-tests.ps1` (Windows, MSBuild) — see
[Run the tests locally](#run-the-tests-locally-issue-13).
