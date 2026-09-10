using System;

namespace BasicGuessingGame
{
    internal class Program
    {
        // The secret is drawn with rand.Next(MinGuess, MaxGuess + 1), i.e. an
        // inclusive whole number in this range. (Issue #3: this range is what
        // the player is told to guess within, and what we now enforce.)
        const int MinGuess = 0;
        const int MaxGuess = 99;

        // How many valid guesses the player gets per round unless told otherwise
        // on the command line. (Issue #4: difficulty / guess limit.)
        const int DefaultMaxGuesses = 10;

        static void Main(string[] args)
        {
            // Optional guess limit from the command line, e.g. "Guess.exe 15".
            // A non-numeric or non-positive value falls back to the default.
            int maxGuesses = DefaultMaxGuesses;
            if (args.Length > 0 && int.TryParse(args[0], out int parsedLimit) && parsedLimit > 0)
            {
                maxGuesses = parsedLimit;
            }

            // Seed the Random ONCE, outside the play-again loop. Re-creating a
            // Random inside a tight loop on .NET Framework can seed from the
            // same clock tick and repeat the secret number (issue #4).
            Random rand = new Random();

            bool playAgain = true;
            while (playAgain)
            {
                PlayOneRound(rand, maxGuesses);

                Console.WriteLine("Play again? (y/n)");
                string again = Console.ReadLine();
                if (again == null)                                        // EOF at the prompt
                {
                    Console.WriteLine("Input closed. Goodbye!");
                    playAgain = false;
                }
                else
                {
                    string answer = again.Trim().ToLowerInvariant();
                    playAgain = (answer == "y" || answer == "yes");
                    if (!playAgain)
                    {
                        Console.WriteLine("Thanks for playing!");
                    }
                }
            }
        }

        static void PlayOneRound(Random rand, int maxGuesses)
        {
            int secretNumber = rand.Next(MinGuess, MaxGuess + 1);         // Random whole number in [MinGuess, MaxGuess].
            int attemptsUsed = 0;                                         // Only valid, in-range guesses count against the limit.
            bool won = false;

            Console.WriteLine(
                "The computer has chosen a random whole number between " + MinGuess + "-" + MaxGuess +
                ". You have " + maxGuesses + " guesses. Try to guess it.");

            while (!won && attemptsUsed < maxGuesses)
            {
                Console.WriteLine("Guess a number: ");
                string input = Console.ReadLine();                        // Read the user's input as a string.
                if (input == null)                                        // null means the input stream has been closed (EOF).
                {
                    Console.WriteLine("Input closed. Goodbye!");
                    break;                                                // End the round; the outer loop exits too.
                }

                if (int.TryParse(input, out int guess) == false)          // Not a whole number...
                {
                    Console.WriteLine("Invalid number. Please enter a whole number.");
                    continue;                                             // Ask again without counting it against the limit.
                }

                if (guess < MinGuess || guess > MaxGuess)                 // Issue #3: enforce the stated range.
                {
                    Console.WriteLine("Please enter a number between " + MinGuess + " and " + MaxGuess + ".");
                    continue;                                             // Ask again without counting it against the limit.
                }

                attemptsUsed++;                                           // A real guess: it now counts against the limit.

                if (guess == secretNumber)
                {
                    Console.WriteLine("Correct! You have guessed the number.");
                    won = true;                                           // Player wins; stop the round.
                }
                else if (guess < secretNumber)
                {
                    Console.WriteLine("Your guess was too low. Try again.");
                }
                else
                {
                    Console.WriteLine("Your guess was too high. Try again.");
                }
            }

            if (!won && attemptsUsed >= maxGuesses)
            {
                // Ran out of guesses (issue #4: the player can now lose).
                Console.WriteLine("Sorry, you ran out of guesses. The number was " + secretNumber + ".");
            }
        }
    }
}
