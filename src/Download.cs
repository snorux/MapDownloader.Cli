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
                    var task = ctx.AddTask("Downloading and extracting files...");
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

                    double incrementAmount = (double)1 / missingMaps.Count * 100;

                    var downloadBlock = new TransformBlock<DownloadModel, DownloadModel>(async filename =>
                    {
                        AnsiConsole.WriteLine("Download {0}", filename.MapName);
                        string url = $@"https://fastdlv2.gflclan.com/file/gflfastdlv2/csgo/maps/{filename.MapName}.bsp.bz2";
                        var result = await _httpClient.GetAsync(url);

                        DownloadModel dm = new DownloadModel() { MapName = filename.MapName, FileResult = result };
                        downloadTask.Increment(incrementAmount);
                        return dm;
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        MaxDegreeOfParallelism = 2,
                    });

                    var extractBlock = new ActionBlock<DownloadModel>(async model =>
                    {
                        AnsiConsole.WriteLine("Unzip {0}", model.MapName);

                        using (Stream fileStream = model.FileResult.Content.ReadAsStreamAsync().Result)
                        using (Stream stream = File.Create(@$"D:\Projects\C#\TPLTest\TPLTest\TPLTest\bin\Debug\netcoreapp3.1\Downloaded\{model.MapName}.bsp"))
                        {
                            await Task.Run(() => BZip2.Decompress(fileStream, stream, true));
                        }
                        extractTask.Increment(incrementAmount);
                    },
                    new ExecutionDataflowBlockOptions
                    {
                        BoundedCapacity = Environment.ProcessorCount * 3,
                        MaxDegreeOfParallelism = Environment.ProcessorCount,
                    });

                    downloadBlock.LinkTo(
                        extractBlock,
                        new DataflowLinkOptions
                        {
                            PropagateCompletion = true,
                        });

                    foreach (string map in missingMaps)
                    {
                        DownloadModel downloadModel = new DownloadModel() { MapName = map, OutputDir = outputDir };
                        downloadBlock.Post(downloadModel);
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
                using (Stream outStream = File.Create($@"D:\Projects\C#\TPLTest\TPLTest\TPLTest\bin\Debug\netcoreapp3.1\Downloaded\{mapName}.bsp"))
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
