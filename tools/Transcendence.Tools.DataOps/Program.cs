using Transcendence.Tools.DataOps;

return await ProgramEntry.RunAsync(args);

internal static class ProgramEntry
{
    public static async Task<int> RunAsync(string[] args)
    {
        try
        {
            var parsed = CommandArguments.Parse(args);
            var command = parsed.Command?.Trim().ToLowerInvariant();

            if (parsed.HasFlag("help"))
                return PrintUsage(command);

            return command switch
            {
                null or "" or "help" or "--help" or "-h" => PrintUsage(),
                "slice-sync" => await DataSliceSyncCommand.RunAsync(parsed),
                "validate-identifiers" => await IdentifierValidationCommand.RunAsync(parsed),
                "rehydrate-riot-ids" => await RiotIdentifierRehydrateCommand.RunAsync(parsed),
                _ => Fail($"Unknown command '{parsed.Command}'.")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int PrintUsage(string? command = null)
    {
        var normalizedCommand = command?.Trim().ToLowerInvariant();

        if (normalizedCommand == "slice-sync")
        {
            Console.WriteLine("""
                              slice-sync
                                Copy a game-only slice from a source Postgres database into a local/disposable target.
                                Options:
                                  --source <connection-string>
                                  --target <connection-string>
                                  --regions <csv>                          default: NA1,EUW1,KR
                                  --patch-depth <n>                        default: 2
                                  --lol-max-matches-per-region <n>         default: 5000
                                  --lol-sample-percent <0-100>             default: 100
                                  --tft-max-matches-per-region <n>         default: 3000
                                  --tft-sample-percent <0-100>             default: 100
                                  --skip-lol
                                  --skip-tft
                              """);
            return 0;
        }

        if (normalizedCommand == "validate-identifiers")
        {
            Console.WriteLine("""
                              validate-identifiers
                                Print app lookup policy, database identifier stats, and optional live Riot validation.
                                Options:
                                  --db <connection-string>
                                  --sample-size <n>                        default: 5
                                  --skip-live
                              """);
            return 0;
        }

        if (normalizedCommand == "rehydrate-riot-ids")
        {
            Console.WriteLine("""
                              rehydrate-riot-ids
                                Refresh non-canonical Riot encrypted identifiers and Riot IDs from stored PUUIDs.
                                Options:
                                  --target <connection-string>
                                  --games <all|lol|tft>                    default: all
                                  --limit <n>                              default: 100
                                  --delay-ms <n>                           default: 75
                                  --only-missing
                              """);
            return 0;
        }

        Console.WriteLine("""
                          Transcendence data ops

                          Commands:
                            slice-sync
                            validate-identifiers
                            rehydrate-riot-ids

                          Use:
                            <command> --help

                          Environment variables:
                            TRN_SOURCE_DB / TRN_TARGET_DB
                            TRN_RIOT_API_KEY_LOL / RIOT_API_KEY_LOL
                            TRN_RIOT_API_KEY_TFT / RIOT_API_KEY_TFT
                          """);
        return 0;
    }

    private static int Fail(string message)
    {
        Console.Error.WriteLine(message);
        return 1;
    }
}
