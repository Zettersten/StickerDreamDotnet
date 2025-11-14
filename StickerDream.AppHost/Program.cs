var builder = DistributedApplication.CreateBuilder(args);

var server = builder.AddProject<Projects.StickerDream_Server>("server");

builder.Build().Run();
