using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Speechly.Tools;
using Speechly.Types;
using Logger = Speechly.Tools.Logger;

namespace Speechly.SLUClient {
  public delegate Task<byte[]> ModelDataProvider();

  public class OnDeviceDecoder : IDecoder
  {
    private const string ERROR_BOILERPLATE = "An error occurred with the following call:\n";

    /// Get/SetParam{I/F} param_ids from Constants.h

    /// @warning EXPERIMENTAL Decoder Info readonly
    private const uint SPEECHLY_DECODER_INFO_PROGRESS_MS_I =             500;
    /// @warning EXPERIMENTAL VAD signal-to-noise energy ratio needed for frame to be 'loud'. Use as param_id to Get/SetParamF().
    private const uint SPEECHLY_VAD_SIGNAL_TO_NOISE_DB_F =               1000;
    /// @warning EXPERIMENTAL VAD Energy threshold - below this won't trigger activation. Range (-90.0f, 0.0f). Use as param_id to Get/SetParamF().
    private const uint SPEECHLY_VAD_NOISE_GATE_DB_F =                    1001;
    /// @warning EXPERIMENTAL VAD Rate of background noise learn. Defined as duration in which background noise energy is moved halfway towards current frame's energy. Range (0, 5000). Use as param_id to Get/SetParamI().
    private const uint SPEECHLY_VAD_NOISE_LEARN_HALFTIME_MS_I =          1002;
    /// @warning EXPERIMENTAL VAD 'loud' to 'silent' ratio in signal_search_frames to activate 'is_signal_detected'. Range(.0f, 1.0f). Use as param_id to Get/SetParamF().
    private const uint SPEECHLY_VAD_SIGNAL_ACTIVATION_F =                1003;
    /// @warning EXPERIMENTAL VAD 'loud' to 'silent' ratio in signal_search_frames to keep 'is_signal_detected' active. Only evaluated when the sustain period is over. Range(.0f, 1.0f). Use as param_id to Get/SetParamF().
    private const uint SPEECHLY_VAD_SIGNAL_RELEASE_F =                   1004;
    /// @warning EXPERIMENTAL VAD duration to keep 'is_signal_detected' active. Renewed as long as VADActivation is holds true. Range(0, 8000). Use as param_id to Get/SetParamI().
    private const uint SPEECHLY_VAD_SIGNAL_SUSTAIN_MS_I =                1005;
    /// @warning EXPERIMENTAL VAD number of past audio frames analyzed by energy threshold VAD. Range(1, 32). Use as param_id to Get/SetParamI().
    private const uint SPEECHLY_VAD_SIGNAL_SEARCH_FRAMES_I =             1006;
    /// @warning EXPERIMENTAL VAD Info readonly signal db at last processed frame
    private const uint SPEECHLY_VAD_INFO_SIGNAL_DB_F =                   1007;
    /// @warning EXPERIMENTAL VAD Info readonly adaptive noise level at last processed prame
    private const uint SPEECHLY_VAD_INFO_NOISE_LEVEL_DB_F =              1008;
    /// @warning EXPERIMENTAL VAD_INFO READONLY 1 if VAD has detected signal and is sending audio for decoding after last processed frame. 0 if not.
    private const uint SPEECHLY_VAD_INFO_IS_SIGNAL_DETECTED_I =          1009;

    // End: Get/SetParam{I/F} param_ids

    private const uint SPEECHLY_ERROR_UNEXPECTED_PARAMETER = 64;
    private const uint SPEECHLY_ERROR_EXPIRED_MODEL = 32;
    private const uint SPEECHLY_ERROR_INVALID_MODEL = 16;
    private const uint SPEECHLY_ERROR_MISMATCH_IN_MODEL_ARCHITECTURE = 8;
    private const uint SPEECHLY_ERROR_UNEXPECTED_PARAMETER_VALUE = 4;
    private const uint SPEECHLY_ERROR_MEMORY_ERROR = 2;
    private const uint SPEECHLY_ERROR_UNEXPECTED_ERROR = 1;
    private const uint SPEECHLY_ERROR_NONE = 0;

    // libSpeechly Decoder.h API

    [DllImport ("SpeechlyDecoder")]
    private static extern IntPtr DecoderFactory_CreateFromModelArchive([MarshalAs(UnmanagedType.LPArray)] byte[] buf, int buf_len, ref DecoderError error);
    // EXPORT DecoderFactoryHandle *DecoderFactory_CreateFromModelArchive(const void *buf, size_t buf_len, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern IntPtr DecoderFactory_Create(string encoder_path, string predictor_path, string joint_path, string feat_path, string subwords_path);
    // EXPORT DecoderFactoryHandle *DecoderFactory_Create(char *encoder_path, char *predictor_path, char *joint_path, char *feat_path, char *subwords_path);

    [DllImport ("SpeechlyDecoder")]
    private static extern IntPtr DecoderFactory_GetBundleId(IntPtr decoderFactoryHandle, ref DecoderError error);
    // EXPORT char *DecoderFactory_GetBundleId(DecoderFactoryHandle *handle, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void DecoderFactory_SetSegmentationDelay(IntPtr decoderHandle, int milliseconds, ref DecoderError error);
    // EXPORT void DecoderFactory_SetSegmentationDelay(DecoderFactoryHandle *handle, int milliseconds, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void DecoderFactory_SetBoostVocabulary(IntPtr decoderHandle, string vocabulary, float weight, ref DecoderError error);
    // EXPORT void DecoderFactory_SetBoostVocabulary(DecoderFactoryHandle *handle, char *vocabulary, float weight, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern IntPtr DecoderFactory_GetDecoder(IntPtr decoderFactoryHandle, string device_id, ref DecoderError error);
    // EXPORT DecoderHandle *DecoderFactory_GetDecoder(DecoderFactoryHandle *handle, char *device_id, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void DecoderFactory_Destroy(IntPtr decoderFactoryHandle);    
    // void DecoderFactory_Destroy(DecoderFactoryHandle *handle);

    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_WriteSamples(IntPtr decoderHandle, IntPtr samples, int samples_size, int end_of_input, ref DecoderError error);
    // EXPORT void Decoder_WriteSamples(DecoderHandle *handle, float *samples, size_t samples_size, int end_of_input, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern IntPtr Decoder_WaitResults(IntPtr decoderHandle, ref DecoderError error);
    // EXPORT struct CResultWord *Decoder_WaitResults(DecoderHandle *handle, DecoderError *error);

    // EXPORT struct CResultWord *Decoder_GetResults(DecoderHandle *handle, DecoderError *error);
    // EXPORT int Decoder_GetNumSamples(DecoderHandle *handle);
    // EXPORT int Decoder_GetNumVoiceSamples(DecoderHandle *handle);
    // EXPORT int Decoder_GetNumCharacters(DecoderHandle *handle);

    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_Destroy(IntPtr decoderHandle);    
    // void Decoder_Destroy(DecoderHandle *handle);

    [DllImport ("SpeechlyDecoder")]
    private static extern void CResultWord_Destroy(IntPtr result_word);
    // void CResultWord_Destroy(struct CResultWord *result_word);

    // EXPORT const char* SpeechlyDecoderVersion();
    // EXPORT unsigned int SpeechlyDecoderBuild();


    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_SetInputSampleRate(IntPtr decoderHandle, int sample_rate, ref DecoderError error);
    // EXPORT void Decoder_SetInputSampleRate(DecoderHandle *handle, int sample_rate, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_EnableVAD(IntPtr decoderHandle, int enabled, ref DecoderError error);
    // EXPORT void Decoder_EnableVAD(DecoderHandle *handle, int enabled, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern int Decoder_GetParamI(IntPtr decoderHandle, uint param_id, ref DecoderError error);
    // EXPORT int Decoder_GetParamI(DecoderHandle *handle, unsigned int param_id, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_SetParamI(IntPtr decoderHandle, uint param_id, int value, ref DecoderError error);
    // EXPORT void Decoder_SetParamI(DecoderHandle *handle, unsigned int param_id, int value, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern float Decoder_GetParamF(IntPtr decoderHandle, uint param_id, ref DecoderError error);
    // EXPORT float Decoder_GetParamF(DecoderHandle *handle, unsigned int param_id, DecoderError *error);

    [DllImport ("SpeechlyDecoder")]
    private static extern void Decoder_SetParamF(IntPtr decoderHandle, uint param_id, float value, ref DecoderError error);
    // EXPORT void Decoder_SetParamF(DecoderHandle *handle, unsigned int param_id, float value, DecoderError *error);

    override internal event ResponseReceivedDelegate OnMessage = (MsgCommon msgCommon, string msgString) => {};

    private const string segmentEndToken = "@";

    private CancellationTokenSource CTS;

    struct CResultWord {
      public IntPtr word; // The recognized word (char*)
      public int start_time; // The start time of the word in ms relative to start of the audio
      public int end_time; // The start time of the word in ms relative to start of the audio
    };

    struct DecoderError {
      public uint error_code;
    };

    private IntPtr decoderFactoryHandle;
    private IntPtr decoderHandle;
    private Task receiveTask;
    private int wordCounter;
    private string deviceId;
    private bool debug = false;
    private int contextSerial = 0;
    private int segmentIndex = 0;
    private ModelDataProvider modelBundleProvider;
    private ConcurrentQueue<string> activeContexts = new ConcurrentQueue<string>();
    private DecoderError error = new DecoderError();
    private AudioInfo Output;

    public OnDeviceDecoder(ModelDataProvider modelBundleProvider, string deviceId, bool debug = false) {
      this.deviceId = deviceId;
      this.modelBundleProvider = modelBundleProvider;
      this.debug = debug;
    }

    override internal async Task Initialize(AudioProcessorOptions audioProcessorOptions, ContextOptions contextOptions, AudioInfo audioInfo) {
      this.Output = audioInfo;
      byte[] bundle_buf;

      if (debug) Logger.Log("Initializing on-device SLU from model byte buffers...");

      try {
        bundle_buf = await this.modelBundleProvider();
      } catch (Exception e) {
        throw new Exception($"Failed to load Speechly model bundle. Please check if the file exists.\n{e.Message}");
      }

      if (bundle_buf == null || bundle_buf.Length == 0) {
        throw new Exception($"Could not load Speechly model bundle or it has zero length.\nAre you trying to load placeholder dummy.bundle? Please contact Speechly to enable on-device support.");
      }

      decoderFactoryHandle = DecoderFactory_CreateFromModelArchive(bundle_buf, bundle_buf.Length, ref error);

      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = $"Error while loading Speechly model files.\n";
        errorDescription += $"DecoderFactory_CreateFromModelArchive code {error.error_code}: ";
        if (0 != (error.error_code & SPEECHLY_ERROR_MISMATCH_IN_MODEL_ARCHITECTURE)) {
          errorDescription += $"SPEECHLY_ERROR_MISMATCH_IN_MODEL_ARCHITECTURE ";
        }
        if (0 != (error.error_code & SPEECHLY_ERROR_INVALID_MODEL)) {
          errorDescription += $"SPEECHLY_ERROR_INVALID_MODEL ";
        }
        if (0 != (error.error_code & SPEECHLY_ERROR_EXPIRED_MODEL)) {
          errorDescription += $"SPEECHLY_ERROR_EXPIRED_MODEL ";
          // Throw a special exception for model expiration so it can be catched
          throw new ModelExpiredException($"{errorDescription}\n");
        }
        throw new Exception($"{errorDescription}\n");
      }

      if (decoderFactoryHandle == null) {
        throw new Exception($"Unknown error with DecoderFactory_CreateFromBuffers. There's probably something wrong with the Speechly model file.");
      }

      DecoderFactory_SetSegmentationDelay(decoderFactoryHandle, contextOptions.SilenceSegmentationMillis, ref error);
      
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"DecoderFactory_SetSegmentationDelay code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }

      if (contextOptions != null) {
        if (contextOptions.BoostVocabulary != null) {
          SetBoostVocabulary(contextOptions.BoostVocabulary.Vocabulary, contextOptions.BoostVocabulary.Weight);
        }
      }

      decoderHandle = DecoderFactory_GetDecoder(decoderFactoryHandle, this.deviceId, ref error);

      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = $"Error while instantiating decoder.\n";
        errorDescription += $"DecoderFactory_GetDecoder code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }

      if (decoderHandle == null) {
        throw new Exception($"Whoops. An error occurred with DecoderFactory_GetDecoder. Please contact Speechly.");
      }

      if (audioProcessorOptions != null) {
        if (audioProcessorOptions.VADControlsListening) {
          if (debug) Logger.Log("Using libSpeechly Audio Processor");
          EnableVAD(true);
        }

        // Use input sample rate. C# AudioProcessor should be inactive, so it's not downsampling
        SetInputSampleRate(audioProcessorOptions.InputSampleRate);
        SetVADConfiguration(audioProcessorOptions.VADSettings);
      }

      CTS = new CancellationTokenSource();
      receiveTask = Task.Factory.StartNew(ReceiveLoop, CTS.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
      if (debug) Logger.Log("On-device SLU ready");
    }

    internal void SetBoostVocabulary(string vocabulary, float weight) {
      DecoderFactory_SetBoostVocabulary(decoderFactoryHandle, vocabulary, weight, ref error);
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"DecoderFactory_SetBoostVocabulary code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }
    }

    internal void SetInputSampleRate(int sampleRate) {
      Decoder_SetInputSampleRate(decoderHandle, sampleRate, ref error);
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"Decoder_SetInputSampleRate code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }
    }    

    internal void EnableVAD(bool enabled) {
      Decoder_EnableVAD(decoderHandle, enabled ? 1 : 0, ref error);
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"Decoder_EnableVAD code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }
    }

    internal void SetVADConfiguration(VADOptions vadSettings) {
      uint compound_error = 0;
      Decoder_SetParamF(decoderHandle, SPEECHLY_VAD_SIGNAL_TO_NOISE_DB_F, vadSettings.SignalToNoiseDb, ref error); compound_error |= error.error_code;
      Decoder_SetParamF(decoderHandle, SPEECHLY_VAD_NOISE_GATE_DB_F, vadSettings.NoiseGateDb, ref error); compound_error |= error.error_code;
      Decoder_SetParamI(decoderHandle, SPEECHLY_VAD_NOISE_LEARN_HALFTIME_MS_I, vadSettings.NoiseLearnHalftimeMillis, ref error); compound_error |= error.error_code;
      Decoder_SetParamF(decoderHandle, SPEECHLY_VAD_SIGNAL_ACTIVATION_F, vadSettings.SignalActivation, ref error); compound_error |= error.error_code;
      Decoder_SetParamF(decoderHandle, SPEECHLY_VAD_SIGNAL_RELEASE_F, vadSettings.SignalRelease, ref error); compound_error |= error.error_code;
      Decoder_SetParamI(decoderHandle, SPEECHLY_VAD_SIGNAL_SUSTAIN_MS_I, vadSettings.SignalSustainMillis, ref error); compound_error |= error.error_code;
      Decoder_SetParamI(decoderHandle, SPEECHLY_VAD_SIGNAL_SEARCH_FRAMES_I, vadSettings.SignalSearchFrames, ref error); compound_error |= error.error_code;
      
      if (SPEECHLY_ERROR_NONE != compound_error) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"SetVADConfiguration code {compound_error}: ";
        if (0 != (error.error_code & SPEECHLY_ERROR_UNEXPECTED_PARAMETER)) {
          errorDescription += $"SPEECHLY_ERROR_UNEXPECTED_PARAMETER ";
        }
        if (0 != (error.error_code & SPEECHLY_ERROR_UNEXPECTED_PARAMETER_VALUE)) {
          errorDescription += $"SPEECHLY_ERROR_UNEXPECTED_PARAMETER_VALUE ";
        }
        throw new Exception($"{errorDescription}\n");
      }
    }

    override internal Task<string> Start() {
      wordCounter = 0;
      segmentIndex = 0;
      contextSerial++;
      string contextId = $"ondevice_{contextSerial}";
      activeContexts.Enqueue(contextId);
      OnMessage(new MsgCommon{type = "started", audio_context = contextId, segment_id = 0}, "");
      return Task.FromResult(contextId);
    }

    override internal Task<string> Stop() {
      var samples = new float[0];

      var sampleGCHandle = GCHandle.Alloc(samples, GCHandleType.Pinned); // Pin the array
      IntPtr sampleAddr = Marshal.UnsafeAddrOfPinnedArrayElement(samples, 0);
      Decoder_WriteSamples(decoderHandle, sampleAddr, 0, 1, ref error);
      sampleGCHandle.Free();
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"Decoder_WriteSamples code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }
      string contextId;
      if (!activeContexts.TryPeek(out contextId)) throw new Exception("No active context");
      return Task.FromResult(contextId);
    }

    override internal void SendAudio(float[] samples, int start = 0, int length = -1) {
      SendAudio(samples, start, length, false);
    }

    internal void SendAudio(float[] samples, int start = 0, int length = -1, bool final = false) {
      if (length < 0) length = samples.Length;

      var sampleGCHandle = GCHandle.Alloc(samples, GCHandleType.Pinned); // Pin the array
      IntPtr sampleAddr = Marshal.UnsafeAddrOfPinnedArrayElement(samples, start);
      Decoder_WriteSamples(decoderHandle, sampleAddr, length, final ? 1 : 0, ref error);
      sampleGCHandle.Free();
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = ERROR_BOILERPLATE;
        errorDescription += $"Decoder_WriteSamples code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }

      // Update the latest signal levels
      Output.SignalDb = Decoder_GetParamF(decoderHandle, SPEECHLY_VAD_INFO_SIGNAL_DB_F, ref error);
      Output.NoiseLevelDb = Decoder_GetParamF(decoderHandle, SPEECHLY_VAD_INFO_NOISE_LEVEL_DB_F, ref error);
      Output.IsSignalDetected = Decoder_GetParamI(decoderHandle, SPEECHLY_VAD_INFO_IS_SIGNAL_DETECTED_I, ref error) == 1 ? true : false;
      if (SPEECHLY_ERROR_NONE != error.error_code) {
        string errorDescription = "An error occurred while querying libSpeechly VAD state:\n";
        errorDescription += $"Error code {error.error_code}.";
        throw new Exception($"{errorDescription}\n");
      }
    }

    private void ReceiveLoop()
    {
      try
      {
        bool endOfAudioStream = false;
        while (!(endOfAudioStream && CTS.Token.IsCancellationRequested)) {
          IntPtr resultsHandle = Decoder_WaitResults(decoderHandle, ref error);
          if (SPEECHLY_ERROR_NONE != error.error_code) {
            string errorDescription = ERROR_BOILERPLATE;
            errorDescription += $"Decoder_WaitResults code {error.error_code}.";
            throw new Exception($"{errorDescription}\n");
          }
          CResultWord resultWord = Marshal.PtrToStructure<CResultWord>(resultsHandle);
          string s = Marshal.PtrToStringAnsi(resultWord.word);  // @TODO Use something like Marshal.PtrToStringUTF8 (Net Standard 2.1 only)
          endOfAudioStream = String.IsNullOrEmpty(s);
          if (!CTS.Token.IsCancellationRequested) {
            string contextId = null;

            if (!endOfAudioStream) {
              activeContexts.TryPeek(out contextId);
              if (s != segmentEndToken) {
                var msgTranscript = new MsgTranscript{
                  data = new Word{word = s, startTimestamp = resultWord.start_time, endTimestamp = resultWord.end_time, index = wordCounter, isFinal = true}
                };
                OnMessage(new MsgCommon{type = "transcript", audio_context = contextId, segment_id = segmentIndex}, JSON.Stringify(msgTranscript));
                wordCounter++;
              } else {
                OnMessage(new MsgCommon{type = "segment_end", audio_context = contextId, segment_id = segmentIndex}, "");
                segmentIndex++;
                wordCounter = 0;
              }
            } else {
              activeContexts.TryDequeue(out contextId);
              if (wordCounter > 0) {
                OnMessage(new MsgCommon{type = "segment_end", audio_context = contextId, segment_id = segmentIndex}, "");
              }
              OnMessage(new MsgCommon{type = "stopped", audio_context = contextId, segment_id = segmentIndex}, "");
            }
          }
          CResultWord_Destroy(resultsHandle);
        }
      } catch (TaskCanceledException) {
        Logger.LogError("Whoopsie, an exception...");
        throw;
      }
    }

    override internal async Task Shutdown()
    {
      if (debug) Logger.Log("On-device SLU shutting down...");
      if (CTS != null) {
        CTS.Cancel();
      }
      if (decoderHandle != null) {
        // Send end of audio to exit Decoder_WaitResults and ultimately ReceiveLoop thread
        float[] dummy = new float[0];
        SendAudio(dummy, 0, dummy.Length, true);
        await receiveTask;
        if (debug) Logger.Log("Cleaning up...");
        Decoder_Destroy(decoderHandle);
      }
      if (decoderFactoryHandle != null) {
        DecoderFactory_Destroy(decoderFactoryHandle);
      }
      if (debug) Logger.Log("Completed shutdown");
    }
  }
}
