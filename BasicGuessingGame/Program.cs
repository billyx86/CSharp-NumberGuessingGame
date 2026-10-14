using System;

namespace BasicGuessingGame
{
    /// <summary>
    /// Thin console adapter around <see cref="GameEngine"/> (issue #9). All
    /// of the game logic — range check, guess counting, win/lose — lives in
    /// GameEngine so it can be unit-tested without spawning a process or
    /// feeding stdin; this class only wires the command line and Console
    /// to the engine and runs the play-again loop.
    ///
    /// Process exit code (issue #17) — the final round's outcome is the
    /// session's outcome, so a shell script can tell how it ended:
    ///   0 = won, 1 = ran out of guesses, 2 = aborted (input closed),
    ///   3 = help requested. A session that plays several rounds exits with
    ///   the code of the LAST round (a win after a loss exits 0).
    /// </summary>
    internal class Program
    {
        /// <summary>Exit code for a session that won its last round (issue #17).</summary>
        public const int ExitCodeWon = 0;
        /// <summary>Exit code for a session whose last round ran out of guesses (issue #17).</summary>
        public const int ExitCodeRanOutOfGuesses = 1;
        /// <summary>Exit code for a session aborted by EOF before any round finished (issue #17).</summary>
        public const int ExitCodeAborted = 2;
        /// <summary>Exit code when the help flag was requested (issue #16/#17).</summary>
        public const int ExitCodeHelp = 3;

        static void Main(string[] args)
        {
            IGameInput input = new ConsoleGameInput();
            IGameOutput output = new ConsoleGameOutput();

            // Optional command line: "Guess.exe [maxGuesses] [seed] [min] [max]".
            // Invalid values are announced, never silently dropped (issue #8);
            // an optional seed makes the round deterministic (issue #7); an
            // optional [min] [max] pair sets a custom guess range (issue #12);
            // a help flag (-h / --help / ?) prints usage and exits 3 without
            // starting a round (issue #16).
            CommandLineOptions options = GameEngine.ParseCommandLineOptions(args, output);
            if (options.ShowHelp)
            {
                Environment.ExitCode = ExitCodeHelp;
                return;
            }

            // Seed the Random ONCE, outside the play-again loop. Re-creating a
            // Random inside a tight loop on .NET Framework can seed from the
            // same clock tick and repeat the secret number (issue #4). With no
            // seed this stays clock-seeded, exactly as before (issue #7).
            Random rand = options.Seed.HasValue ? new Random(options.Seed.Value) : new Random();

            // Custom guess range when supplied (issue #12), otherwise the
            // engine's default 0-99 range.
            GameEngine engine = options.MinGuess.HasValue
                ? new GameEngine(input, output, rand, options.MaxGuesses, options.MinGuess.Value, options.MaxGuess.Value)
                : new GameEngine(input, output, rand, options.MaxGuesses);

            RoundOutcome lastOutcome = RoundOutcome.AbortedByInput;
            bool playAgain = true;
            while (playAgain)
            {
                lastOutcome = engine.PlayRound();
                if (lastOutcome == RoundOutcome.AbortedByInput)
                {
                    // EOF ends the session; the engine already said goodbye.
                    playAgain = false;
                    continue;
                }

                output.WriteLine("Play again? (y/n)");
                string again = input.ReadLine();
                if (again == null)                                        // EOF at the prompt
                {
                    output.WriteLine("Input closed. Goodbye!");
                    // The round already finished (won / ran out of guesses);
                    // the closed play-again prompt does not change the outcome.
                    playAgain = false;
                }
                else
                {
                    string answer = again.Trim().ToLowerInvariant();
                    playAgain = (answer == "y" || answer == "yes");
                    if (!playAgain)
                    {
                        output.WriteLine("Thanks for playing!");
                    }
                }
            }

            // Surface the session outcome to the shell (issue #17).
            switch (lastOutcome)
            {
                case RoundOutcome.Won:
                    Environment.ExitCode = ExitCodeWon;
                    break;
                case RoundOutcome.RanOutOfGuesses:
                    Environment.ExitCode = ExitCodeRanOutOfGuesses;
                    break;
                default:
                    Environment.ExitCode = ExitCodeAborted;
                    break;
            }
        }
    }
}
