using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace Speechly.SLUClient {

  public class SpeechlyClient {
    public static bool DEBUG_LOG = false;
    
    public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
    public delegate void TranscriptDelegate(MsgTranscript msg);
    public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
    public delegate void EntityDelegate(MsgEntity msg);
    public delegate void IntentDelegate(MsgIntent msg);
    public TentativeTranscriptDelegate OnTentativeTranscript = (msg) => {};
    public TranscriptDelegate OnTranscript = (msg) => {};
    public TentativeEntityDelegate OnTentativeEntity = (msg) => {};
    public EntityDelegate OnEntity = (msg) => {};
    public IntentDelegate OnTentativeIntent = (msg) => {};
    public IntentDelegate OnIntent = (msg) => {};

    public bool IsListening { get; private set; } = false;
    private string deviceId;
    private string token;
    private Dictionary<string, Dictionary<int, SegmentState>> activeContexts = new Dictionary<string, Dictionary<int, SegmentState>>();
    private WsClient wsClient;
    private TaskCompletionSource<MsgCommon> startContextTCS;
    private TaskCompletionSource<MsgCommon> stopContextTCS;
    private string loginUrl = "https://api.speechly.com/login";
    private string apiUrl = "wss://api.speechly.com/ws/v1?sampleRate=16000";
    private string projectId = null;
    private string appId = null;
    private ClientState state = ClientState.Disconnected;

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
      wsClient.OnResponseReceived = ResponseReceived;
    }

    public async Task Connect() {
      SetState(ClientState.Connecting);
      // @TODO Retain device ID
      deviceId = System.Guid.NewGuid().ToString();

      var tokenFetcher = new LoginToken();
      token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

      if (DEBUG_LOG) Logger.Log($"deviceId: {deviceId}");
      if (DEBUG_LOG) Logger.Log($"token: {token}");

      await wsClient.ConnectAsync(apiUrl, token);
      SetState(ClientState.Preinitialized);
      SetState(ClientState.Initializing);
      SetState(ClientState.Connected);
    }

    public async Task<string> StartContext(string appId = null) {
      if (IsListening) {
        throw new Exception("Already listening.");
      }
      IsListening = true;
      SetState(ClientState.Starting);
      startContextTCS = new TaskCompletionSource<MsgCommon>();
      if (appId != null) {
        await wsClient.SendText($"{{\"event\": \"start\", \"appId\": \"{appId}\"}}");
      } else {
        await wsClient.SendText($"{{\"event\": \"start\"}}");
      }
      var contextId = (await startContextTCS.Task).audio_context;
      SetState(ClientState.Recording);
      return contextId;
    }

    public async Task SendAudio(Stream fileStream) {
      if (state != ClientState.Recording) return;

      // @TODO Use a pre-allocated buf
      var b = new byte[8192];

      while (true) {
        int bytesRead = fileStream.Read(b, 0, b.Length);
        if (bytesRead == 0) break;
        await wsClient.SendBytes(new ArraySegment<byte>(b, 0, bytesRead));
      }
    }

    public async Task SendAudio(float[] floats, int start = 0, int end = -1) {
      if (state != ClientState.Recording) return;

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

      await wsClient.SendBytes(new ArraySegment<byte>(buf));
    }

    public async Task SendAudioFile(string fileName) {
      var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      await SendAudio(fileStream);
      fileStream.Close();
    }

    public async Task StopContext() {
      if (!IsListening) {
        throw new Exception("Already stopped listening.");
      }
      SetState(ClientState.Stopping);
      IsListening = false;
      stopContextTCS = new TaskCompletionSource<MsgCommon>();
      await wsClient.SendText($"{{\"event\": \"stop\"}}");
      var contextId = (await stopContextTCS.Task).audio_context;
      SetState(ClientState.Connected);
    }

    private void SetState(ClientState state) {
      this.state = state;
    }

    private void ResponseReceived(MemoryStream inputStream)
    {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      try {
        // @TODO Find a way to deserialize only once
        var msgCommon = JSON.Parse(msgString, new MsgCommon());
        switch (msgCommon.type) {
          case "started": {
            if (DEBUG_LOG) Logger.Log($"Started context '{msgCommon.audio_context}'");
            startContextTCS.SetResult(msgCommon);
            break;
          }
          case "tentative_transcript": {
            var msg = JSON.Parse(msgString, new MsgTentativeTranscript());
            OnTentativeTranscript(msg);
            break;
          }
          case "transcript": {
            var msg = JSON.Parse(msgString, new MsgTranscript());
            msg.data.isFinal = true;
            OnTranscript(msg);
            break;
          }
          case "tentative_entities": {
            var msg = JSON.Parse(msgString, new MsgTentativeEntity());
            OnTentativeEntity(msg);
            break;
          }
          case "entity": {
            var msg = JSON.Parse(msgString, new MsgEntity());
            msg.data.isFinal = true;
            OnEntity(msg);
            break;
          }
          case "tentative_intent": {
            var msg = JSON.Parse(msgString, new MsgIntent());
            OnTentativeIntent(msg);
            break;
          }
          case "intent": {
            var msg = JSON.Parse(msgString, new MsgIntent());
            OnIntent(msg);
            break;
          }
          case "segment_end": {
            break;
          }
          case "stopped": {
            if (DEBUG_LOG) Logger.Log($"Stopped context '{msgCommon.audio_context}'");
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
}