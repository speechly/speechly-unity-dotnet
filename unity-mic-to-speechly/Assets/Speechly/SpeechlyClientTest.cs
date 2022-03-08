using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace Speechly.SLUClient {
  public class SpeechlyClientTest {
    public static async Task test() {
      Stopwatch sw = new Stopwatch();

      var client = new SpeechlyClient(
        loginUrl: "https://staging.speechly.com/login",
        apiUrl: "wss://staging.speechly.com/ws/v1?sampleRate=16000",
        appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e" // Chinese
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

      sw.Restart();
      await client.Connect();
      var connectTime = sw.ElapsedMilliseconds;

      sw.Restart();
      await client.StartContext();
      await client.SendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
      await client.StopContext();
      var sluTime = sw.ElapsedMilliseconds;

      Logger.Log($"==== STATS ====");
      Logger.Log($"Connect time: {connectTime} ms");
      Logger.Log($"SLU time: {sluTime} ms");
    }
  }
}