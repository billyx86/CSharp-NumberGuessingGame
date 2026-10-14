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
    /// Parsed command line: the per-round guess limit, (optionally) an RNG
    /// seed for deterministic rounds (issue #7), and (optionally) a custom
    /// guess range [min] [max] (issue #12). MinGuess/MaxGuess are null when
    /// the caller didn't supply (a valid) custom range, in which case the
    /// engine falls back to the default 0-99 range. ShowHelp is true when a
    /// help flag (-h / --help / ?) was present; the usage text has already
    /// been written to the output and no round should be started (issue #16).
    /// </summary>
    public struct CommandLineOptions
    {
        public int MaxGuesses;
        public int? Seed;
        public int? MinGuess;
        public int? MaxGuess;
        public bool ShowHelp;
    }

    /// <summary>
    /// The pure game logic (issue #9): range check, guess counting,
    /// win/lose. No Console access and no Random construction of its own —
    /// the input, output, RNG, guess limit and guess range are all injected,
    /// so the whole round can be driven deterministically by unit tests.
    ///
    /// The guess range is configurable per session (issue #12); the default
    /// is 0-99 (issue #3).
    ///
    /// The Random must be created by the caller ONCE per session (Program
    /// does this outside the play-again loop); re-creating a Random inside a
    /// tight loop on .NET Framework can seed from the same clock tick and
    /// repeat the secret number (issue #4).
    /// </summary>
    public class GameEngine
    {
        /// <summary>Default guess range: 0-99 (issue #3). Overridable per
        /// session via the 6-argument constructor or the [min] [max] CLI
        /// arguments (issue #12).</summary>
        public const int DefaultMinGuess = 0;
        public const int DefaultMaxGuess = 99;

        /// <summary>How many valid guesses the player gets per round by default.</summary>
        public const int DefaultMaxGuesses = 10;

        /// <summary>
        /// Guess-limit cap for the DEFAULT 0-99 range (issue #8): larger
        /// values are clamped to this with a visible message, so a typo like
        /// "999999999" can't be taken at face value. The cap always equals the
        /// number of values in the range — a range with N values can never
        /// benefit from more than N guesses, so an oversized limit is a
        /// guaranteed win, not a difficulty. (The default 0-99 range has
        /// exactly 100 values.) For a custom range, see <see cref="GuessLimitCap"/>.
        /// </summary>
        public const int MaxGuessLimitCap = DefaultMaxGuess - DefaultMinGuess + 1;

        /// <summary>
        /// The largest guess-range span the game can draw a secret from
        /// (issue #15): <c>Random.Next(int, int)</c> can represent at most
        /// <c>int.MaxValue</c> values (the 0..int.MaxValue range), so a
        /// wider span is rejected at parse time and construction time
        /// rather than crashing mid-round.
        /// </summary>
        public const long MaxRangeSpan = int.MaxValue;

        /// <summary>
        /// Guess-limit cap for an arbitrary range (issue #12): a range with
        /// N values can never benefit from more than N guesses, so an
        /// oversized limit is a guaranteed win, never a difficulty.
        ///
        /// Computed in long so a range near int.MaxValue cannot overflow
        /// the span (issue #15); the result is capped at
        /// <see cref="MaxRangeSpan"/>.
        /// </summary>
        public static int GuessLimitCap(int minGuess, int maxGuess)
        {
            long span = (long)maxGuess - minGuess + 1;
            return span > MaxRangeSpan ? (int)MaxRangeSpan : (int)span;
        }

        readonly IGameInput input;
        readonly IGameOutput output;
        readonly Random random;

        /// <summary>Inclusive lower bound of the guess range for this session (issue #12).</summary>
        public int MinGuess { get; private set; }

        /// <summary>Inclusive upper bound of the guess range for this session (issue #12).</summary>
        public int MaxGuess { get; private set; }

        /// <summary>Valid guesses allowed this session (1 .. GuessLimitCap(MinGuess, MaxGuess)).</summary>
        public int MaxGuesses { get; private set; }

        /// <summary>
        /// Default-range (0-99) constructor — behaviour is exactly as before
        /// issue #12.
        /// </summary>
        public GameEngine(IGameInput input, IGameOutput output, Random random, int maxGuesses)
            : this(input, output, random, maxGuesses, DefaultMinGuess, DefaultMaxGuess)
        {
        }

        /// <summary>
        /// Full constructor (issue #12): the guess range is configurable.
        /// minGuess must be a whole number >= 0 and maxGuess > minGuess.
        /// </summary>
        public GameEngine(IGameInput input, IGameOutput output, Random random, int maxGuesses, int minGuess, int maxGuess)
        {
            if (input == null) throw new ArgumentNullException("input");
            if (output == null) throw new ArgumentNullException("output");
            if (random == null) throw new ArgumentNullException("random");
            if (maxGuesses < 1) throw new ArgumentOutOfRangeException("maxGuesses");
            if (minGuess < 0) throw new ArgumentOutOfRangeException("minGuess");
            if (maxGuess <= minGuess) throw new ArgumentOutOfRangeException("maxGuess");
            if ((long)maxGuess - minGuess + 1 > MaxRangeSpan) throw new ArgumentOutOfRangeException("maxGuess");   // span too wide for Random.Next (issue #15)
            this.input = input;
            this.output = output;
            this.random = random;
            this.MinGuess = minGuess;
            this.MaxGuess = maxGuess;
            this.MaxGuesses = maxGuesses;
        }

        /// <summary>
        /// The usage text printed for -h / --help / ? (issue #16). Exposed
        /// as a constant so tests can assert against the exact wording.
        /// </summary>
        public const string UsageText =
            "Usage: Guess.exe [maxGuesses] [seed] [min] [max]\n" +
            "\n" +
            "All arguments are optional:\n" +
            "  maxGuesses   guesses per round (default 10, capped at the\n" +
            "               size of the active range)\n" +
            "  seed         RNG seed for a deterministic round\n" +
            "  min          inclusive lower bound of the guess range\n" +
            "  max          inclusive upper bound of the guess range\n" +
            "\n" +
            "  [min] and [max] must be given together (0 <= min < max);\n" +
            "  without them the secret is in 0-99. Invalid values are\n" +
            "  announced, never silently dropped.";

        /// <summary>True for the help flags: -h, --help and ? (issue #16).</summary>
        public static bool IsHelpFlag(string arg)
        {
            return arg == "-h" || arg == "--help" || arg == "?";
        }

        /// <summary>
        /// Parses the optional "Guess.exe [maxGuesses] [seed] [min] [max]"
        /// arguments. Invalid values are NOT silently ignored (issues #8/#12):
        /// the player is told what was dropped / clamped and which value is
        /// actually used. An unparseable seed falls back to a clock-seeded
        /// Random; an invalid range falls back to the default 0-99 range.
        ///
        /// A help flag anywhere on the command line (-h / --help / ?, issue
        /// #16) writes the usage text and sets ShowHelp — no game is started.
        ///
        /// A range whose span would overflow int (or exceed the maximum span
        /// Random.Next can represent) is rejected with a message and the
        /// default 0-99 range is used (issue #15) — same "announce, never
        /// silently drop" style as the other invalid-input paths.
        /// </summary>
        public static CommandLineOptions ParseCommandLineOptions(string[] args, IGameOutput output)
        {
            CommandLineOptions result = new CommandLineOptions
            {
                MaxGuesses = DefaultMaxGuesses,
                Seed = null,
                MinGuess = null,
                MaxGuess = null,
                ShowHelp = false
            };
            if (args == null)
            {
                return result;
            }

            // ---- help flag (any position, issue #16) ---------------------
            foreach (string arg in args)
            {
                if (IsHelpFlag(arg))
                {
                    output.WriteLine(UsageText);
                    result.ShowHelp = true;
                    return result;
                }
            }

            // ---- [maxGuesses] (positional arg 1, optional) -----------------
            if (args.Length > 0)
            {
                if (int.TryParse(args[0], out int parsedLimit) && parsedLimit > 0)
                {
                    result.MaxGuesses = parsedLimit;   // clamped to the range's size below
                }
                else
                {
                    output.WriteLine("\"" + args[0] + "\" is not a valid guess limit (a whole number between 1 and " + MaxGuessLimitCap + "); using " + DefaultMaxGuesses + ".");
                    result.MaxGuesses = DefaultMaxGuesses;
                }
            }

            // ---- [seed] (positional arg 2, optional) -----------------------
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

            // ---- [min] [max] (positional args 3/4, optional, issue #12) ----
            // The pair must be given together; a lone [min] (or [max]) is
            // not a range and falls back to the default with a message.
            // minGuess/maxGuess track the ACTIVE range for the cap clamp
            // below: they hold the defaults until a valid pair is parsed
            // (issue #15: invalid pairs must reset them, not leave
            // half-parsed values behind).
            int minGuess = DefaultMinGuess;
            int maxGuess = DefaultMaxGuess;
            if (args.Length > 2)
            {
                bool hasBoth = args.Length > 3;
                bool parsed = hasBoth && int.TryParse(args[2], out int parsedMin) && int.TryParse(args[3], out int parsedMax);
                if (!parsed)
                {
                    string given = hasBoth ? args[2] + " " + args[3] : args[2];
                    output.WriteLine("\"" + given + "\" is not a valid guess range (two whole numbers, 0 <= min < max, given together); using the default range " + DefaultMinGuess + "-" + DefaultMaxGuess + ".");
                    minGuess = DefaultMinGuess;
                    maxGuess = DefaultMaxGuess;
                }
                else if (parsedMin < 0)
                {
                    output.WriteLine("Guess range minimum must be 0 or greater; using the default range " + DefaultMinGuess + "-" + DefaultMaxGuess + ".");
                    minGuess = DefaultMinGuess;
                    maxGuess = DefaultMaxGuess;
                }
                else if (parsedMax <= parsedMin)
                {
                    output.WriteLine("Guess range maximum (" + parsedMax + ") must be greater than the minimum (" + parsedMin + "); using the default range " + DefaultMinGuess + "-" + DefaultMaxGuess + ".");
                    minGuess = DefaultMinGuess;
                    maxGuess = DefaultMaxGuess;
                }
                else if ((long)parsedMax - parsedMin + 1 > MaxRangeSpan)
                {
                    // Span too wide for Random.Next(int, int) (issue #15):
                    // announce and fall back, never crash mid-round.
                    output.WriteLine("Guess range " + parsedMin + "-" + parsedMax + " is too large (more than " + MaxRangeSpan + " values); using the default range " + DefaultMinGuess + "-" + DefaultMaxGuess + ".");
                    minGuess = DefaultMinGuess;
                    maxGuess = DefaultMaxGuess;
                }
                else
                {
                    minGuess = parsedMin;
                    maxGuess = parsedMax;
                    result.MinGuess = minGuess;
                    result.MaxGuess = maxGuess;
                }
            }

            // ---- clamp the guess limit to THIS range's size (issue #12) ----
            // (If the range was invalid, minGuess/maxGuess were reset to the
            // defaults above, so the limit is clamped against the default
            // range — issue #15.)
            int cap = GuessLimitCap(minGuess, maxGuess);
            if (result.MaxGuesses > cap)
            {
                output.WriteLine("Guess limit " + result.MaxGuesses + " is above the maximum of " + cap + " for the " + minGuess + "-" + maxGuess + " range; using " + cap + ".");
                result.MaxGuesses = cap;
            }

            return result;
        }

        /// <summary>Plays one full round and returns how it ended.</summary>
        public RoundOutcome PlayRound()
        {
            int secretNumber;
            if (MaxGuess == int.MaxValue)
            {
                // MaxGuess + 1 would overflow, so Next(min, max + 1) cannot
                // be called for a range ending at int.MaxValue. Span-based
                // draw instead: Next(span) is uniform over [0, span) and
                // MinGuess + draw lands in [MinGuess, MaxGuess] without
                // overflow (issue #15). The span is guaranteed to fit an
                // int by the MaxRangeSpan check in the constructor.
                long span = (long)MaxGuess - MinGuess + 1;
                secretNumber = MinGuess + random.Next((int)span);
            }
            else
            {
                secretNumber = random.Next(MinGuess, MaxGuess + 1);   // Whole number in [MinGuess, MaxGuess].
            }
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
