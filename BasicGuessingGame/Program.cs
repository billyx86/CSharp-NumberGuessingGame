using System;

namespace BasicGuessingGame
{
    /// <summary>
    /// Thin console adapter around <see cref="GameEngine"/> (issue #9). All
    /// of the game logic — range check, guess counting, win/lose — lives in
    /// GameEngine so it can be unit-tested without spawning a process or
    /// feeding stdin; this class only wires the command line and Console
    /// to the engine and runs the play-again loop.
    /// </summary>
    internal class Program
    {
        static void Main(string[] args)
        {
            IGameInput input = new ConsoleGameInput();
            IGameOutput output = new ConsoleGameOutput();

            // Optional command line: "Guess.exe [maxGuesses] [seed] [min] [max]".
            // Invalid values are announced, never silently dropped (issue #8);
            // an optional seed makes the round deterministic (issue #7); an
            // optional [min] [max] pair sets a custom guess range (issue #12).
            CommandLineOptions options = GameEngine.ParseCommandLineOptions(args, output);

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

            bool playAgain = true;
            while (playAgain)
            {
                RoundOutcome outcome = engine.PlayRound();
                if (outcome == RoundOutcome.AbortedByInput)
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
        }
    }
}
