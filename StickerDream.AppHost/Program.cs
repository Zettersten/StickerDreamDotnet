using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

// Add server project with dev tunnel for public internet access
var server = builder.AddProject("server", "../StickerDream.Server/StickerDream.Server.csproj")
    .WithHttpEndpoint(env: "ASPIRE_ENVIRONMENT");

builder.Build().Run();
