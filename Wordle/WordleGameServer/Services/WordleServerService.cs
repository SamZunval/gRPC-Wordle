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
        private static Mutex mut = new Mutex();//to prevent two instences from accessing the file at the same time
        public override Task<StatResponse> GetStats(Protos.Empty request, ServerCallContext context)
        {
            //get stats from the file
            int[] stats = ParseCSV("stats.csv");
            int pass = 0;
            //add all guesses from the attempts ranges
            for (int i = 1; i < 7; i++)
            {
                pass += stats[i];
            }
            //should prevent divide by zero
            float divisor = stats[0] == 0 ? 1.0f : (float)stats[0];
            StatResponse response = new StatResponse()
            {
                Players = (uint)stats[0],
                Winners = (float)pass / divisor,//correct players divided by all players
                One = (uint)stats[1],//first try
                Two = (uint)stats[2],//second try
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
            string wordToGuess = GetCurrentWord();//get word from server
            char[] results = new char[5];//holds results like x?***
            Dictionary<char, bool> unused = new Dictionary<char, bool>();//stores all unused letters
            List<char> included = new List<char>();//stores letters that are in the word
            List<char> excluded = new List<char>();//stores letter that are not in the word
            for (int i = 0; i < 26; i++)
            {
                unused.Add((char)('a' + i), true);//add all letter lazily
            }
            // Repeatedly wait for a guess from the client stream
            // requestStream.MoveNext() will return false when the client closes the request stream
            while (wordToGuess != "" && !response.Correct && await requestStream.MoveNext() && !context.CancellationToken.IsCancellationRequested && turnNumber < 6)
            {
                GuessRequest request = requestStream.Current;
                if (IsValidWord(request.Word))//check that the word is valid
                {
                    turnNumber++;//new attempt/turn
                    for (int i = 0; i < 5; i++)
                    {
                        unused[request.Word[i]] = false;//update unused dictionary
                    }
                    if (request.Word.Equals(wordToGuess))//exact match(winner)
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
                        response.GameOver = false;//gameover is for ran out of turns
                        response.Result = "*****";
                        response.Unused = GetStringFromDictionary(unused);//get unused letters from dictionary(useless in this case because user has won)
                        response.Included = new string(included.ToArray());//get included letters from list(useless in this case because user has won)
                        response.Excluded = new string(excluded.ToArray());//get Excluded letters from list(useless in this case because user has won)
                        //write to file
                        int[] stats = ParseCSV("stats.csv");

                        DateTime todaysDate = DateTime.Now.Date;
                        //check if the file date is the current date
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
                        WriteCSV("stats.csv", stats);//write to file
                    }
                    else
                    {
                        for (int i = 0; i < 5; i++)
                        {
                            if (request.Word[i] == wordToGuess[i])//right letter and location
                            {
                                results[i] = '*';//add a star at the location
                                if (!included.Contains(request.Word[i]))
                                {
                                    included.Add(request.Word[i]);//add to included list
                                }
                            }
                            else if (wordToGuess.Contains(request.Word[i]))//right letter wrong location
                            {
                                results[i] = '?';//add a ? at the location
                                if (!included.Contains(request.Word[i]))
                                {
                                    included.Add(request.Word[i]);//add to included list
                                }
                            }
                            else//wrong
                            {
                                results[i] = 'x';//add a x at the location
                                if (!excluded.Contains(request.Word[i]))
                                {
                                    excluded.Add(request.Word[i]);//add to excluded list
                                }
                            }
                        }
                        response.Correct = false;//not the correct word
                        response.GameOver = (turnNumber == 6) ? true : false;//if last turn then game over
                        response.Result = new string(results);//the resuult string to display to users
                        response.Unused = GetStringFromDictionary(unused);//get the unused letters from the dictionary
                        response.Included = new string(included.ToArray());//get included letters from list
                        response.Excluded = new string(excluded.ToArray());//get Excluded letters from list

                        //game over update file
                        if (response.GameOver)
                        {
                            //get daily stats from file
                            int[] stats = ParseCSV("stats.csv");
                            DateTime todaysDate = DateTime.Now.Date;
                            //check if the file is for the right date
                            if (stats[9] == todaysDate.Year && stats[8] == todaysDate.Month && stats[7] == todaysDate.Day)
                            {
                                stats[0] = stats[0] + 1;//did not win so only update player count
                            }
                            else //wrong date, overwrite file
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
                            WriteCSV("stats.csv", stats);//write to file

                        }
                    }
                }
                else
                {
                    //not a valid word
                    response.Correct = false;
                    response.GameOver = (turnNumber == 6) ? true : false;//if last turn then game over
                    response.Result = "error";//send error instead of normal result to inform client that the word is not valid
                    response.Unused = GetStringFromDictionary(unused);//re send the data from last attempt
                    response.Included = new string(included.ToArray());//re send the data from last attempt
                    response.Excluded = new string(excluded.ToArray());//re send the data from last attempt
                }
                
                // Send the response message to the client 
                await responseStream.WriteAsync(response);
            }
            if(wordToGuess == "")
            {
                //word server is down
                response.Result = "rpc";// send rpc if the wordToGuess is empty(word server down)
                //empty data
                response.Correct = false;
                response.GameOver = (turnNumber == 6) ? true : false;//if last turn then game over
                response.Unused = GetStringFromDictionary(unused);//re send the data from last attempt
                response.Included = new string(included.ToArray());//re send the data from last attempt
                response.Excluded = new string(excluded.ToArray());//re send the data from last attempt

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
            mut.WaitOne();//lock file
            try
            {
                string newLine = "";
                //stats: [0] : count, [1 -6]: attempts/guesses, [7-9]: date
                for (int i = 0; i < stats.Length; i++)
                {
                    newLine += stats[i].ToString()+",";//add each stat value to the string seperated by commas
                }
                csv.AppendLine(newLine);//write line to string builder
                File.WriteAllText(statFile, csv.ToString());//overwrite file with new data
            }
            catch (Exception ex)
            {
                throw new Exception("An error occurred", ex);
            }
            mut.ReleaseMutex();//release file
        }
        //gets data from the csv file
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
            mut.WaitOne();//lock file
            string[] lines = File.ReadAllLines(statFile);//read file to string array
            mut.ReleaseMutex();//release file
            string[] thisLine = lines[0].Split(',');//only the first line of the file is used, split the line on every comma (csv file)
            //thisLine: [0] : count, [1 -6]: attempts/guesses, [7-9]: date
            for (int i = 0;i < 10; i++)
            {
                result[i] = int.Parse(thisLine[i]);//convert string results into an int array
            }

            return result;
        }
        //returns all unused letters from the dictionary
        public string GetStringFromDictionary(Dictionary<char,bool> dict)
        {
            string result = string.Empty;
            foreach(var letter in dict)
            {
                if (letter.Value)//check for the value to be true(meaning the letter is not used)
                {
                    result += letter.Key;//add the letter to the string
                }
            }
            return result;
        }
        //gets the word of the day from the word server
        public string GetCurrentWord()
        {
            string result = "";
            try
            {
                // Connect to the service
                var channel = GrpcChannel.ForAddress("https://localhost:7163");
                var server = new DailyWord.DailyWordClient(channel);

                WordServer.Protos.Empty request = new ();//empty request

                WordResponse word = server.GetWord(request);//call rpc

                result = new string(word.Word);//return the word of the day
            }
            catch (RpcException)
            {
                Console.WriteLine("\nERROR: The daily word service is not currently available.");
            }
            return result;
        }
        //asks the word server if a word is valid
        public bool IsValidWord(string guess)
        {
            try
            {
                // Connect to the service
                var channel = GrpcChannel.ForAddress("https://localhost:7163");
                var server = new DailyWord.DailyWordClient(channel);

                //send the guess to the server to check
                WordRequest request = new WordRequest()
                {
                    Word = guess
                };

                ValidateResponse valid = server.ValidateWord(request);//recieve server response

                return valid.IsValid;//return whether the server thinks the word is valid
            }
            catch (RpcException)
            {
                Console.WriteLine("\nERROR: The daily word service is not currently available.");
            }
            return false;
        }
    }
}
