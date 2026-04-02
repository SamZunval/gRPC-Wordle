using System;
using System.Threading.Tasks;
using Grpc.Core;
using Grpc.Net.Client;
using WordleGameServer.Protos;

namespace WordleGameClient
{
    class Program
    {
        static async Task Main(string[] args)
        {
            using var channel = GrpcChannel.ForAddress("https://localhost:7253");
            var client = new WordleServer.WordleServerClient(channel);

            Console.WriteLine(" Welcome to Wordle!");
            try
            {
                //Start the streaming call
                using (var call = client.Play())
                {
                    bool gameOver = false;

                    while (!gameOver)
                    {
                        //Get input
                        Console.Write("\nEnter a 5-letter word: ");
                        var guess = Console.ReadLine()?.ToLower();

                        //Validate imput
                        if (string.IsNullOrWhiteSpace(guess) || guess.Length != 5)
                        {
                            Console.WriteLine("Invalid input. Must be 5 letters.");
                            continue;
                        }

                        //Send guess
                        await call.RequestStream.WriteAsync(new GuessRequest
                        {
                            Word = guess
                        });

                        //Wait for the server to respond
                        if (await call.ResponseStream.MoveNext())
                        {
                            var response = call.ResponseStream.Current;

                            //Checks if error(invalid word)
                            if (response.Result.Contains("error"))
                            {
                                Console.WriteLine("The word is invalid, please try again");
                                continue;
                            }
                            //Checks if rpc error(word server down)
                            if (response.Result.Contains("rpc"))
                            {
                                Console.WriteLine("Error: The word server is not running");
                                Console.WriteLine("\nPress any key to exit...");
                                Console.ReadKey();
                                return;
                            }
                            //print information about the guess
                            Console.WriteLine($"\nResult: {response.Result}");
                            Console.WriteLine($"Included: {response.Included}");
                            Console.WriteLine($"Excluded: {response.Excluded}");
                            Console.WriteLine($"Unused: {response.Unused}");

                            if (response.Correct)
                            {
                                Console.WriteLine("Correct word!");
                                gameOver = true;
                            }
                            else if (response.GameOver)
                            {
                                Console.WriteLine("Game Over!");
                                gameOver = true;
                            }
                        }
                    }

                    await call.RequestStream.CompleteAsync();//close the game

                    var stats = client.GetStats(new Empty());//get stats from rpc
                    //display statistics to the client
                    Console.WriteLine("\n--- GAME STATS ---");
                    Console.WriteLine($"Players: {stats.Players}");
                    Console.WriteLine($"Win Rate: {stats.Winners:P}");

                    Console.WriteLine("\nGuess Distribution:");
                    Console.WriteLine($"1 guess: {stats.One}");
                    Console.WriteLine($"2 guesses: {stats.Two}");
                    Console.WriteLine($"3 guesses: {stats.Three}");
                    Console.WriteLine($"4 guesses: {stats.Four}");
                    Console.WriteLine($"5 guesses: {stats.Five}");
                    Console.WriteLine($"6 guesses: {stats.Six}");

                    Console.WriteLine("\nPress any key to exit...");
                    Console.ReadKey();
                }
            }
            catch (RpcException ex) {
                Console.WriteLine("Error, the game server is not running");
                Console.WriteLine("\nPress any key to exit...");
                Console.ReadKey();
            }

        }
        
    }
}