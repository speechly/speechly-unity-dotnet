using System;
using System.Threading.Tasks;

using Speechly.SLUClient;

class Program
{
  static async Task Main(string[] args)
  {
    EnergyTresholdVAD vad = new EnergyTresholdVAD();
    string outFolder = "./temp";
    string logPath = "./temp";

    string[] files = args;
    if (files.Length == 0) {
      files = new string[] {"Speechly/SLUClientTest/00_chinese_restaurant.raw"};
    }

    foreach( string fileName in files) {
      await SpeechlyClientTest.test(fileName, outFolder, logPath, vad, useCloudSpeechProcessing: false);
    }
  }
}
