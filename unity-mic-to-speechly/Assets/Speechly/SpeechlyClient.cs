using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Speechly.SLUClient {

  public class SpeechlyClient {
    public static bool DEBUG_LOG = false;
    public static bool DEBUG_SAVE_AUDIO = false;

    public delegate void SegmentChangeDelegate(Segment segment);
    public delegate void StateChangeDelegate(ClientState state);
    public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
    public delegate void TranscriptDelegate(MsgTranscript msg);
    public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
    public delegate void EntityDelegate(MsgEntity msg);
    public delegate void IntentDelegate(MsgIntent msg);
    public SegmentChangeDelegate OnSegmentChange = (Segment segment) => {};
    public TentativeTranscriptDelegate OnTentativeTranscript = (msg) => {};
    public TranscriptDelegate OnTranscript = (msg) => {};
    public TentativeEntityDelegate OnTentativeEntity = (msg) => {};
    public EntityDelegate OnEntity = (msg) => {};
    public IntentDelegate OnTentativeIntent = (msg) => {};
    public IntentDelegate OnIntent = (msg) => {};
    public StateChangeDelegate OnStateChange = (ClientState state) => {};

    public bool IsListening { get; private set; } = false;
    public ClientState State { get; private set; } = ClientState.Disconnected;
    // Optional message queue should messages be run in the main thread
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
    private string deviceId;
    private string token;
    private Dictionary<string, Dictionary<int, Segment>> activeContexts = new Dictionary<string, Dictionary<int, Segment>>();
    private WsClient wsClient;
    private TaskCompletionSource<MsgCommon> startContextTCS;
    private TaskCompletionSource<MsgCommon> stopContextTCS;
    private string loginUrl = "https://api.speechly.com/login";
    private string apiUrl = "wss://api.speechly.com/ws/v1?sampleRate=16000";
    private string projectId = null;
    private string appId = null;
    private FileStream debugAudioStream;

    public SpeechlyClient(
      string loginUrl = null,
      string apiUrl = null,
      string projectId = null,
      string appId = null,
      bool manualUpdate = false
    ) {
      if (loginUrl != null) this.loginUrl = loginUrl;
      if (apiUrl != null) this.apiUrl = apiUrl;
      if (projectId != null) this.projectId = projectId;
      if (appId != null) this.appId = appId;

      wsClient = new WsClient();
      if (manualUpdate) {
        wsClient.OnResponseReceived = QueueResponse;
      } else {
        wsClient.OnResponseReceived = ProcessResponse;
      }
    }

    public async Task Connect() {
      SetState(ClientState.Connecting);

      var c = SpeechlyConfig.RestoreOrCreate();
      if (c.deviceId == null) {
        deviceId = System.Guid.NewGuid().ToString();
        c.deviceId = deviceId;
        c.Save();
        if (DEBUG_LOG) Logger.Log($"New deviceId: {deviceId}");
      } else {
        deviceId = c.deviceId;
        if (DEBUG_LOG) Logger.Log($"Restored deviceId: {deviceId}");
      }

      var tokenFetcher = new LoginToken();
      token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

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
      if (DEBUG_SAVE_AUDIO) {
        debugAudioStream = new FileStream($"utterance_{contextId}.raw", FileMode.CreateNew, FileAccess.Write, FileShare.None);
      }
      SetState(ClientState.Recording);
      return contextId;
    }

    public async Task SendAudio(Stream fileStream) {
      if (State != ClientState.Recording) return;

      // @TODO Use a pre-allocated buf
      var b = new byte[8192];

      while (true) {
        int bytesRead = fileStream.Read(b, 0, b.Length);
        if (bytesRead == 0) break;
        if (DEBUG_SAVE_AUDIO) {
          debugAudioStream.Write(b, 0, bytesRead);
        }
        await wsClient.SendBytes(new ArraySegment<byte>(b, 0, bytesRead));
      }
    }

    public async Task SendAudio(float[] floats, int start = 0, int end = -1) {
      if (State != ClientState.Recording) return;

      if (end < 0) end = floats.Length;
      int bufSize = end - start;
      // @TODO Use a pre-allocated buf
      var buf = new byte[bufSize * 2];
      int i = 0;

      for (var l = start; l < end; l++) {
        short v = (short)(floats[l] * 0x7fff);
        buf[i++] = (byte)(v);
        buf[i++] = (byte)(v >> 8);
      }

      if (DEBUG_SAVE_AUDIO) {
        debugAudioStream.Write(buf, 0, buf.Length);
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
      if (DEBUG_SAVE_AUDIO) {
        debugAudioStream.Close();
      }

      SetState(ClientState.Connected);
    }

    /**
     * Fire Speechly callbacks manually if you want them to run in main UI/Unity thread
     */    
    public void Update() {
      string msgString;
      while (messageQueue.TryDequeue(out msgString)) {
        OnResponse(msgString);
      }
    }

    private void SetState(ClientState state) {
      this.State = state;
      OnStateChange(state);
    }

    private void QueueResponse(MemoryStream inputStream) {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      messageQueue.Enqueue(msgString);
    }

    private void ProcessResponse(MemoryStream inputStream) {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      OnResponse(msgString);
    }

    private void OnResponse(string msgString)
    {
      try {
        // @TODO Find a way to deserialize only once
        var msgCommon = JSON.Parse(msgString, new MsgCommon());
        switch (msgCommon.type) {
          case "started": {
            if (DEBUG_LOG) Logger.Log($"Started context '{msgCommon.audio_context}'");
            activeContexts.Add(msgCommon.audio_context, new Dictionary<int, Segment>());
            startContextTCS.SetResult(msgCommon);
            break;
          }
          case "stopped": {
            if (DEBUG_LOG) Logger.Log($"Stopped context '{msgCommon.audio_context}'");
            activeContexts.Remove(msgCommon.audio_context);
            stopContextTCS.SetResult(msgCommon);
            break;
          }
          default: {
            OnSegmentMessage(msgCommon, msgString);
            break;
          }
        }
      } catch (Exception e) {
        throw new Exception($"Ouch. {e.GetType()} while handling message with content  with content: {msgString}");
      }
    }

    private void OnSegmentMessage(MsgCommon msgCommon, string msgString)
    {
      Segment segmentState;
      if (!activeContexts[msgCommon.audio_context].TryGetValue(msgCommon.segment_id, out segmentState)) {
        segmentState = new Segment(msgCommon.audio_context, msgCommon.segment_id);
        activeContexts[msgCommon.audio_context].Add(msgCommon.segment_id, segmentState);
      }

      switch (msgCommon.type) {
        case "tentative_transcript": {
          var msg = JSON.Parse(msgString, new MsgTentativeTranscript());
          segmentState.UpdateTranscript(msg.data.words);
          OnTentativeTranscript(msg);
          break;
        }
        case "transcript": {
          var msg = JSON.Parse(msgString, new MsgTranscript());
          msg.data.isFinal = true;
          segmentState.UpdateTranscript(msg.data);
          OnTranscript(msg);
          break;
        }
        case "tentative_entities": {
          var msg = JSON.Parse(msgString, new MsgTentativeEntity());
          segmentState.UpdateEntity(msg.data.entities);
          OnTentativeEntity(msg);
          break;
        }
        case "entity": {
          var msg = JSON.Parse(msgString, new MsgEntity());
          msg.data.isFinal = true;
          segmentState.UpdateEntity(msg.data);
          OnEntity(msg);
          break;
        }
        case "tentative_intent": {
          var msg = JSON.Parse(msgString, new MsgIntent());
          segmentState.UpdateIntent(msg.data.intent, false);
          OnTentativeIntent(msg);
          break;
        }
        case "intent": {
          var msg = JSON.Parse(msgString, new MsgIntent());
          segmentState.UpdateIntent(msg.data.intent, true);
          OnIntent(msg);
          break;
        }
        case "segment_end": {
          segmentState.EndSegment();
          break;
        }
        default: {
          throw new Exception($"Unhandled message type '{msgCommon.type}' with content: {msgString}");
        }
      }

      OnSegmentChange(segmentState);
    }

  }
}