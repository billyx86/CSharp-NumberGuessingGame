using System;

namespace BasicGuessingGame
{
    /// <summary>
    /// How a round of the guessing game ended (issue #9: the engine returns
    /// this instead of being coupled to the console).
    /// </summary>
    public enum RoundOutcome
    {
        /// <summary>The player guessed the secret number.</summary>
        Won,
        /// <summary>The player used up every valid guess without winning.</summary>
        RanOutOfGuesses,
        /// <summary>The input stream closed (EOF) before the round finished.</summary>
        AbortedByInput
    }

    /// <summary>
    /// Minimal input seam: one line per call, null on EOF. The console
    /// adapter feeds it from <see cref="Console.ReadLine"/>; tests feed it
    /// from a scripted list.
    /// </summary>
    public interface IGameInput
    {
        string ReadLine();
    }

    /// <summary>
    /// Minimal output seam. A single <see cref="WriteLine"/> (the game only
    /// ever needs the no-arg overload plus a pre-built string).
    /// </summary>
    public interface IGameOutput
    {
        void WriteLine(string message);
    }

    /// <summary>IGameInput backed by Console.ReadLine (used by Program).</summary>
    public sealed class ConsoleGameInput : IGameInput
    {
        public string ReadLine() { return Console.ReadLine(); }
    }

    /// <summary>IGameOutput backed by Console.WriteLine (used by Program).</summary>
    public sealed class ConsoleGameOutput : IGameOutput
    {
        public void WriteLine(string message) { Console.WriteLine(message); }
    }

    /// <summary>
    /// Parsed command line: the per-round guess limit and (optionally) an
    /// RNG seed for deterministic rounds (issue #7).
    /// </summary>
    public struct CommandLineOptions
    {
        public int MaxGuesses;
        public int? Seed;
    }

    /// <summary>
    /// The pure game logic (issue #9): range check, guess counting,
    /// win/lose. No Console access and no Random construction of its own —
    /// the input, output, RNG and guess limit are all injected, so the whole
    /// round can be driven deterministically by unit tests.
    ///
    /// The Random must be created by the caller ONCE per session (Program
    /// does this outside the play-again loop); re-creating a Random inside a
    /// tight loop on .NET Framework can seed from the same clock tick and
    /// repeat the secret number (issue #4).
    /// </summary>
    public class GameEngine
    {
        public const int MinGuess = 0;
        public const int MaxGuess = 99;

        /// <summary>How many valid guesses the player gets per round by default.</summary>
        public const int DefaultMaxGuesses = 10;

        /// <summary>
        /// Upper bound for the command-line guess limit (issue #8): larger
        /// values are clamped to this with a visible message, so a typo like
        /// "999999999" can't be taken at face value. (The range 0-99 has
        /// exactly 100 values, so a bigger limit is a mathematically
        /// guaranteed win — not a difficulty.)
        /// </summary>
        public const int MaxGuessLimitCap = 100;

        readonly IGameInput input;
        readonly IGameOutput output;
        readonly Random random;

        /// <summary>Valid guesses allowed this session (1 .. MaxGuessLimitCap).</summary>
        public int MaxGuesses { get; private set; }

        public GameEngine(IGameInput input, IGameOutput output, Random random, int maxGuesses)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (output == null) throw new ArgumentNullException("output");
            if (random == null) throw new ArgumentNullException("random");
            if (maxGuesses < 1) throw new ArgumentOutOfRangeException("maxGuesses");
            this.input = input;
            this.output = output;
            this.random = random;
            this.MaxGuesses = maxGuesses;
        }

        /// <summary>
        /// Parses the optional "Guess.exe [maxGuesses] [seed]" arguments.
        /// Invalid values are NOT silently ignored (issue #8): the player is
        /// told what was dropped / clamped and which value is actually used.
        /// An unparseable seed falls back to a clock-seeded Random.
        /// </summary>
        public static CommandLineOptions ParseCommandLineOptions(string[] args, IGameOutput output)
        {
            CommandLineOptions result = new CommandLineOptions { MaxGuesses = DefaultMaxGuesses, Seed = null };
            if (args == null)
            {
                return result;
            }

            if (args.Length > 0)
            {
                if (int.TryParse(args[0], out int parsedLimit) && parsedLimit > 0)
                {
                    if (parsedLimit > MaxGuessLimitCap)
                    {
                        output.WriteLine("Guess limit " + parsedLimit + " is above the maximum of " + MaxGuessLimitCap + "; using " + MaxGuessLimitCap + ".");
                        result.MaxGuesses = MaxGuessLimitCap;
                    }
                    else
                    {
                        result.MaxGuesses = parsedLimit;
                    }
                }
                else
                {
                    output.WriteLine("\"" + args[0] + "\" is not a valid guess limit (a whole number between 1 and " + MaxGuessLimitCap + "); using " + DefaultMaxGuesses + ".");
                    result.MaxGuesses = DefaultMaxGuesses;
                }
            }

            if (args.Length > 1)
            {
                if (int.TryParse(args[1], out int parsedSeed))
                {
                    result.Seed = parsedSeed;
                }
                else
                {
                    output.WriteLine("\"" + args[1] + "\" is not a valid seed; the number will be clock-seeded instead.");
                    result.Seed = null;
                }
            }

            return result;
        }

        /// <summary>Plays one full round and returns how it ended.</summary>
        public RoundOutcome PlayRound()
        {
            int secretNumber = random.Next(MinGuess, MaxGuess + 1);         // Whole number in [MinGuess, MaxGuess].
            int attemptsUsed = 0;                                           // Only valid, in-range guesses count against the limit.

            output.WriteLine(
                "The computer has chosen a random whole number between " + MinGuess + "-" + MaxGuess +
                ". You have " + MaxGuesses + " guesses. Try to guess it.");

            while (attemptsUsed < MaxGuesses)
            {
                output.WriteLine("Guess a number: ");
                string line = input.ReadLine();
                if (line == null)                                           // null means the input stream has been closed (EOF).
                {
                    output.WriteLine("Input closed. Goodbye!");
                    return RoundOutcome.AbortedByInput;
                }

                if (int.TryParse(line, out int guess) == false)             // Not a whole number...
                {
                    output.WriteLine("Invalid number. Please enter a whole number.");
                    continue;                                               // Ask again without counting it against the limit.
                }

                if (guess < MinGuess || guess > MaxGuess)                   // Enforce the stated range (issue #3).
                {
                    output.WriteLine("Please enter a number between " + MinGuess + " and " + MaxGuess + ".");
                    continue;                                               // Ask again without counting it against the limit.
                }

                attemptsUsed++;                                             // A real guess: it now counts against the limit.

                if (guess == secretNumber)
                {
                    output.WriteLine("Correct! You have guessed the number.");
                    return RoundOutcome.Won;
                }
                if (guess < secretNumber)
                {
                    output.WriteLine("Your guess was too low. Try again.");
                }
                else
                {
                    output.WriteLine("Your guess was too high. Try again.");
                }
            }

            // Ran out of guesses (issue #4: the player can lose).
            output.WriteLine("Sorry, you ran out of guesses. The number was " + secretNumber + ".");
            return RoundOutcome.RanOutOfGuesses;
        }
    }
}
