using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Linq;
using System.Diagnostics;
using Speechly.Tools;
using Speechly.Types;

namespace Speechly.SLUClient {
  internal class SpeechlyClientTest {
    public static async Task TestCloudProcessing(string fileName) {
      Stopwatch stopWatch = new Stopwatch();

      var client = new SpeechlyClient(
        debug: true
      );

      client.OnSegmentChange = (segment) => {
        Logger.Log(segment.ToString());
      };
      
      client.OnTentativeTranscript = (msg) => {
        StringBuilder sb = new StringBuilder();
        sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
        msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms " ));
        Logger.Log(sb.ToString());
      };
      client.OnTentativeEntity = (msg) => {
        StringBuilder sb = new StringBuilder();
        sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
        msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.type}': '{w.value}' @ {w.startPosition}..{w.endPosition} " ));
        Logger.Log(sb.ToString());
      };
      client.OnTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");
      
      client.OnTranscript = (msg) => Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
      client.OnEntity = (msg) => Logger.Log($"Final entity '{msg.data.type}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
      client.OnIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

      stopWatch.Restart();
      
      var decoder = new CloudDecoder(
        apiUrl: "https://staging.speechly.com",
        appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e", // Restaurant booking configuration
        deviceId: Platform.GetDeviceId()
      );

      await client.Initialize(decoder);
      var connectTime = stopWatch.ElapsedMilliseconds;

      stopWatch.Restart();
      _ = client.Start();

      client.ProcessAudioFile(fileName);
      await client.Stop();
      var sluTime = stopWatch.ElapsedMilliseconds;

      Logger.Log($"==== STATS ====");
      Logger.Log($"Connect time: {connectTime} ms");
      Logger.Log($"SLU time: {sluTime} ms");
    }

    public static async Task SplitWithVAD(string fileName, string saveToFolder = null, string logUtteranceFolder = null) {
      Stopwatch stopWatch = new Stopwatch();

      EnergyThresholdVAD vad = new EnergyThresholdVAD(
        new VADOptions(),
        new AudioInfo()
      );

      StreamWriter logUtteranceStream = null;
      int utteranceStartSamplePos = 0;

      if (logUtteranceFolder != null) {
        Directory.CreateDirectory(logUtteranceFolder);
      }

      var client = new SpeechlyClient(
        saveToFolder: saveToFolder,
        debug: true
      );

      await client.Initialize(decoder: null, audioProcessorOptions: new AudioProcessorOptions() { VADControlsListening = true } );

      client.OnStartStream = () => {
        Logger.Log("client.OnStartStream");
        if (logUtteranceFolder != null) {
          logUtteranceStream = new StreamWriter(Path.Combine(logUtteranceFolder, $"{client.AudioInputStreamIdentifier}.tsv"), false);
        }
      };

      client.OnStopStream = () => {
        Logger.Log("client.OnStopStream");
        logUtteranceStream.Close();
        logUtteranceStream = null;
      };

      client.OnStart = () => {
        Logger.Log($"client.OnStart {client.Output.StreamSamplePos}");
        utteranceStartSamplePos = client.Output.StreamSamplePos;
      };

      client.OnStop = () => {
        Logger.Log("client.OnStop");
        string serialString = client.Output.UtteranceSerial.ToString().PadLeft(4, '0');
        logUtteranceStream.WriteLine($"{client.AudioInputStreamIdentifier}\t{serialString}\t{utteranceStartSamplePos}\t{client.Output.SamplesSent}");
      };

      stopWatch.Restart();
      // _ = client.Start();
      client.ProcessAudioFile(fileName);
      // await client.Stop();
      var processTime = stopWatch.ElapsedMilliseconds;

      Logger.Log($"==== STATS ====");
      Logger.Log($"Process time: {processTime} ms");
    }

  }
}
