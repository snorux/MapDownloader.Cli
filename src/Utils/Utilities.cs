using MapDownloader.Model;
using Spectre.Console;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Runtime.InteropServices.WindowsRuntime;

namespace MapDownloader
{
    partial class Program
    {
        static void WriteLogMessage(string message, bool trailingDots = true)
        {
            AnsiConsole.MarkupLine($"[grey]LOG:[/] {message}{(trailingDots ? "[grey]...[/]" : String.Empty)}");
        }

        static bool IsValidFile(string input, string extension)
        {
            string fileExtension = Path.GetExtension(input);
            if (fileExtension != extension)
            {
                AnsiConsole.MarkupLine($"[bold red]The input file is not a \"{extension}\" file.[/]");
                return false;
            }

            bool fileExist = File.Exists(input);
            if (!fileExist)
            {
                AnsiConsole.MarkupLine("[bold red]File not found.[/]");
                return false;
            }

            return true;
        }

        static string[] ReadFromCSV(string mapList, string optionalCSV)
        {
            string[] returnValue = null;
            if (!String.IsNullOrWhiteSpace(optionalCSV))
            {
                try
                {
                    returnValue = File.ReadAllLines(optionalCSV).Select(line => line.TrimEnd(',')).ToArray();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[bold red]Unable to read .csv file, make sure it's the right format:[/]");
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                    Environment.Exit(1);
                }
            }
            else if (String.IsNullOrWhiteSpace(optionalCSV) && !String.IsNullOrWhiteSpace(mapList))
            {
                try
                {
                    returnValue = _httpClient.GetStringAsync(mapList).Result.Replace("\n", String.Empty).Split(',').Where(line => !String.IsNullOrWhiteSpace(line)).ToArray();
                }
                catch (Exception ex)
                {
                    AnsiConsole.MarkupLine("[bold red]Unable to get the .csv file from the URL, make sure it's the right link and format:[/]");
                    AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                    Environment.Exit(1);
                }
            }

            return returnValue;
        }

        static string[] ReadFromDirectory(string outputDir, string optionalOutputDir)
        {
            string[] returnValue = null;
            string actualOutputDir = String.IsNullOrWhiteSpace(optionalOutputDir) ? outputDir : optionalOutputDir;

            try
            {
                if (Directory.Exists(actualOutputDir))
                    returnValue = Directory.GetFiles($@"{actualOutputDir}", "*bsp").Select(file => Path.GetFileNameWithoutExtension(file).ToLower()).ToArray();
                else
                    throw new Exception("Directory does not exist.");
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine("[bold red]Unable to get the get the files in the directory, make sure it's pointed to the right path:[/]");
                AnsiConsole.WriteException(ex, ExceptionFormats.ShortenEverything);
                Environment.Exit(1);
            }

            return returnValue;
        }
    }
}
