using System;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Speechly.SLUClient {

  public class SpeechlyClient {
    public delegate void SegmentChangeDelegate(Segment segment);
    public delegate void StateChangeDelegate(ClientState state); // @TODO Future: Split to IsMicReady, IsConnected, IsListening
    public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
    public delegate void TranscriptDelegate(MsgTranscript msg);
    public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
    public delegate void EntityDelegate(MsgEntity msg);
    public delegate void IntentDelegate(MsgIntent msg);
    public delegate void StartStreamDelegate();
    public delegate void StopStreamDelegate();
    public delegate void StartContextDelegate();
    public delegate void StopContextDelegate();
    private delegate void ProcessResponseDelegate(MsgCommon msgCommon, string msgString);
    public SegmentChangeDelegate OnSegmentChange = (Segment segment) => {};
    public TentativeTranscriptDelegate OnTentativeTranscript = (msg) => {};
    public TranscriptDelegate OnTranscript = (msg) => {};
    public TentativeEntityDelegate OnTentativeEntity = (msg) => {};
    public EntityDelegate OnEntity = (msg) => {};
    public IntentDelegate OnTentativeIntent = (msg) => {};
    public IntentDelegate OnIntent = (msg) => {};
    public StateChangeDelegate OnStateChange = (ClientState state) => {};
    public StartStreamDelegate OnStartStream = () => {};
    public StopStreamDelegate OnStopStream = () => {};
    public StartContextDelegate OnStartContext = () => {};
    public StopContextDelegate OnStopContext = () => {};

    public bool IsAudioStreaming { get; private set; } = false;
    public bool IsListening { get; private set; } = false;
    public int SamplesSent { get; private set; } = 0;
    public ClientState State { get; private set; } = ClientState.Disconnected;
    // Optional message queue should messages be run in the main thread
    public EnergyTresholdVAD Vad { get; private set; } = null;
    public bool UseCloudSpeechProcessing { get; private set; } = true;
    public string AudioInputStreamIdentifier { get; private set; } = "utterance";

    /// 0-based local index of utterance within the stream
    public int UtteranceSerial { get; private set; } = -1;

    // Current count of continuously processed samples (thru ProcessAudio) from start of stream
    public int StreamSamplePos { get; private set; } = 0;

    private ConcurrentQueue<SegmentMessage> messageQueue = new ConcurrentQueue<SegmentMessage>();
    private ProcessResponseDelegate ProcessResponse = (MsgCommon msgCommon, string msgString) => {};
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

    private int inputSampleRate = 16000;
    private int internalSampleRate = 16000;
    private int frameMillis = 30;

    /// Total number of history frames to keep in memory. Will be sent upon a starting a new utterance.
    private int historyFrames = 5;

    private int frameSamples;

    private int streamFramePos = 0;
    private bool streamAutoStarted;
    private float[] sampleRingBuffer = null;
    private int frameSamplePos;
    private int currentFrameNumber = 0;


    public SpeechlyClient(
      string loginUrl = null,
      string apiUrl = null,
      string projectId = null,
      string appId = null,
      string deviceId = null,
      EnergyTresholdVAD vad = null, // @TODO Future: Allow different VAD implementation thru IVAD interface
      bool useCloudSpeechProcessing = true,
      bool manualUpdate = false,
      int frameMillis = 30,
      int historyFrames = 5,
      int inputSampleRate = 16000,
      string saveToFolder = null, // @TODO Future: Allow storing to memory stream as well for replay?
      bool debug = false
    ) {
      if (loginUrl != null) this.loginUrl = loginUrl;
      if (apiUrl != null) this.apiUrl = apiUrl;
      if (projectId != null) this.projectId = projectId;
      if (appId != null) this.appId = appId;
      this.Vad = vad;
      this.UseCloudSpeechProcessing = useCloudSpeechProcessing;
      this.manualUpdate = manualUpdate;
      this.frameMillis = Math.Max(frameMillis, 1);
      this.historyFrames = Math.Max(historyFrames, 1);  // Need at least 1 frame; the current one
      this.inputSampleRate = inputSampleRate;
      this.saveToFolder = saveToFolder;
      this.debug = debug;

      if (this.manualUpdate) {
        ProcessResponse = QueueMessage;
      } else {
        ProcessResponse = OnMessage;
      }

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

      frameSamples = internalSampleRate * frameMillis / 1000;

      if (saveToFolder != null) {
        Directory.CreateDirectory(saveToFolder);
      }

      sampleRingBuffer = new float[frameSamples * historyFrames];
    }

    public void StartStream(string streamIdentifier, bool auto = false) {
      if (!IsAudioStreaming) {
        IsAudioStreaming = true;
        streamAutoStarted = auto;
        streamFramePos = 0;
        StreamSamplePos = 0;
        frameSamplePos = 0;
        currentFrameNumber = 0;
        UtteranceSerial = -1;
        this.AudioInputStreamIdentifier = streamIdentifier;

        OnStartStream();
      }
    }

    public async Task StopStream(bool auto = false) {
      if (IsAudioStreaming) {
        if ((auto && streamAutoStarted) || !streamAutoStarted) {
          if (IsListening) {
            _ = StopContext();
          }

          // Process remaining frame samples
          await ProcessAudio(sampleRingBuffer, 0, frameSamplePos, true);

          IsAudioStreaming = false;
        
          OnStopStream();
        }
      }
    }

    ~SpeechlyClient() {
      _ = StopStream();
    }

    public async Task Connect() {
      if (State < ClientState.Connecting) {
        SetState(ClientState.Connecting);
        try {
          var tokenFetcher = new LoginToken();
          token = await tokenFetcher.FetchToken(loginUrl, projectId, appId, deviceId);

          if (debug) Logger.Log($"token: {token}");

          wsClient = new WsClient();
          wsClient.OnResponseReceived = OnResponse;

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

      if (!IsAudioStreaming) {
        StartStream(AudioInputStreamIdentifier, auto: true);
      }

      IsListening = true;
      SamplesSent = 0;
      UtteranceSerial++;

      string localBaseName = $"{AudioInputStreamIdentifier}_{UtteranceSerial.ToString().PadLeft(4, '0')}";
      string contextId = localBaseName;

      if (saveToFolder != null) {
        outAudioStream = new FileStream(Path.Combine(saveToFolder, $"{localBaseName}.raw"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
      }

      SetState(ClientState.Starting);
      OnStartContext();

      try {
        if (UseCloudSpeechProcessing) {
          startContextTCS = new TaskCompletionSource<MsgCommon>();
          Task t;
          if (appId != null) {
            t = wsClient.SendText($"{{\"event\": \"start\", \"appId\": \"{appId}\"}}");
          } else {
            t = wsClient.SendText($"{{\"event\": \"start\"}}");
          }
          await t;
          MsgCommon msgCommon = await startContextTCS.Task;
          contextId = msgCommon.audio_context;
        }

        SetState(ClientState.Recording);
        return contextId;
      } catch (Exception e) {
        SetState(ClientState.Connected);
        Logger.LogError(e.ToString());
        throw;
      }
    }

    public async Task ProcessAudioFile(string fileName) {
      string outBaseName = Path.GetFileNameWithoutExtension(fileName);
      var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      StartStream(outBaseName);
      await ProcessAudio(fileStream);
      await StopStream();
      fileStream.Close();
    }

    public async Task ProcessAudio(Stream fileStream) {
      // @TODO Use a pre-allocated buf
      var bytes = new byte[frameSamples * 2];
      var floats = new float[frameSamples];

      while (true) {
        int bytesRead = fileStream.Read(bytes, 0, bytes.Length);
        if (bytesRead == 0) break;
        int samples = bytesRead / 2;
        int processed = AudioTools.ConvertInt16ToFloat(in bytes, ref floats, 0, samples);
        // Pad with zeroes
        for (int i = floats.Length-1 ; i >= samples; i--) {
          floats[i] = 0;
        }
        await ProcessAudio(floats, 0, samples);
      }
    }

    public async Task ProcessAudio(float[] floats, int start = 0, int length = -1, bool forceSubFrameProcess = false) {
      if (length < 0) length = floats.Length;
      if (length == 0) return;

      int i = start;
      int endIndex = start + length;

      while (i < endIndex) {
        int frameBase = currentFrameNumber * frameSamples;

        if (inputSampleRate == internalSampleRate) {
          // Copy input samples to fill current ringbuffer frame
          int samplesToFillFrame = Math.Min(endIndex - i, frameSamples - frameSamplePos);
          int frameEndIndex = frameSamplePos + samplesToFillFrame;
          while (frameSamplePos < frameEndIndex) {
            sampleRingBuffer[frameBase + frameSamplePos++] = floats[i++];
          }
        } else {
          // Downsample input samples to fill current ringbuffer frame
          float ratio = 1f * inputSampleRate / internalSampleRate;
          int inputSamplesToFillFrame = Math.Min(endIndex - i, (int)Math.Round(ratio * (frameSamples - frameSamplePos)));
          int samplesToFillFrame = Math.Min((int)Math.Round((endIndex - i) / ratio), frameSamples - frameSamplePos);
          AudioTools.Downsample(floats, ref sampleRingBuffer, i,inputSamplesToFillFrame, frameBase+frameSamplePos,samplesToFillFrame);
          i += inputSamplesToFillFrame;
          frameSamplePos += samplesToFillFrame;
        }

        // Process frame
        if (frameSamplePos == frameSamples || forceSubFrameProcess) {
          frameSamplePos = 0;
          int subFrameSamples = forceSubFrameProcess ? frameSamplePos : frameSamples;

          if (!forceSubFrameProcess) {
            await ProcessFrame(sampleRingBuffer, frameBase, subFrameSamples);
          }

          if (IsListening) {
            if (SamplesSent == 0) {
              // Start of the utterance - send history frames
              int sendHistory = Math.Min(streamFramePos, historyFrames - 1);
              int historyFrameIndex = (currentFrameNumber + historyFrames - sendHistory) % historyFrames;
              while (historyFrameIndex != currentFrameNumber) {
                await SendAudio(sampleRingBuffer, historyFrameIndex * frameSamples, frameSamples);
                historyFrameIndex = (historyFrameIndex + 1) % historyFrames;
              }
            }
            await SendAudio(sampleRingBuffer, frameBase, subFrameSamples);
          }

          streamFramePos += 1;
          StreamSamplePos += subFrameSamples;
          currentFrameNumber = (currentFrameNumber + 1) % historyFrames;
        }
      }
    }

    private async Task ProcessFrame(float[] floats, int start = 0, int length = -1) {
      AnalyzeAudioFrame(in floats, start, length);
      await AutoControlListening();
    }

    private void AnalyzeAudioFrame(in float[] waveData, int s, int frameSamples) {
      if (this.Vad != null && this.Vad.Enabled) {
        Vad.ProcessFrame(waveData, s, frameSamples);
      }
    }

    private async Task AutoControlListening() {
      if (this.Vad != null && this.Vad.Enabled && this.Vad.VADControlListening) {
        if (!IsListening && Vad.IsSignalDetected) {
          await StartContext();
        }

        if (IsListening && !Vad.IsSignalDetected) {
          await StopContext();
        }
      }
    }

    private async Task SendAudio(float[] floats, int start = 0, int length = -1) {
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
      if (UseCloudSpeechProcessing) {
        await wsClient.SendBytes(new ArraySegment<byte>(buf));
      }
    }

    public async Task<string> StopContext() {
      if (!IsListening) {
        throw new Exception("Already stopped listening.");
      }
      IsListening = false;
      SetState(ClientState.Stopping);

      string localBaseName = $"{AudioInputStreamIdentifier}_{UtteranceSerial.ToString().PadLeft(4, '0')}";
      string contextId = localBaseName;

      try {

        if (saveToFolder != null) {
          outAudioStream.Close();
        }

        if (UseCloudSpeechProcessing) {
          stopContextTCS = new TaskCompletionSource<MsgCommon>();
          await wsClient.SendText($"{{\"event\": \"stop\"}}");
          MsgCommon msgCommon = await stopContextTCS.Task;
          contextId = msgCommon.audio_context;
        }
        SetState(ClientState.Connected);
        OnStopContext();
        return contextId;
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
      SegmentMessage segmentUpdateProps;
      while (messageQueue.TryDequeue(out segmentUpdateProps)) {
        OnMessage(segmentUpdateProps.msgCommon, segmentUpdateProps.msgString);
      }
    }

    private void SetState(ClientState state) {
      // Logger.Log($"{this.State} -> {state}");
      this.State = state;
      OnStateChange(state);
    }

    private void OnResponse(MemoryStream inputStream)
    {
      var msgString = Encoding.UTF8.GetString(inputStream.ToArray());
      try {
        // @TODO Find a way to deserialize only once
        var msgCommon = JSON.Parse(msgString, new MsgCommon());
        Logger.Log($"[WsClient] IN MSG {msgCommon.type}");
        switch (msgCommon.type) {
          case "started": {
            if (debug) Logger.Log($"Started message received '{msgCommon.audio_context}'");
            startContextTCS.SetResult(msgCommon);
            break;
          }
          case "stopped": {
            if (debug) Logger.Log($"Stopped message received '{msgCommon.audio_context}'");
            stopContextTCS.SetResult(msgCommon);
            break;
          }
        }
        ProcessResponse(msgCommon, msgString);
      } catch {
        Logger.LogError($"Error while handling message with content: {msgString}");
        throw;
      }
    }

    private void QueueMessage(MsgCommon msgCommon, string msgString) {
      messageQueue.Enqueue(new SegmentMessage(msgCommon, msgString));
    }

    private void OnMessage(MsgCommon msgCommon, string msgString)
    {
      switch (msgCommon.type) {
        case "started": {
          if (debug) Logger.Log($"Started context '{msgCommon.audio_context}'");
          activeContexts.Add(msgCommon.audio_context, new Dictionary<int, Segment>());
          break;
        }
        case "stopped": {
          if (debug) Logger.Log($"Stopped context '{msgCommon.audio_context}'");
          activeContexts.Remove(msgCommon.audio_context);
          break;
        }
        default: {
          OnSegmentMessage(msgCommon, msgString);
          break;
        }
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