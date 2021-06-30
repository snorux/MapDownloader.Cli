using MapDownloader.Model;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using ICSharpCode.SharpZipLib.BZip2;

namespace MapDownloader
{
    partial class Program
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        static int SetupDownload(string input, string? output, string? csv, bool multithread)
        {
            if (!IsValidFile(input, ".json"))
                return 1;

            JsonModel jsonModel = new JsonModel();
            try
            {
                string jsonFile = File.ReadAllText(input);
                jsonModel = JsonSerializer.Deserialize<JsonModel>(jsonFile);
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[bold red]Error found while trying to read or deserialize json:[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                return 1;
            }

            if (jsonModel != null)
            {
                // Validate the JsonModel first?:
                // Read from csv and store it first:
                // Check if output is empty, if it isn't override the file directory:
                // Read from directory and get the files if it doesn't exist, download them:
                if (!jsonModel.FastDL.IsValidURL())
                {
                    AnsiConsole.MarkupLine("[red]ERROR:[/] [grey]Invalid FastDL URL in .json file... exiting...[/]");
                    return 1;
                }

                if (!jsonModel.MapList.IsValidURL())
                {
                    AnsiConsole.MarkupLine("[red]ERROR:[/] [grey]Invalid \".csv\" URL in .json file... exiting...[/]");
                    return 1;
                }

                AnsiConsole.MarkupLine("[red]WARN:[/] [grey]For the best experience, [bold underline]DO NOT[/] exit the console...[/]");

                string[] csvResult = null;
                string[] downloadedMaps = null;
                List<string> missingMaps = new List<string>();

                AnsiConsole.Status()
                    .AutoRefresh(true)
                    .Spinner(Spinner.Known.BouncingBar)
                    .Start("[yellow]Performing initial setup...[/]", ctx =>
                    {
                        WriteLogMessage("Reading from csv file");
                        csvResult = ReadFromCSV(jsonModel.MapList, csv);

                        WriteLogMessage("Reading maps from local directory");
                        downloadedMaps = ReadFromDirectory(jsonModel.OutputDirectory, output);

                        ctx.Status("[lime]Comparing maps from csv file and local directory...[/]");
                        WriteLogMessage("Getting list of missing maps");
                        foreach (string map in csvResult)
                        {
                            if (!downloadedMaps.Any(x => map.ToLower().Contains(x)))
                                missingMaps.Add(map);
                        }
                    });

                AnsiConsole.MarkupLine("\n[red3]Please ensure the following information is correct:[/]");
                AnsiConsole.MarkupLine($"[yellow]FastDL URL:[/] {jsonModel.FastDL}");
                AnsiConsole.MarkupLine($"[yellow]Output Directory:[/] {(String.IsNullOrWhiteSpace(output) ? jsonModel.OutputDirectory : output)}\n");

                if (missingMaps.Count > 0)
                    WriteLogMessage($"[lime]{missingMaps.Count}[/] map(s) missing from local directory, awaiting confirmation to download");
                else
                {
                    WriteLogMessage("[lime]All maps are up to date with the server. No actions needed[/]");
                    return 0;
                }

                if (!AnsiConsole.Confirm("Proceed to download?", false))
                {
                    WriteLogMessage("Download [red]aborted[/]");
                    return 0;
                }

                DownloadFilesAsync(missingMaps, String.IsNullOrWhiteSpace(output) ? jsonModel.OutputDirectory : output, jsonModel.FastDL, multithread);
            }
            return 0;
        }

        private static void DownloadFilesAsync(List<string> missingMaps, string outputDir, string fastDLUrl, bool multithread)
        {
            if (!Directory.Exists(outputDir))
            {
                AnsiConsole.MarkupLine($"[red]ERROR:[/] [grey]Path \"{outputDir}\" does not exist!");
                Environment.Exit(1);
            }

            if (!multithread)
            {
                AnsiConsole.Progress()
                .AutoClear(false)
                .AutoRefresh(true)
                .Columns(new ProgressColumn[]
                {
                                new TaskDescriptionColumn(),
                                new ProgressBarColumn(),
                                new PercentageColumn(),
                                new RemainingTimeColumn(),
                })
                .Start(ctx =>
                {
                    var task = ctx.AddTask("Downloading and Extracting files...");
                    task.MaxValue(missingMaps.Count);

                    foreach (string map in missingMaps)
                    {
                        using (Stream fileStream = DownloadMap(map, fastDLUrl))
                        {
                            if (fileStream != null)
                                ExtractMap(fileStream, map, outputDir);
                        }

                        // Increment task no matter success or fail:
                        task.Increment(1);
                    }
                });
            }
            else
            {
                AnsiConsole.Progress()
                .AutoClear(false)
                .Columns(new ProgressColumn[]
                {
                    new TaskDescriptionColumn(),
                    new ProgressBarColumn(),
                    new PercentageColumn(),
                    new RemainingTimeColumn()
                })
                .Start(ctx =>
                {
                    var downloadTask = ctx.AddTask("Downloading files...");
                    var extractTask = ctx.AddTask("Extracting files...");

                    downloadTask.MaxValue(missingMaps.Count);
                    extractTask.MaxValue(missingMaps.Count);

                    var downloadBlock = new TransformBlock<string, Tuple<string, HttpResponseMessage>>(async mapName =>
                    {
                        try
                        {
                            WriteLogMessage($"Downloading map: [bold olive]{mapName}[/]");

                            Uri downloadUrl = new Uri($"{fastDLUrl}{mapName}.bsp.bz2");
                            var result = await _httpClient.GetAsync(downloadUrl);

                            WriteLogMessage($"Downloading map: [bold olive]{mapName}[/] [green]success![/]", false);

                            downloadTask.Increment(1);
                            return new Tuple<string, HttpResponseMessage>(mapName, result);
                        }
                        catch (Exception ex)
                        {
                            WriteLogMessage($"Downloading map: [bold olive]{mapName}[/] [red]failed![/]", false);
                            AnsiConsole.WriteException(ex);
                        }

                        // Increment task no matter success or fail:
                        downloadTask.Increment(1);

                        return null;
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = 1,
                    });

                    var extractBlock = new ActionBlock<Tuple<string, HttpResponseMessage>>(async model =>
                    {
                        try
                        {
                            if (model != null)
                            {
                                WriteLogMessage($"Extracting map: [bold olive]{model.Item1}[/]");

                                using (Stream fileStream = await model.Item2.Content.ReadAsStreamAsync())
                                using (Stream stream = File.Create(Path.Combine(new[] { outputDir, $"{model.Item1}.bsp" })))
                                {
                                    await Task.Run(() => 
                                        BZip2.Decompress(fileStream, stream, true)
                                    );
                                    WriteLogMessage($"Extracting map: [bold olive]{model.Item1}[/] [green]success![/]", false);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            WriteLogMessage($"Extracting map: [bold olive]{model.Item1}[/] [red]failed![/]", false);
                            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                        }

                        // Increment task no matter success or fail:
                        extractTask.Increment(1);
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        BoundedCapacity = Environment.ProcessorCount * 3,
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                        //MaxDegreeOfParallelism = DataflowBlockOptions.Unbounded
                    });

                    downloadBlock.LinkTo(
                        extractBlock,
                        new DataflowLinkOptions
                        {
                            PropagateCompletion = true,
                        });

                    foreach (string map in missingMaps)
                    {
                        downloadBlock.Post(map);
                        // Extraction sometimes take awhile on HDD...
                        // Thread.Sleep(2000);
                    }

                    downloadBlock.Complete();
                    extractBlock.Completion.GetAwaiter().GetResult();
                });
            }
        }

        static Stream DownloadMap(string mapName, string fastDLUrl)
        {
            Stream resultStream = null;
            try
            {
                WriteLogMessage($"Downloading map: [bold olive]{mapName}[/]");

                Uri downloadUrl = new Uri($"{fastDLUrl}{mapName}.bsp.bz2");
                var result = _httpClient.GetAsync(downloadUrl).Result;
                result.EnsureSuccessStatusCode();
                resultStream = result.Content.ReadAsStreamAsync().Result;

                WriteLogMessage($"Downloading map: [bold olive]{mapName}[/] [green]success![/]", false);
            }
            catch (Exception ex)
            {
                WriteLogMessage($"Downloading map: [bold olive]{mapName}[/] [red]failed![/]", false);
                AnsiConsole.WriteException(ex);
            }

            return resultStream;
        }

        static void ExtractMap(Stream fileStream, string mapName, string outputDir)
        {
            try
            {
                WriteLogMessage($"Extracting map: [bold olive]{mapName}[/]");

                using (Stream outStream = File.Create(Path.Combine(new[] { outputDir, $"{mapName}.bsp" })))
                {
                    Task.Run(() => BZip2.Decompress(fileStream, outStream, true)).Wait();
                    WriteLogMessage($"Extracting map: [bold olive]{mapName}[/] [green]success![/]", false);
                }
            }
            catch(Exception ex)
            {
                WriteLogMessage($"Extracting map: [bold olive]{mapName}[/] [red]failed![/]", false);
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
            }
        }
    }
}
