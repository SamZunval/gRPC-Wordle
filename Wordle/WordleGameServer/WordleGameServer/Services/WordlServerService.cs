using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using WordleGameServer.Protos;
using WordServer.Protos;

namespace WordleGameServer.Services
{
    public class WordlServerService : WordlServer.WordlServerBase
    {
        public override async Task Play(IAsyncStreamReader<GuessRequest> requestStream, IServerStreamWriter<GuessResponse> responseStream, ServerCallContext context)
        {
            // Session counters
            GuessResponse response = new()
            {
                Correct = false,
                GameOver = false,
            };
            uint turnNumber = 0;
            string wordToGuess = GetCurrentWord();//todo: add null check
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
            while (!response.Correct && await requestStream.MoveNext() && !context.CancellationToken.IsCancellationRequested && turnNumber < 6)
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
                        //todo: write to file
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

                Empty request = new Empty();

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
