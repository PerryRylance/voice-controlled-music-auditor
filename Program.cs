using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Speech.Recognition;
using NAudio.Wave;
using System.IO;
using System.Threading;
using System.Globalization;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace VoiceControlledMusicAuditor
{
    internal class Program
    {
        const string VOICE_COMMAND_ACCEPT = "accept";
        const string VOICE_COMMAND_DELETE = "delete";
        const string VOICE_COMMAND_SKIP = "skip";

        static WaveOutEvent waveOut;
        static AudioFileReader currentFile;

        static string inputPath;
        static Queue<string> files;
        static string outputPath;

        static SpeechRecognitionEngine recognizer;

        static async Task Main(string[] args)
        {
            var inputOption = new Option<string>("--input", "Input path to scan for MP3 files");
            var outputOption = new Option<string>("--output", "Where to place the accepted MP3 files");

            var rootCommand = new RootCommand("A tool to audit MP3 files and allow you to accept them into a folder of your choice, or delete them, using voice commands")
            {
                inputOption,
                outputOption
            };

            rootCommand.SetHandler((inputOptionValue, outputOptionValue) =>
            {
                AssertPathExists(outputOptionValue);
                AssertPathNotIn(outputOptionValue, inputOptionValue);

                outputPath = outputOptionValue;

                ScanDirectories(inputOptionValue);

                inputPath = inputOptionValue;

                InitRecognizer();
                Loop();

            }, inputOption, outputOption);

            await rootCommand.InvokeAsync(args);

            Console.ReadKey();
        }

        private static void AssertPathExists(string path)
        {
            if (Directory.Exists(path))
                return;
            
            Console.WriteLine($"Path {path} does not exist");
            Environment.Exit(1);
        }

        private static void AssertPathNotIn(string path, string container)
        {
            if (!path.StartsWith(container, StringComparison.OrdinalIgnoreCase))
                return;

            Console.WriteLine($"Path {path} must not be within {container}");
            Environment.Exit(1);
        }

        private static void ScanDirectories(string path)
        {
            AssertPathExists(path);

            var arr = Directory.GetFiles(path, "*.mp3", SearchOption.AllDirectories);

            files = new Queue<string>(arr);
        }

        private static void InitRecognizer()
        {
            recognizer = new SpeechRecognitionEngine(new CultureInfo("en-US"));

            var choices = new Choices(Program.VOICE_COMMAND_ACCEPT, Program.VOICE_COMMAND_DELETE, Program.VOICE_COMMAND_SKIP);
            var builder = new GrammarBuilder(choices);
            var grammer = new Grammar(builder);

            recognizer.LoadGrammar(grammer);
            recognizer.SetInputToDefaultAudioDevice();
            recognizer.SpeechRecognized += onSpeechRecognized;
        }

        private static void Loop()
        {
            while (files.Count > 0)
            {
                var file = files.Dequeue();

                PlayFile(file);
                recognizer.RecognizeAsync(RecognizeMode.Multiple);

                Console.WriteLine(files.Count + " songs remain");
                Console.WriteLine("Playing: " + file);

                while (waveOut.PlaybackState == PlaybackState.Playing)
                {
                    Thread.Sleep(500);
                }

                recognizer.RecognizeAsyncStop();
            }
            
            Console.WriteLine("All files processed");
            Environment.Exit(0);
        }

        private static void PlayFile(string filePath)
        {
            currentFile = new AudioFileReader(filePath);

            waveOut = new WaveOutEvent();
            waveOut.Init(currentFile);
            waveOut.Play();
        }

        private static void StopPlayback()
        {
            waveOut.Stop();

            currentFile.Dispose();
            waveOut.Dispose();
        }

        private static void Delete(string file)
        {
            File.Delete(file);
            Console.WriteLine("Deleted: " + file);
        }

        private static string GetRelativePath(string relativeTo, string path)
        {
            String pathSep = "\\";
            String itemPath = Path.GetFullPath(path);
            String baseDirPath = Path.GetFullPath(relativeTo); // If folder contains upper folder references, they get resolved here. "c:\test\..\test2" => "c:\test2"
            bool isDirectory = path.EndsWith(pathSep);

            String[] p1 = Regex.Split(itemPath, "[\\\\/]").Where(x => x.Length != 0).ToArray();
            String[] p2 = Regex.Split(relativeTo, "[\\\\/]").Where(x => x.Length != 0).ToArray();
            int i = 0;

            for (; i < p1.Length && i < p2.Length; i++)
                if (String.Compare(p1[i], p2[i], true) != 0)    // Case insensitive match
                    break;

            if (i == 0)     // Cannot make relative path, for example if resides on different drive
                return itemPath;

            String r = String.Join(pathSep, Enumerable.Repeat("..", p2.Length - i).Concat(p1.Skip(i).Take(p1.Length - i)));
            if (String.IsNullOrEmpty(r)) return ".";
            else if (isDirectory && p1.Length >= p2.Length) // only append on forward traversal, to match .Net Standard Implementation of System.IO.Path.GetRelativePath
                r += pathSep;

            return r;
        }

        private static void MoveToAccepted(string file)
        {
            var relativeToInput = GetRelativePath(inputPath, file);
            var outputFile = Path.Combine(outputPath, relativeToInput);
            var outputDirectory = Path.GetDirectoryName(outputFile);

            Directory.CreateDirectory(outputDirectory);
            File.Move(file, outputFile);

            Console.WriteLine("Accepted: " + outputFile);
        }

        static void onSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Console.WriteLine("Speech recognized: " + e.Result.Text);

            string file = currentFile.FileName;

            StopPlayback();

            switch (e.Result.Text)
            {
                case Program.VOICE_COMMAND_DELETE:
                    Delete(file);
                    break;

                case Program.VOICE_COMMAND_ACCEPT:
                    MoveToAccepted(file);
                    break;

                case Program.VOICE_COMMAND_SKIP:
                    break;

                default:
                    Console.WriteLine("Don't know how to respond to voice command");
                    break;
            }
        }
    }
}
