using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

public class SpeechlyClientTest {
  public static async Task test() {
    var client = new SpeechlyClient(
      loginUrl: "https://staging.speechly.com/login",
      apiUrl: "wss://staging.speechly.com/ws/v1?sampleRate=16000",
      appId: "76e901c8-7795-43d5-9c5c-4a25d5edf80e" // Chinese
    );

    client.onTentativeTranscript = (msg) => {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative transcript ({msg.data.words.Length} words): ");
      msg.data.words.ToList().ForEach(w => sb.Append($"'{w.word}'@{w.index} {w.startTimestamp}..{w.endTimestamp}ms " ));
      Logger.Log(sb.ToString());
    };
    client.onTranscript = (msg) => Logger.Log($"Final transcript: '{msg.data.word}'@{msg.data.index} {msg.data.startTimestamp}..{msg.data.endTimestamp}ms");
    client.onTentativeEntity = (msg) => {
      StringBuilder sb = new StringBuilder();
      sb.Append($"Tentative entities ({msg.data.entities.Length}): ");
      msg.data.entities.ToList().ForEach(w => sb.Append($"'{w.entity}': '{w.value}' @ {w.startPosition}..{w.endPosition} " ));
      Logger.Log(sb.ToString());
    };
    client.onEntity = (msg) => Logger.Log($"Final entity '{msg.data.entity}' with value '{msg.data.value}' @ {msg.data.startPosition}..{msg.data.endPosition}");
    client.onTentativeIntent = (msg) => Logger.Log($"Tentative intent: '{msg.data.intent}'");
    client.onIntent = (msg) => Logger.Log($"Intent: '{msg.data.intent}'");

    await client.connect();
    await client.startContext();
    await client.sendAudioFile("Assets/Speechly/00_chinese_restaurant.raw");
    await client.stopContext();
  }
}