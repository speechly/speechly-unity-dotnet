using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using Speechly.SLUClient;

class Program
{
  static async Task Main(string[] args)
  {
    string outFolder = "./temp";
    string logPath = "./temp";

    var argList = new Queue<string>(args);

    if (argList.Count == 0) {
      PrintHelp();
      return;
    }

    string cmd = null;
    if (argList.TryDequeue(out cmd)) {
      cmd = cmd.ToLower();
    }

    switch (cmd) {
      case "vad": {
        string[] files = argList.ToArray();

        if (files.Length == 0) {
          files = new string[] {"Speechly/SLUClientTest/00_chinese_restaurant.raw"};
        }

        foreach( string fileName in files) {
          await SpeechlyClientTest.SplitWithVAD(fileName, outFolder, logPath);
        }

        break;
      }
      case "test": {
        string fileName = null;
        argList.TryDequeue(out fileName);
        if (String.IsNullOrWhiteSpace(fileName)) {
          fileName = "Speechly/SLUClientTest/00_chinese_restaurant.raw";
        }
        await SpeechlyClientTest.TestCloudProcessing(fileName);
        break;
      }

      default:
        PrintHelp();
        return;
    }

    void PrintHelp() {
      Console.WriteLine("Issue a command like 'test' or 'vad'");
    }

  }
}
