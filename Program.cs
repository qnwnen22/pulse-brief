using PulseBrief;

var app = WebApplicationBootstrap.Build(new WebApplicationOptions { Args = args });
await app.RunAsync();
