using System;
using System.IO;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Collections.Concurrent;
using Speechly.Types;
using Speechly.Tools;

namespace Speechly.SLUClient {

/// <summary>
/// Create a new Speechly Spoken Language Understading (SLU) Client to process speech and provide the results of automatic speech recogition (ASR) and natural langugage understanding (NLU) using delegates.
/// 
/// #### Usage
///
/// - Create a new SpeechlyClient instance.
/// - Create an SLU decoder (<see cref="CloudDecoder"/>) and pass it to `SpeechlyClient`'s <see cref="Initialize"/>.
/// - Attach delegates like <see cref="OnSegmentChange"/> to listen to and handle the SLU results.
/// - Feed audio to process with <see cref="ProcessAudio"/>.
/// - (delegates are firing up as speech is processed)
/// - When you don't need SLU services any more call <see cref="Shutdown"/> to free resources.
///
/// You can feed audio continuously, but control when to start and stop process speech with <see cref="Start"/> and <see cref="Stop"/> or
/// let voice activity detection (VAD) handle that automatically by passing <see cref="EnergyThresholdVAD"/> to SpeechlyClient constructor.
/// </summary>

  public class SpeechlyClient {

/// <summary>
/// Read the combined results of automatic speech recoginition (ASR) and natural language detection (NLU).
/// 
/// You can control when to start and stop process speech either manually with <see cref="Start"/> and <see cref="Stop"/>.
/// Alternatively, you may let SpeechlyClient control this automatically by setting setting AudioProcessorOptions.VADControlsListening to true upon SpeechlyClient.Initialize() or by calling SpeechlyClient.AdjustAudioProcessor(true).
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
    public StartDelegate OnStart = () => {};
    public StopDelegate OnStop = () => {};

    public AudioInfo Output { get; private set; }
    private AudioProcessor AudioProcessor;

    // Returns true when StartStream has been called
    public bool IsAudioStreaming { get; private set; } = false;

    /// Returns true when SLU Engine is ready for Start/Stop calls
    public bool IsReady {
      get {
        return this.decoder != null;
      }
    }

    /// Returns true when Start is called and expecting Stop next
    public bool IsActive { get; private set; } = false;

    /// Utterance identifier
    public string AudioInputStreamIdentifier { get; private set; } = "utterance";

    /// Enable debug prints
    public bool Debug = false;

    /// Message queue used when manualUpdate is true. Delegates are fired with a call to Update() that should be run in the desired thread (main) thread.
    private ConcurrentQueue<SegmentMessage> messageQueue = new ConcurrentQueue<SegmentMessage>();
    private Dictionary<string, Dictionary<int, Segment>> activeContexts = new Dictionary<string, Dictionary<int, Segment>>();
    private AudioProcessorOptions audioProcessorOptions;
    private ContextOptions contextOptions;
    private bool manualUpdate;
    private string saveToFolder = null;
    private FileStream outAudioStream;

    private bool streamAutoStarted;

    private IDecoder decoder;

/// <summary>
/// Create a new SpeechlyClient to process audio and fire delegates to provide SLU results.
/// </summary>
/// <param name="vad"><see cref="EnergyThresholdVAD"/> instance to control automatic listening on/off. Null disables VAD. (default: `null`)</param>
/// <param name="historyFrames">Count of the audio history frames (default: `5`). Total history duration will be historyFrames * frameSamples.</param>
/// <param name="frameMillis">Size of one audio frame (default: `30` ms). Total history duration will be historyFrames*frameSamples. History is sent upon Start to capture the start of utterance which especially important with VAD, which activates with a constant delay.</param>
/// <param name="manualUpdate">Setting `manualUpdate = true` postpones SpeechlyClient's delegates (OnSegmentChange, OnTranscript...) until you manually run <see cref="Update"/>. This enables you to call Unity API in SpeechlyClient's delegates, as Unity API should only be used in the main Unity thread. (Default: false)</param>
/// <param name="saveToFolder">Defines a local folder to save utterance files as 16 bit, 16000 Hz mono raw. Null disables saving. (default: `null`)</param>
/// <param name="inputSampleRate">Define the sample rate of incoming audio (default: `16000`)</param>
/// <param name="debug">Enable debug prints thru <see cref="Logger.Log"/> delegate. (default: `false`)</param>

    public SpeechlyClient(
      bool manualUpdate = false,
      string saveToFolder = null, // @TODO Future: Allow storing to memory stream as well for replay?
      AudioInfo output = null,
      bool debug = false
    ) {

      this.manualUpdate = manualUpdate;
      this.saveToFolder = saveToFolder;
      this.Debug = debug;

      if (output == null) {
        this.Output = new AudioInfo();
      } else {
        this.Output = output;
      }

      if (saveToFolder != null) {
        Directory.CreateDirectory(saveToFolder);
      }
    }

/// <summary>
/// SLU decoder instance to use like <see cref="CloudDecoder"/>.
///
/// The SLU decoder provides the automatic speech recognition (ASR) and
/// natural language understanding (NLU) capabilities via SLU delegates (OnSegmentChange, OnTranscript...).
/// </summary>
/// <param name="decoder">SLU decoder implementing IDecoder interface like <see cref="CloudDecoder"/>.</param>
/// <returns>Task that completes when the decoder is ready.</returns>
    public async Task Initialize(IDecoder decoder, AudioProcessorOptions audioProcessorOptions = null, ContextOptions contextOptions = null, bool preferLibSpeechlyAudioProcessor = false) {
      if (audioProcessorOptions == null) {
        audioProcessorOptions = new AudioProcessorOptions();
      }
      this.audioProcessorOptions = audioProcessorOptions;

      if (contextOptions == null) {
        contextOptions = new ContextOptions();
      }
      this.contextOptions = contextOptions;

      if (this.decoder == null && decoder != null) {
        try {
          if (this.manualUpdate) {
            decoder.OnMessage += QueueMessage;
          } else {
            decoder.OnMessage += OnMessage;
          }
          await decoder.Initialize(
            audioProcessorOptions,
            contextOptions,
            this.Output
          );
          this.decoder = decoder;
        } catch (Exception e) {
          Logger.LogError(e.ToString());
          throw;
        }
      }

      if (!preferLibSpeechlyAudioProcessor) {
        this.AudioProcessor = new AudioProcessor(
          audioProcessorOptions,
          this.Output,
          this.Debug
        );

        this.AudioProcessor.OnSendAudio += SendAudio;
        this.AudioProcessor.OnVadStateChange += (isSignalDetected) => {
          if (isSignalDetected) {
            _ = Start();
          } else {
            _ = Stop();
          }
        };
      }

    }

/// <summary>
/// `StartStream` should be called at start of a continuous audio stream. It resets the stream sample counters and history. For backwards compability, ProcessAudio and Start ensure it's been called.
/// `OnStreamStart` delegate is triggered upon a call to StartStream.
/// </summary>
    public void StartStream(string streamIdentifier, bool auto = false) {
      if (!IsAudioStreaming) {
        streamAutoStarted = auto;

        IsAudioStreaming = true;
        AudioInputStreamIdentifier = streamIdentifier;
        AudioProcessor?.Reset();

        OnStartStream();

        // Start receiving results if using libSpeechly AudioProcessor
        if (AudioProcessor == null && audioProcessorOptions.VADControlsListening) {
          _ = Start();
        }

      }
    }

/// <summary>
/// `StopStream` should be called at the end of a continuous audio stream.
/// `OnStreamStop` delegate is triggered upon a call to StopStream.
/// </summary>
    public void StopStream(bool auto = false) {
      if (IsAudioStreaming) {
        if ((auto && streamAutoStarted) || !streamAutoStarted) {
          if (IsActive) {
            _ = Stop();
          }

          IsAudioStreaming = false;
        
          OnStopStream();
        }
      }
    }

/// <summary>
/// Control AudioProcessor parameters.
/// </summary>
/// <param name="vadControlsListening">`true` enables VAD to control listening. `false` disables VAD feature and stops listening immediately. `null` for no change.</param>

    public void AdjustAudioProcessor(bool? vadControlsListening = null) {
      if (vadControlsListening != null) {
        this.audioProcessorOptions.VADControlsListening = (bool)vadControlsListening;
        // Start audio flow if using libSpeechly
        if (AudioProcessor == null) {
          if (this.audioProcessorOptions.VADControlsListening && !IsActive) {
            _ = Start();
          }
        }
        // Ensure listening is stopped when VAD control is toggled off
        if (!this.audioProcessorOptions.VADControlsListening && IsActive) {
          _ = Stop();
        }
      }
    }

/// <summary>
/// Start listening for user speech and feeding it to the SLU decoder.
/// `OnContextStart` is triggered upon a call to Start. It's also triggered by automatic VAD activation.
///
/// You don't need to await this call if you don't need the utterance id.
/// </summary>
/// <param name="appId">Cloud decoder only: The Speechly app id to connect to, if not the default. If specified, you must use project id based login with cloud decoder.</param>
/// <returns>An unique utterance id.</returns>

    public Task<string> Start(string appId = null) {
      if (IsActive) {
        throw new Exception("Already listening.");
      }

      if (!IsAudioStreaming) {
        StartStream(AudioInputStreamIdentifier, auto: true);
      }

      IsActive = true;
      string localBaseName;
      
      if (AudioProcessor != null) {
        AudioProcessor.Start();
        localBaseName = $"{AudioInputStreamIdentifier}_{Output.UtteranceSerial.ToString().PadLeft(4, '0')}";
      } else {
        localBaseName = $"{AudioInputStreamIdentifier}";
      }
      string contextId = localBaseName;

      if (saveToFolder != null) {
        outAudioStream = new FileStream(Path.Combine(saveToFolder, $"{localBaseName}.raw"), FileMode.OpenOrCreate, FileAccess.Write, FileShare.None);
      }

      OnStart();

      if (this.decoder != null) {
        try {
          return this.decoder.Start();
        } catch (Exception e) {
          IsActive = false;
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
/// It's recommended to feed audio early and often instead of large chunks to benefit from real-time ASR and NLU output.
///
/// You can control when to start and stop process speech either manually with <see cref="Start"/> and <see cref="Stop"/>.
/// Alternatively you may use automatic voice activity detection (VAD) with <see cref="AudioProcessorOptions"/> passed to <see cref="Initialize"/>.
/// 
/// The audio is handled as follows:
/// - Downsample to 16kHz if needed
/// - Add to history ringbuffer
/// - Calculate energy (VAD)
/// - Automatic Start/Stop (VAD)
/// - Send utterance audio to a file
/// - Send utterance audio to Speechly SLU decoder
/// </summary>
/// <param name="floats">Array of float containing samples to feed to the audio pipeline. Each sample needs to be in range -1f..1f.</param>
/// <param name="start">Start index of audio to process in samples (default: `0`).</param>
/// <param name="length">Length of audio to process in samples or `-1` to process the whole array (default: `-1`).</param>

    public void ProcessAudio(float[] floats, int start = 0, int length = -1) {
      if (!IsAudioStreaming) {
        StartStream(AudioInputStreamIdentifier, auto: false);  // Assume no auto-start/stop if ProcessAudio call encountered before startContext call
      }

      if (AudioProcessor != null) {
        AudioProcessor.ProcessAudio(floats, start, length);
      } else {
        if (IsActive) {
          SendAudio(floats, start, length);
        }
      }
    }

    public void ProcessAudio(Stream fileStream) {
      const int CHUNK_SAMPLES = 16000;
      var bytes = new byte[CHUNK_SAMPLES * 2];
      var floats = new float[CHUNK_SAMPLES];

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

    internal void SendAudio(float[] floats, int start = 0, int length = -1) {
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
/// `OnContextStop` is triggered upon a call to Stop. It's also triggered by automatic VAD deactivation.
///
/// You don't need to await this call if you don't need the utterance id.
/// </summary>
/// <returns>An unique utterance id.</returns>

    public Task<string> Stop() {
      if (!IsActive) {
        throw new Exception("Already stopped listening.");
      }
      AudioProcessor?.Stop();
      IsActive = false;

      string localBaseName = $"{AudioInputStreamIdentifier}_{Output.UtteranceSerial.ToString().PadLeft(4, '0')}";
      string contextId = localBaseName;

      if (saveToFolder != null) {
        outAudioStream.Close();
      }

      OnStop();

      if (this.decoder != null) {
        try {
          return this.decoder.Stop();
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
          if (Debug) Logger.Log($"Started context '{msgCommon.audio_context}'");
          activeContexts.Add(msgCommon.audio_context, new Dictionary<int, Segment>());
          break;
        }
        case "stopped": {
          if (Debug) Logger.Log($"Stopped context '{msgCommon.audio_context}'");
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
          if (Debug) Logger.Log(segmentState.ToString());
          OnTentativeTranscript(msg);
          break;
        }
        case "transcript": {
          var msg = JSON.Parse(msgString, new MsgTranscript());
          msg.data.isFinal = true;
          segmentState.UpdateTranscript(msg.data);
          if (Debug) Logger.Log(segmentState.ToString());
          OnTranscript(msg);
          break;
        }
        case "tentative_entities": {
          var msg = JSON.Parse(msgString, new MsgTentativeEntity());
          segmentState.UpdateEntity(msg.data.entities);
          if (Debug) Logger.Log(segmentState.ToString());
          OnTentativeEntity(msg);
          break;
        }
        case "entity": {
          var msg = JSON.Parse(msgString, new MsgEntity());
          msg.data.isFinal = true;
          segmentState.UpdateEntity(msg.data);
          if (Debug) Logger.Log(segmentState.ToString());
          OnEntity(msg);
          break;
        }
        case "tentative_intent": {
          var msg = JSON.Parse(msgString, new MsgIntent());
          segmentState.UpdateIntent(msg.data.intent, false);
          if (Debug) Logger.Log(segmentState.ToString());
          OnTentativeIntent(msg);
          break;
        }
        case "intent": {
          var msg = JSON.Parse(msgString, new MsgIntent());
          segmentState.UpdateIntent(msg.data.intent, true);
          if (Debug) Logger.Log(segmentState.ToString());
          OnIntent(msg);
          break;
        }
        case "segment_end": {
          segmentState.EndSegment();
          if (Debug) Logger.Log(segmentState.ToString());
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

internal struct SegmentMessage {
  public MsgCommon msgCommon;
  public string msgString;
  public SegmentMessage(MsgCommon msgCommon, string msgString) {
    this.msgCommon = msgCommon;
    this.msgString = msgString;
  }
}
