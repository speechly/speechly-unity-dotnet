using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;

namespace Speechly.SLUClient {

/// <summary>
/// Create a new Speechly Spoken Language Understading (SLU) Client to process speech and provide the results of automatic speech recogition (ASR) and natural langugage understanding (NLU) using delegates.
/// 
/// #### Usage
///
/// - Create a new SpeechlyClient instance.
/// - Initialize a SLU decoder with <see cref="Initialize"/>.
/// - Attach delegates like <see cref="OnSegmentChange"/> to listen to SLU results.
/// - Feed audio to process with <see cref="ProcessAudio"/>.
/// - When you don't need SLU services any more call <see cref="Shutdown"/> to free resources.
///
/// You can control when to start and stop process speech either manually with <see cref="StartContext"/> and <see cref="StopContext"/> or
/// automatically by providing a voice activity detection (VAD) field to SpeechlyClient constructor.
/// </summary>

  public class SpeechlyClient {
    public delegate void SegmentChangeDelegate(Segment segment);
    public delegate void TentativeTranscriptDelegate(MsgTentativeTranscript msg);
    public delegate void TranscriptDelegate(MsgTranscript msg);
    public delegate void TentativeEntityDelegate(MsgTentativeEntity msg);
    public delegate void EntityDelegate(MsgEntity msg);
    public delegate void IntentDelegate(MsgIntent msg);
    public delegate void StartStreamDelegate();
    public delegate void StopStreamDelegate();
    public delegate void StartContextDelegate();
    public delegate void StopContextDelegate();

/// <summary>
/// Read the combined results of automatic speech recoginition (ASR) and natural language detection (NLU).
/// 
/// You can control when to start and stop process speech either manually with <see cref="StartContext"/> and <see cref="StopContext"/> or
/// automatically by providing a voice activity detection (VAD) field to <see cref="SpeechlyClient"/>.
/// </summary>

    public SegmentChangeDelegate OnSegmentChange = (Segment segment) => {};
    public TentativeTranscriptDelegate OnTentativeTranscript = (msg) => {};
    public TranscriptDelegate OnTranscript = (msg) => {};
    public TentativeEntityDelegate OnTentativeEntity = (msg) => {};
    public EntityDelegate OnEntity = (msg) => {};
    public IntentDelegate OnTentativeIntent = (msg) => {};
    public IntentDelegate OnIntent = (msg) => {};
    public StartStreamDelegate OnStartStream = () => {};
    public StopStreamDelegate OnStopStream = () => {};
    public StartContextDelegate OnStartContext = () => {};
    public StopContextDelegate OnStopContext = () => {};

    // Returns true when StartStream has been called
    public bool IsAudioStreaming { get; private set; } = false;

    /// Returns true when SLU Engine is ready for Start/StopContext calls
    public bool IsReady {
      get {
        return this.decoder != null;
      }
    }

    /// Returns true when StartContext is called and expecting StopContext next
    public bool IsListening { get; private set; } = false;

    public int SamplesSent { get; private set; } = 0;
    public EnergyTresholdVAD Vad { get; private set; } = null;

    /// Utterance identifier
    public string AudioInputStreamIdentifier { get; private set; } = "utterance";

    /// 0-based local index of utterance within the stream
    public int UtteranceSerial { get; private set; } = -1;

    /// Current count of continuously processed samples (thru ProcessAudio) from start of stream
    public int StreamSamplePos { get; private set; } = 0;

    /// Message queue used when manualUpdate is true. Delegates are fired with a call to Update() that should be run in the desired thread (main) thread.
    private ConcurrentQueue<SegmentMessage> messageQueue = new ConcurrentQueue<SegmentMessage>();
    private Dictionary<string, Dictionary<int, Segment>> activeContexts = new Dictionary<string, Dictionary<int, Segment>>();
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
    private IDecoder decoder;

/// <summary>
/// Create a new SpeechlyClient to process audio and fire delegates to provide SLU results.
/// </summary>
/// <param name="vad">Provide a voice activation detection (VAD) implementation to control automatic listening on/off. Null disables VAD. (default: `null`)</param>
/// <param name="historyFrames">Defines the size of the history as historyFrames * frameSamples (default: `5`)</param>
/// <param name="frameMillis">Defines the size of the history as historyFrames*frameSamples (default: `30` ms). History is sent upon StartContext to capture the start of utterance which especially important with VAD, which activates with a constant delay.</param>
/// <param name="manualUpdate">Setting `manualUpdate = true` postpones SpeechlyClient's delegates (OnSegmentChange, OnTranscript...) until you manually run <see cref="Update"/>. This enables you to call Unity API in SpeechlyClient's delegates, as Unity API should only be used in the main Unity thread. (Default: false)</param>
/// <param name="saveToFolder">Defines a local folder to save utterance files as 16 bit, 16000 hZ mono raw. Null disables saving. (default: `null`)</param>
/// <param name="inputSampleRate">Define the sample rate of incoming audio (default: `16000`)</param>
/// <param name="debug">Enable debug prints thru <see cref="Logger.Log"/> delegate. (default: `false`)</param>

    public SpeechlyClient(
      int frameMillis = 30,
      int historyFrames = 5,
      int inputSampleRate = 16000,
      bool manualUpdate = false,
      string saveToFolder = null, // @TODO Future: Allow storing to memory stream as well for replay?
      EnergyTresholdVAD vad = null, // @TODO Future: Allow different VAD implementation thru IVAD interface
      bool debug = false
    ) {

      this.frameMillis = Math.Max(frameMillis, 1);
      this.historyFrames = Math.Max(historyFrames, 1);  // Need at least 1 frame; the current one
      this.inputSampleRate = inputSampleRate;
      this.manualUpdate = manualUpdate;
      this.saveToFolder = saveToFolder;
      this.Vad = vad;
      this.debug = debug;

      frameSamples = internalSampleRate * frameMillis / 1000;

      if (saveToFolder != null) {
        Directory.CreateDirectory(saveToFolder);
      }

      sampleRingBuffer = new float[frameSamples * historyFrames];
    }

/// <summary>
/// Provide a SLU decoder instance for the SpeechlyClient.
/// The SLU decoder provides the automatic speech recognition (ASR) and
/// natural language understanding (NLU) capabilities.
/// The SLU delegates (OnSegmentChange, OnTranscript...) are only fired when SpeechlyClient is initialized with a SLU decoder.
/// </summary>
/// <param name="decoder">SLU decoder implementing IDecoder interface</param>
/// <returns>Task that completes when the decoder is ready.</returns>
    public async Task Initialize(IDecoder decoder) {
      if (this.decoder == null) {
        try {
          if (this.manualUpdate) {
            decoder.OnMessage += QueueMessage;
          } else {
            decoder.OnMessage += OnMessage;
          }
          await decoder.Initialize();
          this.decoder = decoder;
        } catch (Exception e) {
          Logger.LogError(e.ToString());
          throw;
        }
      }
    }

/// <summary>
/// `StartStream` should be called at start of a continuous audio stream. It resets the stream sample counters and history. For backwards compability, ProcessAudio and StartContext ensure it's been called.
/// `OnStreamStart` delegate is triggered upon a call to StartStream.
/// </summary>
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

/// <summary>
/// `StopStream` should be called at the end of a continuous audio stream.
/// `OnStreamStop` delegate is triggered upon a call to StopStream.
/// </summary>
    public void StopStream(bool auto = false) {
      if (IsAudioStreaming) {
        if ((auto && streamAutoStarted) || !streamAutoStarted) {
          if (IsListening) {
            _ = StopContext();
          }

          // Process remaining frame samples
          ProcessAudio(sampleRingBuffer, 0, frameSamplePos, true);

          IsAudioStreaming = false;
        
          OnStopStream();
        }
      }
    }

/// <summary>
/// Start listening for user speech and feeding it to the SLU decoder.
/// `OnContextStart` is triggered upon a call to StartContext. It's also triggered by automatic VAD activation.
///
/// You don't need to await this call if you don't need the utterance id.
/// </summary>
/// <param name="appId">Cloud decoder only: The Speechly app id to connect to, if not the default. If specified, you must use project id based login with cloud decoder.</param>
/// <returns>An unique utterance id.</returns>

    public Task<string> StartContext(string appId = null) {
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

      OnStartContext();

      if (this.decoder != null) {
        try {
          return this.decoder.StartContext();
        } catch (Exception e) {
          IsListening = false;
          Logger.LogError(e.ToString());
          throw;
        }
      }
      
      return Task.FromResult(contextId);
    }

    public void ProcessAudioFile(string fileName) {
      string outBaseName = Path.GetFileNameWithoutExtension(fileName);
      var fileStream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read);
      StartStream(outBaseName);
      ProcessAudio(fileStream);
      StopStream();
      fileStream.Close();
    }

/// <summary>
/// Process speech audio samples from a microphone or other audio source.
///
/// It's recommended to constantly feed new audio as long as you want to use Speechly's SLU services.
///
/// You can control when to start and stop process speech either manually with <see cref="StartContext"/> and <see cref="StopContext"/> or
/// automatically by providing a voice activity detection (VAD) field to <see cref="SpeechlyClient"/>.
/// 
/// The audio processing pipeline is as follows:
/// - INPUT: ProcessAudio()
/// - Downsample audio to 16kHz if needed (controlled by inputSampleRate in constructor)
/// - Add audio to history ringbuffer (controlled by HistoryFrames and FrameSamples in constructor)
/// - Energy threshold calculation (enabled by EnergyTresholdVAD in constructor)
/// - Automatic VAD Start/StopContext control (enabled by EnergyTresholdVAD.ControlListening = true)
/// - OUTPUT: Send utterances to files (enabled by SaveToFolder = "folder" in the constructor)
/// - OUTPUT: Send utterances to Speechly SLU decoder (passed with <see cref="Initialize"/>)
/// </summary>
/// <param name="floats">Array of float containing samples to feed to the audio pipeline. Each sample needs to be in range -1f..1f.</param>
/// <param name="start">Start index of audio to process in samples (default: `0`).</param>
/// <param name="length">Length of audio to process in samples or `-1` to process the whole array (default: `-1`).</param>
/// <param name="forceSubFrameProcess"><see cref="StopStream"/> internally uses this to force processing of last subframe at end of audio stream (default: `false`).</param>

    public void ProcessAudio(float[] floats, int start = 0, int length = -1, bool forceSubFrameProcess = false) {
      if (!IsAudioStreaming) {
        StartStream(AudioInputStreamIdentifier, auto: false);  // Assume no auto-start/stop if ProcessAudio call encountered before startContext call
      }

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
            ProcessFrame(sampleRingBuffer, frameBase, subFrameSamples);
          }

          if (IsListening) {
            
            if (SamplesSent == 0) {
              // Start of the utterance - send history frames
              int sendHistory = Math.Min(streamFramePos, historyFrames - 1);
              int historyFrameIndex = (currentFrameNumber + historyFrames - sendHistory) % historyFrames;
              while (historyFrameIndex != currentFrameNumber) {
                SendAudio(sampleRingBuffer, historyFrameIndex * frameSamples, frameSamples);
                historyFrameIndex = (historyFrameIndex + 1) % historyFrames;
              }
            }
            SendAudio(sampleRingBuffer, frameBase, subFrameSamples);
          }

          streamFramePos += 1;
          StreamSamplePos += subFrameSamples;
          currentFrameNumber = (currentFrameNumber + 1) % historyFrames;
        }
      }
    }

    public void ProcessAudio(Stream fileStream) {
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
        ProcessAudio(floats, 0, samples);
      }
    }

    private void ProcessFrame(float[] floats, int start = 0, int length = -1) {
      AnalyzeAudioFrame(in floats, start, length);
      AutoControlListening();
    }

    private void AnalyzeAudioFrame(in float[] waveData, int s, int frameSamples) {
      if (this.Vad != null && this.Vad.Enabled) {
        Vad.ProcessFrame(waveData, s, frameSamples);
      }
    }

    private void AutoControlListening() {
      if (this.Vad != null && this.Vad.Enabled && this.Vad.VADControlListening) {
        if (!IsListening && Vad.IsSignalDetected) {
          _ = StartContext();
        }

        if (IsListening && !Vad.IsSignalDetected) {
          _ = StopContext();
        }
      }
    }

    private void SendAudio(float[] floats, int start = 0, int length = -1) {
      SamplesSent += length;

      // Stream to file
      if (saveToFolder != null) {
        SaveToDisk(floats, start, length);
      }

      if (this.decoder != null) {
        this.decoder.SendAudio(floats, start, length);
      }
    }

    private void SaveToDisk(float[] floats, int start = 0, int length = -1) {
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

      outAudioStream.Write(buf, 0, length * 2);
    }

/// <summary>
/// Stop listening for user speech.
/// `OnContextStop` is triggered upon a call to StopContext. It's also triggered by automatic VAD deactivation.
///
/// You don't need to await this call if you don't need the utterance id.
/// </summary>
/// <returns>An unique utterance id.</returns>

    public Task<string> StopContext() {
      if (!IsListening) {
        throw new Exception("Already stopped listening.");
      }
      IsListening = false;

      string localBaseName = $"{AudioInputStreamIdentifier}_{UtteranceSerial.ToString().PadLeft(4, '0')}";
      string contextId = localBaseName;

      if (saveToFolder != null) {
        outAudioStream.Close();
      }

      OnStopContext();

      if (this.decoder != null) {
        try {
          return this.decoder.StopContext();
        } catch (Exception e) {
          Logger.LogError(e.ToString());
          throw;
        }
      }

      return Task.FromResult(contextId);
    }

/// <summary>
/// Call Update in your game loop to fire Speechly delegates manually if you want them to run in main UI/Unity thread.
/// </summary>
    public void Update() {
      SegmentMessage segmentUpdateProps;
      while (messageQueue.TryDequeue(out segmentUpdateProps)) {
        OnMessage(segmentUpdateProps.msgCommon, segmentUpdateProps.msgString);
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


/// <summary>
/// Closes any connections (e.g. cloud SLU) and frees resources.
/// </summary>
/// <returns>Task that completes with the shutdown.</returns>

    public async Task Shutdown() {
      StopStream();

      if (this.decoder != null) {
        await decoder.Shutdown();
      }
      this.decoder = null;
    }

  }
}