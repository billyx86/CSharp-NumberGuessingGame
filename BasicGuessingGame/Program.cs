using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BasicGuessingGame
{
    internal class Program
    {
        static void Main(string[] args)
        {
            Random rand = new Random();
            int secretNumber = rand.Next(0, 100);                                             // Selects a random number between 0-100, stores it as "secretNumber".
            bool guessedNumber = false;                                                       // A boolean variable to record whether the player has guessed correctly or not.
            Console.WriteLine("The computer has chosen a random whole number between 0-100. Try to guess it.");

            while (guessedNumber == false)                                                    // The boolean variable is used to loop this while loop until the player has guessed the number.
            {
                Console.WriteLine("Guess a number: ");
                int.TryParse(Console.ReadLine(), out int guess);                              // Converts the string into an integer, and outputs it into the integer variable "guess".
                
                if (guess == secretNumber)
                {
                    Console.WriteLine("Correct! You have guessed the number.");
                    guessedNumber = true;                                                     // Stops the loop as the player has won the game.
                }
                else if (guess < secretNumber)
                {
                    Console.WriteLine("Your guess was too low. Try again.");
                }
                else if (guess > secretNumber)
                {
                    Console.WriteLine("Your guess was too high. Try again.");
                }
            }

            Console.ReadLine();
        }
    }
}