using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Speechly.SLUClient {

  public class SpeechlyClient {
    public static bool SEND_AUDIO = false;

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
    public int SamplesSent { get; private set; } = 0;
    public ClientState State { get; private set; } = ClientState.Disconnected;
    // Optional message queue should messages be run in the main thread
    private ConcurrentQueue<string> messageQueue = new ConcurrentQueue<string>();
    private string deviceId;
    private string token;
    private Dictionary<string, Dictionary<int, Segment>> activeContexts = new Dictionary<string, Dictionary<int, Segment>>();
    private WsClient wsClient = null;
    private TaskCompletionSource<MsgCommon> startContextTCS;
    private TaskCompletionSource<MsgCommon> stopContextTCS;
    private string loginUrl = "https://api.speechly.com/login";
    private string apiUrl = "wss://api.speechly.com/ws/v1?sampleRate=16000";
    private string projectId = null;
    private string appId = null;
    private bool manualUpdate;
    private bool debug = false;
    private string saveToFolder = null;
    private FileStream outAudioStream;
    private EnergyTresholdVAD vad = null;

    private int sampleRate = 16000;
    private int frameMillis = 30;
    private int frameSamples;
    private int utteranceSerial;

    public SpeechlyClient(
      string loginUrl = null,
      string apiUrl = null,
      string projectId = null,
      string appId = null,
      string deviceId = null,
      EnergyTresholdVAD vad = null,
      bool manualUpdate = false,
      string saveToFolder = null,
      bool debug = false
    ) {
      if (loginUrl != null) this.loginUrl = loginUrl;
      if (apiUrl != null) this.apiUrl = apiUrl;
      if (projectId != null) this.projectId = projectId;
      if (appId != null) this.appId = appId;
      this.manualUpdate = manualUpdate;
      this.saveToFolder = saveToFolder;
      this.vad = vad;
      this.debug = debug;

      if (!String.IsNullOrEmpty(deviceId)) {
        this.deviceId = Platform.GuidFromString(deviceId);
        if (this.debug) Logger.Log($"Using manual deviceId: {deviceId}");
      } else {
        // Load settings
        Preferences config = ConfigTool.RestoreOrCreate<Preferences>(Preferences.FileName);
        // Restore or generate device id
        if (!String.IsNullOrEmpty(config.deviceId)) {
          this.deviceId = config.deviceId;
          if (this.debug) Logger.Log($"Restored deviceId: {this.deviceId}");
        } else {
          this.deviceId = System.Guid.NewGuid().ToString();
          config.deviceId = this.deviceId;
          ConfigTool.Save<Preferences>(config, Preferences.FileName);
          if (this.debug) Logger.Log($"New deviceId: {this.deviceId}");
        }
      }

      frameSamples = sampleRate * frameMillis / 1000;
    }

    public async Task Connect() {
      if (State < ClientState.Connecting) {
        SetState(ClientState.Connecting);
        try {
          var tokenFetcher = new LoginToken();
          token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

          if (debug) Logger.Log($"token: {token}");

          wsClient = new WsClient();
          if (manualUpdate) {
            wsClient.OnResponseReceived = QueueResponse;
          } else {
            wsClient.OnResponseReceived = ProcessResponse;
          }
          await wsClient.ConnectAsync(apiUrl, token);
          SetState(ClientState.Preinitialized);
          SetState(ClientState.Initializing);
          SetState(ClientState.Connected);
        } catch (Exception e) {
          SetState(ClientState.Failed);
          Logger.LogError(e.ToString());
          throw;
        }
      }
    }

    public async Task<string> StartContext(string appId = null) {
      if (IsListening) {
        throw new Exception("Already listening.");
      }
      IsListening = true;
      SamplesSent = 0;
      if (saveToFolder != null) {
        string fileIdentifier = (utteranceSerial++).ToString().PadLeft(4, '0');;
        outAudioStream = new FileStream(Path.Combine(saveToFolder, $"utterance_{fileIdentifier}.raw"), FileMode.CreateNew, FileAccess.Write, FileShare.None);
      }
      SetState(ClientState.Starting);
      try {
        if (SEND_AUDIO) {
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
        SetState(ClientState.Recording);
        return "localContext";
      } catch (Exception e) {
        SetState(ClientState.Connected);
        Logger.LogError(e.ToString());
        throw;
      }
    }

    public async Task ProcessAudioFile(string fileName) {
      var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      await ProcessAudio(fileStream);
      fileStream.Close();
    }

    public async Task ProcessAudio(Stream fileStream) {
      // if (State != ClientState.Starting && State != ClientState.Recording) return;

      // @TODO Use a pre-allocated buf
      var bytes = new byte[frameSamples * 2];
      var floats = new float[frameSamples];

      while (true) {
        int bytesRead = fileStream.Read(bytes, 0, bytes.Length);
        if (bytesRead == 0) break;
        int samples = bytesRead / 2;
        Logger.Log($"Read {samples}, first value {bytes[0]}");
        int processed = AudioTools.ConvertInt16ToFloat(in bytes, ref floats, 0, samples);
        Logger.Log($"Converted {processed}, first value {floats[0]}");
        // Pad with zeroes
        for (int i = floats.Length-1 ; i >= samples; i--) {
          floats[i] = 0;
        }
        await ProcessFrame(floats, 0, samples);
      }
    }

    public async Task ProcessFrame(float[] floats, int start = 0, int length = -1) {
      if (length < 0) length = floats.Length;
      if (length == 0) return;
      int end = start + length;

      AnalyzeAudioFrame(in floats, start, length);

      AutoControlListening();

      await SendAudio(floats, start, length);
    }

    private void AnalyzeAudioFrame(in float[] waveData, int s, int frameSamples) {
      if (this.vad != null) {
        vad.ProcessFrame(waveData, s, frameSamples);
      }
    }

    private void AutoControlListening() {
      if (this.vad != null) {
        if (!IsListening && vad.IsSignalDetected) {
          _ = StartContext();
        }

        if (IsListening && !vad.IsSignalDetected) {
          _ = StopContext();
        }
        //} else {
        //  EnsureStopContext();
        //}

      }
    }


    private async Task SendAudio(float[] floats, int start = 0, int length = -1) {
      if (State != ClientState.Starting && State != ClientState.Recording) return;

      if (length < 0) length = floats.Length;
      int end = start + length;
      // @TODO Use a pre-allocated buf
      var buf = new byte[length * 2];
      int i = 0;

      for (var l = start; l < end; l++) {
        short v = (short)(floats[l] * 0x7fff);
        buf[i++] = (byte)(v);
        buf[i++] = (byte)(v >> 8);
      }

      SamplesSent += length;

      // Stream to file
      if (saveToFolder != null) {
        outAudioStream.Write(buf, 0, length * 2);
      }

      // Stream via websocket
      if (SEND_AUDIO) {
        await wsClient.SendBytes(new ArraySegment<byte>(buf));
      }
    }

    public async Task<string> StopContext() {
      if (!IsListening) {
        throw new Exception("Already stopped listening.");
      }
      SetState(ClientState.Stopping);
      IsListening = false;
      try {
        if (saveToFolder != null) {
          outAudioStream.Close();
        }
        if (SEND_AUDIO) {
          stopContextTCS = new TaskCompletionSource<MsgCommon>();
          await wsClient.SendText($"{{\"event\": \"stop\"}}");
          var contextId = (await stopContextTCS.Task).audio_context;
          SetState(ClientState.Connected);
          return contextId;
        }
        SetState(ClientState.Connected);
        return "localContext";
      } catch (Exception e) {
        SetState(ClientState.Connected);
        Logger.LogError(e.ToString());
        throw;
      }
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
            if (debug) Logger.Log($"Started context '{msgCommon.audio_context}'");
            activeContexts.Add(msgCommon.audio_context, new Dictionary<int, Segment>());
            startContextTCS.SetResult(msgCommon);
            break;
          }
          case "stopped": {
            if (debug) Logger.Log($"Stopped context '{msgCommon.audio_context}'");
            activeContexts.Remove(msgCommon.audio_context);
            stopContextTCS.SetResult(msgCommon);
            break;
          }
          default: {
            OnSegmentMessage(msgCommon, msgString);
            break;
          }
        }
      } catch {
        Logger.LogError($"Error while handling message with content: {msgString}");
        throw;
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