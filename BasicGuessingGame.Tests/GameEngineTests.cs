using System;
using System.Collections.Generic;
using BasicGuessingGame;

namespace BasicGuessingGame.Tests
{
    /// <summary>Scripted input: returns the lines in order, then null (EOF).</summary>
    sealed class ScriptedInput : IGameInput
    {
        readonly Queue<string> lines;
        public ScriptedInput(params string[] lines)
        {
            this.lines = new Queue<string>(lines);
        }
        public string ReadLine()
        {
            return lines.Count > 0 ? lines.Dequeue() : null;
        }
    }

    /// <summary>Output that records every line for assertions.</summary>
    sealed class CapturingOutput : IGameOutput
    {
        public readonly List<string> Lines = new List<string>();
        public void WriteLine(string message) { Lines.Add(message); }
        public bool Contains(string substring)
        {
            foreach (string line in Lines)
            {
                if (line != null && line.Contains(substring)) return true;
            }
            return false;
        }
        public string All()
        {
            return string.Join("\n", Lines);
        }
    }

    /// <summary>
    /// Unit tests for GameEngine + command-line parsing (issue #9). Pure
    /// console-assert style: no NUnit/xUnit dependency, so it builds with a
    /// plain `mcs` on Mono (matching CI) and runs anywhere mono runs.
    /// Exit code 0 = all passed, 1 = at least one failure.
    /// </summary>
    static class GameEngineTests
    {
        static int passed;
        static int failed;

        static void Check(bool condition, string name)
        {
            if (condition)
            {
                passed++;
                Console.WriteLine("PASS " + name);
            }
            else
            {
                failed++;
                Console.WriteLine("FAIL " + name);
            }
        }

        static void CheckEqual<T>(T expected, T actual, string name)
        {
            Check(EqualityComparer<T>.Default.Equals(expected, actual),
                name + " (expected '" + expected + "', got '" + actual + "')");
        }

        static CapturingOutput RunGame(string[] inputLines, int maxGuesses, int? seed)
        {
            CapturingOutput output = new CapturingOutput();
            // Seed 1 -> secret 24 in this runtime (deterministic under mono and
            // .NET Framework, which share the seeded Random algorithm).
            Random rand = new Random(seed ?? 1);
            GameEngine engine = new GameEngine(new ScriptedInput(inputLines), output, rand, maxGuesses);
            engine.PlayRound();
            return output;
        }

        static CapturingOutput RunGameRange(string[] inputLines, int maxGuesses, int? seed, int minGuess, int maxGuess)
        {
            CapturingOutput output = new CapturingOutput();
            Random rand = new Random(seed ?? 1);
            GameEngine engine = new GameEngine(new ScriptedInput(inputLines), output, rand, maxGuesses, minGuess, maxGuess);
            engine.PlayRound();
            return output;
        }

        static void Main()
        {
            Test_InvalidInput_DoesNotConsumeAGuess();
            Test_OutOfRangeInput_DoesNotConsumeAGuess();
            Test_Win_ReturnsWon_AndStopsReading();
            Test_LoseAtExactLimit();
            Test_DirectionHints();
            Test_EofDuringRound_Aborts();
            Test_EofAtPrompt_ExitPath();
            Test_SeededFullTranscript();
            Test_TooLowThenTooHighSequence();
            Test_ParseCommandLineOptions();
            Test_CustomRange_SeededTranscript();
            Test_CustomRange_EnforcesNewBounds();
            Test_GuessLimitCap_ScalesWithRange();
            Test_CustomRange_CapClampsLimit();
            Test_CustomRange_ConstructorValidation();
            Test_ParseCommandLineOptions_Range();

            Console.WriteLine();
            Console.WriteLine(passed + " passed, " + failed + " failed.");
            Environment.Exit(failed == 0 ? 0 : 1);
        }

        static void Test_InvalidInput_DoesNotConsumeAGuess()
        {
            // Limit 2: two invalid inputs followed by the correct secret (24
            // with seed 1). The invalid lines must not consume guesses, so
            // this wins even though only one VALID guess was made.
            CapturingOutput output = RunGame(new[] { "abc", "12.5", "24" }, 2, 1);
            Check(output.Contains("Invalid number. Please enter a whole number."), "invalid-input: non-numeric rejected");
            Check(output.Contains("Correct! You have guessed the number."), "invalid-input: win after 2 invalid + 1 valid guess (limit 2)");
        }

        static void Test_OutOfRangeInput_DoesNotConsumeAGuess()
        {
            // Same idea with out-of-range guesses instead of non-numeric ones.
            CapturingOutput output = RunGame(new[] { "-5", "150", "24" }, 1, 1);
            Check(output.Contains("Please enter a number between 0 and 99."), "out-of-range: rejected with range message");
            Check(!output.Contains("too high"), "out-of-range: 150 not played as 'too high'");
            Check(!output.Contains("too low"), "out-of-range: -5 not played as 'too low'");
            Check(output.Contains("Correct! You have guessed the number."), "out-of-range: win with limit 1 after two out-of-range inputs");
        }

        static void Test_Win_ReturnsWon_AndStopsReading()
        {
            // With limit 1 the very first valid guess ends the round; the
            // remaining scripted lines must stay unread (no further prompts).
            CapturingOutput output = RunGame(new[] { "24", "0", "1" }, 1, 1);
            Check(output.Contains("Correct! You have guessed the number."), "win: correct guess on attempt 1");
            int prompts = 0;
            foreach (string line in output.Lines)
            {
                if (line == "Guess a number: ") prompts++;
            }
            Check(prompts == 1, "win: exactly one prompt (game stops reading after the winning guess)");
        }

        static void Test_LoseAtExactLimit()
        {
            // Limit 3, guesses 0/1/2 — all below the secret (24, seed 1) —
            // must run out of guesses and reveal the number.
            CapturingOutput output = RunGame(new[] { "0", "1", "2" }, 3, 1);
            Check(output.Contains("Sorry, you ran out of guesses."), "lose: ran out at exactly the limit");
            Check(output.Contains("The number was 24."), "lose: secret revealed (seed 1 -> 24)");
            Check(output.All().Split('\n').Length == 1 + 3 * 2 + 1, "lose: banner + 3 prompts/replies + loss line, nothing more");
        }

        static void Test_DirectionHints()
        {
            CapturingOutput output = RunGame(new[] { "0", "99" }, 5, 1);
            // Output is banner, prompt, hint, prompt, hint (input is never echoed).
            Check(output.Lines[1] == "Guess a number: ", "hints: first prompt right after the banner");
            Check(output.Lines[2] == "Your guess was too low. Try again.", "hints: 0 < 24 is 'too low'");
            Check(output.Lines[4] == "Your guess was too high. Try again.", "hints: 99 > 24 is 'too high'");
        }

        static void Test_EofDuringRound_Aborts()
        {
            // One invalid line, then EOF: the round must abort, print the
            // goodbye, and end (Program maps this to session exit).
            CapturingOutput output = RunGame(new[] { "abc" }, 5, 1);
            Check(output.Contains("Input closed. Goodbye!"), "eof: goodbye printed mid-round");
            Check(output.Contains("Invalid number. Please enter a whole number."), "eof: the valid prompt cycle happened before EOF");
            Check(!output.Contains("ran out of guesses"), "eof: EOF is not a loss");
        }

        static void Test_EofAtPrompt_ExitPath()
        {
            // Immediate EOF on the first prompt: banner + prompt + goodbye.
            CapturingOutput output = RunGame(new string[0], 5, 1);
            Check(output.Lines.Count == 3, "eof@prompt: exactly banner, prompt, goodbye");
            CheckEqual("Input closed. Goodbye!", output.Lines[2], "eof@prompt: goodbye line");
        }

        static void Test_SeededFullTranscript()
        {
            // Full deterministic transcript with seed 1 (secret 24): the exact
            // line sequence, including the 'n=..,fp=..' style is NOT used
            // here (that is the Safari extension's log) — this is the plain
            // console transcript.
            CapturingOutput output = RunGame(new[] { "0", "99", "24" }, 10, 1);
            string expected =
                "The computer has chosen a random whole number between 0-99. You have 10 guesses. Try to guess it.\n" +
                "Guess a number: \n" +
                "Your guess was too low. Try again.\n" +
                "Guess a number: \n" +
                "Your guess was too high. Try again.\n" +
                "Guess a number: \n" +
                "Correct! You have guessed the number.";
            CheckEqual(expected, output.All(), "seeded-transcript: exact output for seed 1, guesses 0,99,24");
        }

        static void Test_TooLowThenTooHighSequence()
        {
            // 20 -> low, 50 -> high (secret 24): both directions in one round.
            CapturingOutput output = RunGame(new[] { "20", "50", "24" }, 10, 1);
            Check(output.Lines[2] == "Your guess was too low. Try again.", "sequence: 20 < 24 low");
            Check(output.Lines[4] == "Your guess was too high. Try again.", "sequence: 50 > 24 high");
            Check(output.Lines[6] == "Correct! You have guessed the number.", "sequence: 24 wins on third valid guess");
        }

        static void Test_ParseCommandLineOptions()
        {
            CapturingOutput out1 = new CapturingOutput();
            CommandLineOptions o1 = GameEngine.ParseCommandLineOptions(new string[0], out1);
            CheckEqual(GameEngine.DefaultMaxGuesses, o1.MaxGuesses, "parse: no args -> default limit");
            CheckEqual(null, o1.Seed, "parse: no args -> no seed");
            Check(out1.Lines.Count == 0, "parse: no args -> no messages");

            CapturingOutput out2 = new CapturingOutput();
            CommandLineOptions o2 = GameEngine.ParseCommandLineOptions(new[] { "15" }, out2);
            CheckEqual(15, o2.MaxGuesses, "parse: valid limit 15");
            Check(out2.Lines.Count == 0, "parse: valid limit -> no message");

            CapturingOutput out3 = new CapturingOutput();
            CommandLineOptions o3 = GameEngine.ParseCommandLineOptions(new[] { "abc" }, out3);
            CheckEqual(GameEngine.DefaultMaxGuesses, o3.MaxGuesses, "parse: 'abc' falls back to default");
            Check(out3.Contains("\"abc\" is not a valid guess limit"), "parse: 'abc' announces the fallback (no silent drop)");

            CapturingOutput out4 = new CapturingOutput();
            CommandLineOptions o4 = GameEngine.ParseCommandLineOptions(new[] { "0" }, out4);
            CheckEqual(GameEngine.DefaultMaxGuesses, o4.MaxGuesses, "parse: 0 falls back to default");
            Check(out4.Contains("is not a valid guess limit"), "parse: 0 announces the fallback");

            CapturingOutput out5 = new CapturingOutput();
            CommandLineOptions o5 = GameEngine.ParseCommandLineOptions(new[] { "999999999" }, out5);
            CheckEqual(GameEngine.MaxGuessLimitCap, o5.MaxGuesses, "parse: 999999999 clamped to the cap");
            Check(out5.Contains("above the maximum of " + GameEngine.MaxGuessLimitCap), "parse: clamp announces itself");

            CapturingOutput out6 = new CapturingOutput();
            CommandLineOptions o6 = GameEngine.ParseCommandLineOptions(new[] { "5", "42" }, out6);
            CheckEqual(5, o6.MaxGuesses, "parse: limit with seed");
            CheckEqual(42, o6.Seed, "parse: seed 42 accepted");
            Check(out6.Lines.Count == 0, "parse: valid seed -> no message");

            CapturingOutput out7 = new CapturingOutput();
            CommandLineOptions o7 = GameEngine.ParseCommandLineOptions(new[] { "5", "xyz" }, out7);
            CheckEqual(null, o7.Seed, "parse: 'xyz' seed dropped -> clock-seeded");
            Check(out7.Contains("is not a valid seed"), "parse: invalid seed announces itself");
        }

        static void Test_CustomRange_SeededTranscript()
        {
            // Seed 1, range 10-50 (41 values) -> secret 20. Exact transcript
            // proves the banner, hints and win all use the custom bounds.
            CapturingOutput output = RunGameRange(new[] { "15", "20" }, 10, 1, 10, 50);
            string expected =
                "The computer has chosen a random whole number between 10-50. You have 10 guesses. Try to guess it.\n" +
                "Guess a number: \n" +
                "Your guess was too low. Try again.\n" +
                "Guess a number: \n" +
                "Correct! You have guessed the number.";
            CheckEqual(expected, output.All(), "custom-range: exact transcript for seed 1, range 10-50 (secret 20)");
        }

        static void Test_CustomRange_EnforcesNewBounds()
        {
            // Range 10-50 (secret 20, seed 1): 5 is below the new minimum and
            // 60 above the new maximum — both rejected against 10..50, not
            // 0..99, and neither consumes the single valid guess.
            CapturingOutput output = RunGameRange(new[] { "5", "60", "20" }, 1, 1, 10, 50);
            Check(output.Contains("Please enter a number between 10 and 50."), "custom-range: bounds message uses 10-50");
            Check(!output.Contains("between 0 and 99"), "custom-range: default bounds never mentioned");
            Check(!output.Contains("too high"), "custom-range: 60 rejected, not played as 'too high'");
            Check(!output.Contains("too low"), "custom-range: 5 rejected, not played as 'too low'");
            Check(output.Contains("Correct! You have guessed the number."), "custom-range: win on the one valid guess (limit 1)");
        }

        static void Test_GuessLimitCap_ScalesWithRange()
        {
            // The cap is the size of the range: N values -> at most N guesses.
            CheckEqual(100, GameEngine.GuessLimitCap(0, 99), "cap: default 0-99 range -> 100");
            CheckEqual(41, GameEngine.GuessLimitCap(10, 50), "cap: 10-50 range -> 41");
            CheckEqual(10, GameEngine.GuessLimitCap(0, 9), "cap: 0-9 range -> 10");
            CheckEqual(1, GameEngine.GuessLimitCap(7, 7), "cap: single-value range -> 1");
            CheckEqual(GameEngine.MaxGuessLimitCap, GameEngine.GuessLimitCap(GameEngine.DefaultMinGuess, GameEngine.DefaultMaxGuess), "cap: constant agrees with the function for the default range");
        }

        static void Test_CustomRange_CapClampsLimit()
        {
            // Limit 500 on the 10-50 range must clamp to 41 (the range size),
            // announcing itself — the same visible-clamp contract as #8.
            CapturingOutput output = new CapturingOutput();
            CommandLineOptions options = GameEngine.ParseCommandLineOptions(new[] { "500", "1", "10", "50" }, output);
            CheckEqual(41, options.MaxGuesses, "custom-range cap: 500 clamped to 41");
            CheckEqual(10, options.MinGuess, "custom-range cap: min 10 parsed");
            CheckEqual(50, options.MaxGuess, "custom-range cap: max 50 parsed");
            Check(output.Contains("above the maximum of 41 for the 10-50 range"), "custom-range cap: clamp announces the range-specific maximum");
        }

        static void Test_CustomRange_ConstructorValidation()
        {
            Random rand = new Random(1);
            CapturingOutput output = new CapturingOutput();
            ScriptedInput input = new ScriptedInput();

            bool negativeMinThrown = false;
            try { new GameEngine(input, output, rand, 10, -1, 50); }
            catch (ArgumentOutOfRangeException) { negativeMinThrown = true; }
            Check(negativeMinThrown, "ctor: negative min throws ArgumentOutOfRangeException");

            bool invertedRangeThrown = false;
            try { new GameEngine(input, output, rand, 10, 50, 10); }
            catch (ArgumentOutOfRangeException) { invertedRangeThrown = true; }
            Check(invertedRangeThrown, "ctor: max == min throws ArgumentOutOfRangeException");

            bool invertedMaxBelowMinThrown = false;
            try { new GameEngine(input, output, rand, 10, 20, 19); }
            catch (ArgumentOutOfRangeException) { invertedMaxBelowMinThrown = true; }
            Check(invertedMaxBelowMinThrown, "ctor: max < min throws ArgumentOutOfRangeException");

            GameEngine engine = new GameEngine(input, output, rand, 7, 10, 50);
            CheckEqual(10, engine.MinGuess, "ctor: MinGuess property set");
            CheckEqual(50, engine.MaxGuess, "ctor: MaxGuess property set");
            CheckEqual(7, engine.MaxGuesses, "ctor: MaxGuesses property set");
        }

        static void Test_ParseCommandLineOptions_Range()
        {
            CapturingOutput out1 = new CapturingOutput();
            CommandLineOptions o1 = GameEngine.ParseCommandLineOptions(new[] { "5", "42" }, out1);
            CheckEqual(null, o1.MinGuess, "parse range: limit+seed only -> no custom range");
            CheckEqual(null, o1.MaxGuess, "parse range: limit+seed only -> MaxGuess null");
            Check(out1.Lines.Count == 0, "parse range: no range -> no message");

            CapturingOutput out2 = new CapturingOutput();
            CommandLineOptions o2 = GameEngine.ParseCommandLineOptions(new[] { "5", "42", "10", "50" }, out2);
            CheckEqual(10, o2.MinGuess, "parse range: valid pair 10 50");
            CheckEqual(50, o2.MaxGuess, "parse range: valid pair max 50");
            CheckEqual(5, o2.MaxGuesses, "parse range: limit kept");
            CheckEqual(42, o2.Seed, "parse range: seed kept");
            Check(out2.Lines.Count == 0, "parse range: valid pair -> no message");

            CapturingOutput out3 = new CapturingOutput();
            CommandLineOptions o3 = GameEngine.ParseCommandLineOptions(new[] { "5", "42", "-5", "50" }, out3);
            CheckEqual(null, o3.MinGuess, "parse range: negative min -> default range");
            Check(out3.Contains("Guess range minimum must be 0 or greater"), "parse range: negative min announces the fallback");

            CapturingOutput out4 = new CapturingOutput();
            CommandLineOptions o4 = GameEngine.ParseCommandLineOptions(new[] { "5", "42", "50", "10" }, out4);
            CheckEqual(null, o4.MaxGuess, "parse range: max <= min -> default range");
            Check(out4.Contains("must be greater than the minimum"), "parse range: inverted range announces the fallback");

            CapturingOutput out5 = new CapturingOutput();
            CommandLineOptions o5 = GameEngine.ParseCommandLineOptions(new[] { "5", "42", "10" }, out5);
            CheckEqual(null, o5.MinGuess, "parse range: lone min (no max) -> default range");
            Check(out5.Contains("is not a valid guess range"), "parse range: lone min announces the fallback");

            CapturingOutput out6 = new CapturingOutput();
            CommandLineOptions o6 = GameEngine.ParseCommandLineOptions(new[] { "5", "42", "abc", "50" }, out6);
            CheckEqual(null, o6.MinGuess, "parse range: non-numeric min -> default range");
            Check(out6.Contains("is not a valid guess range"), "parse range: non-numeric range announces the fallback");
        }
    }
}
