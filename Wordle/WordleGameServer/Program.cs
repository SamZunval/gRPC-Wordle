using Grpc.Core;
using Grpc.Net.Client;
using WordleGameServer.Protos;
using WordleGameServer.Services;
using WordServer.Protos;
/*
 INFO-5060: Wordle Game
 Connor Tidy and Samuel Zuniga Valencia
 A basic Program file to set up the grpc
 */
namespace WordleGameServer
{
    public class Program
    {
        public static void Main(string[] args)
        {            
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddGrpc();

            var app = builder.Build();

            // Configure the HTTP request pipeline.
            app.MapGrpcService<WordleServerService>();
            app.MapGet("/", () => "Communication with gRPC endpoints must be made through a gRPC client. To learn how to create a client, visit: https://go.microsoft.com/fwlink/?linkid=2086909");

            app.Run();
        }
        
    }
}