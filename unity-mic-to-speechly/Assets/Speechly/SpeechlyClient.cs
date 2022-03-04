using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor.
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.

public class SpeechlyClient {
  public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
  public delegate void TranscriptDelegate(MsgTranscript msg);
  public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
  public delegate void EntityDelegate(MsgEntity msg);
  public delegate void IntentDelegate(MsgIntent msg);
  public TentativeTranscriptDelegate onTentativeTranscript = (msg) => {};
  public TranscriptDelegate onTranscript = (msg) => {};
  public TentativeEntityDelegate onTentativeEntity = (msg) => {};
  public EntityDelegate onEntity = (msg) => {};
  public IntentDelegate onTentativeIntent = (msg) => {};
  public IntentDelegate onIntent = (msg) => {};

  public bool isListening { get; private set; } = false;
  private string deviceId;
  private string token;
  private Dictionary<string, Dictionary<int, SegmentState>> activeContexts = new Dictionary<string, Dictionary<int, SegmentState>>();
  private WsClient wsClient;
  private TaskCompletionSource<MsgCommon> startContextTCS;
  private TaskCompletionSource<MsgCommon> stopContextTCS;
  string loginUrl = "https://api.speechly.com/login";
  string apiUrl = "wss://api.speechly.com/ws/v1?sampleRate=16000";
  string projectId = null;
  string appId = null;

  public SpeechlyClient(
    string loginUrl = null,
    string apiUrl = null,
    string projectId = null,
    string appId = null
  ) {
    if (loginUrl != null) this.loginUrl = loginUrl;
    if (apiUrl != null) this.apiUrl = apiUrl;
    if (projectId != null) this.projectId = projectId;
    if (appId != null) this.appId = appId;

    wsClient = new WsClient();
    wsClient.onResponseReceived = ResponseReceived;
  }

  public async Task connect() {
    deviceId = System.Guid.NewGuid().ToString();

    var tokenFetcher = new LoginToken();
    token = await tokenFetcher.fetchToken(loginUrl, projectId, appId, deviceId);

    Logger.Log($"deviceId: {deviceId}");
    Logger.Log($"token: {token}");

    await wsClient.ConnectAsync(apiUrl, token);
  }

  public async Task<string> startContext(string appId = null) {
    if (isListening) {
      throw new Exception("Already listening.");
    }
    isListening = true;
    startContextTCS = new TaskCompletionSource<MsgCommon>();
    wsClient.startContext(appId);
    var contextId = (await startContextTCS.Task).audio_context;
    return contextId;
  }

  public async Task sendAudio(Stream fileStream) {
    // @TODO Use a pre-allocated buf
    var b = new byte[8192];

    while (true) {
      int bytesRead = fileStream.Read(b, 0, b.Length);
      if (bytesRead == 0) break;
      await wsClient.sendAudio(new ArraySegment<byte>(b, 0, bytesRead));
    }
  }

  public async Task sendAudio(float[] floats, int start = 0, int end = -1) {
    if (end < 0) end = floats.Length;
    var bufSize = end - start;
    // @TODO Use a pre-allocated buf
    var buf = new byte[bufSize * 2];
    int i = 0;

    for (var l = start; l < end; l++) {
      short v = (short)(floats[l] * 0x7fff);
      buf[i++] = (byte)(v);
      buf[i++] = (byte)(v >> 8);
    }

    await wsClient.sendAudio(new ArraySegment<byte>(buf));
  }

  public async Task sendAudioFile(string fileName) {
    var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
    await sendAudio(fileStream);
    fileStream.Close();
  }

  public async Task stopContext() {
    if (!isListening) {
      throw new Exception("Already stopped listening.");
    }
    isListening = false;
    stopContextTCS = new TaskCompletionSource<MsgCommon>();
    wsClient.stopContext();
    var contextId = (await stopContextTCS.Task).audio_context;
  }

  private void ResponseReceived(MemoryStream inputStream)
  {
    var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
    // Logger.Log(msgString);
    try {
      var msgCommon = JSON.JSONDeserialize(msgString, new MsgCommon());
      // Logger.Log($"message type {msgCommon.type}");
      switch (msgCommon.type) {
        case "started": {
          Logger.Log($"Started context '{msgCommon.audio_context}'");
          startContextTCS.SetResult(msgCommon);
          break;
        }
        case "tentative_transcript": {
          var msg = JSON.JSONDeserialize(msgString, new MsgTentativeTranscript());
          onTentativeTranscript(msg);
          break;
        }
        case "transcript": {
          var msg = JSON.JSONDeserialize(msgString, new MsgTranscript());
          msg.data.isFinal = true;
          onTranscript(msg);
          break;
        }
        case "tentative_entities": {
          var msg = JSON.JSONDeserialize(msgString, new MsgTentativeEntity());
          onTentativeEntity(msg);
          break;
        }
        case "entity": {
          var msg = JSON.JSONDeserialize(msgString, new MsgEntity());
          msg.data.isFinal = true;
          onEntity(msg);
          break;
        }
        case "tentative_intent": {
          var msg = JSON.JSONDeserialize(msgString, new MsgIntent());
          onTentativeIntent(msg);
          break;
        }
        case "intent": {
          var msg = JSON.JSONDeserialize(msgString, new MsgIntent());
          onIntent(msg);
          break;
        }
        case "segment_end": {
          break;
        }
        case "stopped": {
          Logger.Log($"Stopped context '{msgCommon.audio_context}'");
          stopContextTCS.SetResult(msgCommon);
          break;
        }
        default: {
          throw new Exception($"Unhandled message type '{msgCommon.type}' with content: {msgString}");
        }
      }
    } catch (Exception e) {
      throw new Exception($"Ouch. {e.GetType()} while handling message with content  with content: {msgString}");
    }
  }

}