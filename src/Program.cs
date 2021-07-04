using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using Spectre.Console;
using System.Threading.Tasks;
using System.Text;

namespace MapDownloader
{
    partial class Program
    {
        static async Task<int> Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            var rootCommand = new RootCommand
            {
                new Option<string>(
                    new[] {"--input", "-i" },
                    description: "An option to specify the required json file.") { IsRequired = true },
                new Option<string?>(
                    new[] {"--output", "-o" },
                    description: "An optional argument to override the output folder of the downloaded files.") { ArgumentHelpName = "Path to output folder" },
                new Option<string?>(
                    "--csv",
                    description: "An optional argument to override the csv located in the json file. Must be locally hosted.") { ArgumentHelpName = "Path to .csv file"},
                new Option<string?>(
                    "--fastdl",
                    description: "An optional argument to override the fastdl link located in the json file.") { ArgumentHelpName = "Link to fastDL" },
                new Option(new[] { "--async", "-a", "--multi-thread" }, "Downloads and extract the file concurrently.")
            };

            rootCommand.Description = "Map Downloader CLI - Tool made by Snowy";
            rootCommand.Handler = CommandHandler.Create<string, string?, string?, string?, bool>(SetupDownload);

            AnsiConsole.Render(new FigletText("MapDownloader")
                .Color(Color.Green)
                .LeftAligned());

            return await rootCommand.InvokeAsync(args);
        }
    }
}
