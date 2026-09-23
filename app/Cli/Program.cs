using System.Text.Json;
using SpeakForever;
using SpeakForever.Cli.Commands;
using SpeakForever.Configuration;
using SpeakForever.Logging;

// Headless Speak Forever plus the setup checks. The WinUI app is the everyday front end.
AppPaths.MigrateFromOldName();
Log.ToConsole();
Log.ToFile("speakforever-cli.log");

using var quit = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true; // shut down cleanly: release the controller and flush the log
    quit.Cancel();
};

Config cfg;
Engine engine;
try
{
    cfg = await Config.LoadOrCreateAsync(quit.Token);
    engine = new Engine(cfg);
}
catch (Exception e) when (e is FormatException or JsonException)
{
    Log.Warn($"Config error in {AppPaths.Config}: {e.Message}");
    return 1;
}
Log.Info($"Config: {AppPaths.Config}");

await using (engine)
{
    try
    {
        return await RunAsync();
    }
    catch (OperationCanceledException) when (quit.IsCancellationRequested)
    {
        return 0;
    }
}

async Task<int> RunAsync()
{
    if (args is ["--benchmark", ..])
        return await Benchmark.RunAllAsync(args.Length > 1 ? args[1] : null, quit.Token);
    if (args is ["--benchmark-one", var benchModel, var beam, ..])
        return await Benchmark.RunOneAsync(cfg, benchModel, int.Parse(beam, System.Globalization.CultureInfo.InvariantCulture), quit.Token);

    if (args is ["--test-type", ..])
        return await Diagnostics.TestTypeAsync(cfg, args.Length > 1 ? string.Join(' ', args[1..]) : "Hello from Speak Forever!", quit.Token);

    if (args.Contains("--probe"))
    {
        engine.Start(probe: true);
        await Task.Delay(Timeout.Infinite, quit.Token);
    }

    var model = args is ["--model", var m, ..] ? m : engine.StartingModel();
    if (model is null || !File.Exists(model))
    {
        Log.Warn(model is null ? "No speech model yet." : $"Whisper model not found: {model}");
        Log.Warn($"Download one in the Speak Forever app, or put a ggml model from https://huggingface.co/ggerganov/whisper.cpp in {AppPaths.Models}.");
        return 1;
    }
    await engine.LoadModelAsync(model, quit.Token);
    if (engine.LoadedModel is null) return 1;

    string[] rest = args is ["--model", _, ..] ? args[2..] : args;
    if (rest is ["--test-mic", ..])
        return await Diagnostics.TestMicAsync(engine, quit.Token);
    if (rest is ["--transcribe", var file, ..])
        return await Diagnostics.TranscribeFileAsync(engine, file, quit.Token);

    Log.Info($"Open chat with {cfg.OpenChatChord}, then {cfg.DictateChord} to dictate. Typing into: {string.Join(", ", cfg.ProcessNames)}");
    if (!engine.Start()) return 1;
    Log.Info("Ctrl+C to quit.");
    await Task.Delay(Timeout.Infinite, quit.Token);
    return 0;
}
