using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using System.Runtime;
using System.Text;
using WordleGameServer.Protos;
using WordServer.Protos;

namespace WordleGameServer.Services
{
    public class WordleServerService : WordleServer.WordleServerBase
    {
        private static Mutex mut = new Mutex();
        public override Task<StatResponse> GetStats(Protos.Empty request, ServerCallContext context)
        {
            int[] stats = ParseCSV("stats.csv");
            int pass = 0;
            for (int i = 1; i < stats.Length; i++)
            {
                pass += stats[i];
            }
            //should prevent divide by zero
            float divisor = stats[0] == 0 ? 1.0f : (float)stats[0];
            StatResponse response = new StatResponse()
            {
                Players = (uint)stats[0],
                Winners = (float)pass / divisor,//correct players divided by all players
                One = (uint)stats[1],
                Two = (uint)stats[2],
                Three = (uint)stats[3],
                Four = (uint)stats[4],
                Five = (uint)stats[5],
                Six = (uint)stats[6],
            };
            return Task.FromResult(response);
        }

        public override async Task Play(IAsyncStreamReader<GuessRequest> requestStream, IServerStreamWriter<GuessResponse> responseStream, ServerCallContext context)
        {
            // Session counters
            GuessResponse response = new()
            {
                Correct = false,
                GameOver = false,
            };

            uint turnNumber = 0;
            string wordToGuess = GetCurrentWord();
            char[] results = new char[5];
            Dictionary<char, bool> unused = new Dictionary<char, bool>();
            List<char> included = new List<char>();
            List<char> excluded = new List<char>();
            for (int i = 0; i < 26; i++)
            {
                unused.Add((char)('a' + i), true);//add all letter lazily
            }
            // Repeatedly wait for a guess from the client stream
            // requestStream.MoveNext() will return false when the client closes the request stream
            while (wordToGuess != "" && !response.Correct && await requestStream.MoveNext() && !context.CancellationToken.IsCancellationRequested && turnNumber < 6)
            {
                // Record the outcome of the previous question
                GuessRequest request = requestStream.Current;
                if (isValidWord(request.Word))
                {
                    turnNumber++;
                    for (int i = 0; i < 5; i++)
                    {
                        unused[request.Word[i]] = false;
                    }
                    if (request.Word.Equals(wordToGuess))
                    {
                        //add word to list of included chars
                        for (int i = 0; i < 5; i++)
                        {
                            if (!included.Contains(request.Word[i]))
                            {
                                included.Add(request.Word[i]);
                            }
                        }
                        response.Correct = true;
                        response.GameOver = false;
                        response.Result = "*****";
                        response.Unused = GetStringFromDictionary(unused);
                        response.Included = new string(included.ToArray());
                        response.Excluded = new string(excluded.ToArray());
                        //write to file
                        int[] stats = ParseCSV(wordToGuess + ".csv");

                        DateTime todaysDate = DateTime.Now.Date;
                        if (stats[9] == todaysDate.Year && stats[8] == todaysDate.Month && stats[7] == todaysDate.Day)
                        {
                            stats[0] = stats[0] + 1; //update player count
                            stats[turnNumber] = stats[turnNumber] + 1; //update stat at turn number
                        }
                        else
                        {
                            //new day
                            stats[0] = 1; //1 player
                            stats[1] = 0; // 1 attempt
                            stats[2] = 0; // 2 attempt
                            stats[3] = 0; // 3 attempt
                            stats[4] = 0; // 4 attempt
                            stats[5] = 0; // 5 attempt
                            stats[6] = 0; // 6 attempt
                            stats[7] = todaysDate.Day; //day
                            stats[8] = todaysDate.Month; // month
                            stats[9] = todaysDate.Year; // year

                            stats[turnNumber] = stats[turnNumber] + 1; //update stat at turn number
                        }
                        WriteCSV("stats.csv", stats);
                    }
                    else
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            if (request.Word[i] == wordToGuess[i])//right
                            {
                                results[i] = '*';
                                if (!included.Contains(request.Word[i]))
                                {
                                    included.Add(request.Word[i]);
                                }
                                unused[request.Word[i]] = false;
                            }
                            else if (wordToGuess.Contains(request.Word[i]))//wrong location
                            {
                                results[i] = '?';
                                if (!included.Contains(request.Word[i]))
                                {
                                    included.Add(request.Word[i]);
                                }
                                unused[request.Word[i]] = false;
                            }
                            else//wrong
                            {
                                results[i] = 'x';
                                if (!excluded.Contains(request.Word[i]))
                                {
                                    excluded.Add(request.Word[i]);
                                }
                                unused[request.Word[i]] = false;
                            }
                        }
                        response.Correct = false;
                        response.GameOver = (turnNumber == 6) ? true : false;//if last turn then game over
                        response.Result = new string(results);
                        response.Unused = GetStringFromDictionary(unused);
                        response.Included = new string(included.ToArray());
                        response.Excluded = new string(excluded.ToArray());
                        if (response.GameOver)
                        {
                            int[] stats = ParseCSV(wordToGuess + ".csv");
                            DateTime todaysDate = DateTime.Now.Date;
                            if (stats[9] == todaysDate.Year && stats[8] == todaysDate.Month && stats[7] == todaysDate.Day)
                            {
                                stats[0] = stats[0] + 1;//update player count
                            }
                            else
                            {
                                //new day
                                stats[0] = 1; //1 player
                                stats[1] = 0; // 1 attempt
                                stats[2] = 0; // 2 attempt
                                stats[3] = 0; // 3 attempt
                                stats[4] = 0; // 4 attempt
                                stats[5] = 0; // 5 attempt
                                stats[6] = 0; // 6 attempt
                                stats[7] = todaysDate.Day; //day
                                stats[8] = todaysDate.Month; // month
                                stats[9] = todaysDate.Year; // year

                            }
                            WriteCSV("stats.csv", stats);

                        }
                    }
                }
                else
                {
                    //not a valid word or word server is down
                    response.Correct = false;
                    response.GameOver = (turnNumber == 6) ? true : false;//if last turn then game over
                    response.Result = "error";
                    response.Unused = GetStringFromDictionary(unused);
                    response.Included = new string(included.ToArray());
                    response.Excluded = new string(excluded.ToArray());
                }
                
                // Send the response message to the client 
                await responseStream.WriteAsync(response);
            }
        }
        public static void WriteCSV(string file, int[] stats)
        {
            string workingDirectory = Environment.CurrentDirectory;
            string projectDirectory = Directory.GetParent(workingDirectory).FullName;
            string statFile = projectDirectory + "\\Data\\" + file;
            var csv = new StringBuilder();
            mut.WaitOne();
            try
            {
                string newLine = "";
                for (int i = 0; i < stats.Length; i++)
                {
                    newLine += stats[i].ToString()+",";
                }
                csv.AppendLine(newLine);
                File.WriteAllText(statFile, csv.ToString());
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred", ex);
            }
            mut.ReleaseMutex();
        }
        public static int[] ParseCSV(string file)
        {
            int[] result = new int[10];//player count + 6 guess distributions + 3(day,month,year)
            string workingDirectory = Environment.CurrentDirectory;
            string projectDirectory = Directory.GetParent(workingDirectory).FullName;
            string statFile = projectDirectory + "\\Data\\" + file;
            if (!File.Exists(statFile))
            {
                return result;
            }
            string[] lines = File.ReadAllLines(statFile);
            string[] thisLine = lines[0].Split(',');
            for(int i = 0;i < 10; i++)
            {
                result[i] = int.Parse(thisLine[i]);
            }

            return result;
        }
        public string GetStringFromDictionary(Dictionary<char,bool> dict)
        {
            string result = string.Empty;
            foreach(var letter in dict)
            {
                if (letter.Value)
                {
                    result += letter.Key;
                }
            }
            return result;
        }
        public string GetCurrentWord()
        {
            string result = "";
            try
            {
                // Connect to the service
                // Note that the port number 7047 has to match the port number selected for https
                // by the service project's launchSettings module (found in the Properties folder).
                var channel = GrpcChannel.ForAddress("https://localhost:7163");
                var server = new DailyWord.DailyWordClient(channel);

                WordServer.Protos.Empty request = new ();

                WordResponse word = server.GetWord(request);
                //Console.WriteLine("Word: " + word.ToString());

                result = new string(word.Word);
            }
            catch (RpcException)
            {
                Console.WriteLine("\nERROR: The daily word service is not currently available.");
            }
            return result;
        }
        public bool isValidWord(string guess)
        {
            try
            {
                // Connect to the service
                // Note that the port number 7047 has to match the port number selected for https
                // by the service project's launchSettings module (found in the Properties folder).
                var channel = GrpcChannel.ForAddress("https://localhost:7163");
                var server = new DailyWord.DailyWordClient(channel);

                WordRequest request = new WordRequest()
                {
                    Word = guess
                };

                ValidateResponse valid = server.ValidateWord(request);
                //Console.WriteLine("Word: " + word.ToString());

                return valid.IsValid;
            }
            catch (RpcException)
            {
                Console.WriteLine("\nERROR: The daily word service is not currently available.");
            }
            return false;
        }
    }
}
