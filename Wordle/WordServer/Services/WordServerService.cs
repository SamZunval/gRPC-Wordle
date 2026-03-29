using Grpc.Core;
using System;
using System.Text.Json;
using WordServer.Protos;
using WordServer.Services;

namespace WordServer.Services
{
    public class WordServerService: DailyWord.DailyWordBase
    {
        //List to contain all the words from the json doc 
        private static List<string> words = new List<string>();

        //Todays word
        private static string todaysWord = "";
        
        //Here when the class is create we will get all the words from the JSON file
        //and set the words and todaysWord fields with there respective day
        public WordServerService() 
        {
            try
            {
                if (words.Count == 0)
                {
                    string json = File.ReadAllText("Data/wordle.json");
                    words = JsonSerializer.Deserialize<List<string>>(json);
                }

                //Now get the random word from the list using the day as the seed
                Random rnd = new Random(DateTime.Now.Date.GetHashCode());

                int index = rnd.Next(words.Count());
                todaysWord = words[index];
            }
            catch (FileNotFoundException e)
            {
                Console.WriteLine("The file was not found");
                Console.WriteLine(e);
            }
        }

        public override Task<WordResponse> GetWord(Empty request, ServerCallContext context)
        {
            WordResponse response = new WordResponse();
            response.Word = todaysWord;

            return Task.FromResult(response);
        }

        public override Task<ValidateResponse> ValidateWord(WordRequest request, ServerCallContext context)
        {
            ValidateResponse response = new ValidateResponse();
            bool exists = words.Contains(request.Word.ToLower());

            response.IsValid = exists;
            return Task.FromResult(response);

        }

    }
}
